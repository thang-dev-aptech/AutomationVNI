using System.Globalization;
using System.Text;
using Backend.Data;
using Backend.Modules.SocialChannel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.ChannelGroup;

/// <summary>
/// Nhập nhóm kênh từ CSV (CHANNEL-GROUP-01 / channel-group-csv-import).
/// Tự parse CSV (BOM, ngoặc kép, ","/";") — không thêm thư viện.
/// </summary>
public class ChannelGroupImportService(AppDbContext context, ChannelGroupRepository repository)
{
    public const long MaxFileBytes = 1 * 1024 * 1024;
    public const int MaxDataRows = 5000;

    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public byte[] GetTemplateBytes()
    {
        var preamble = Utf8Bom.GetPreamble();
        var body = Utf8Bom.GetBytes("Tên nhóm,ID Page,Mô tả\r\n");
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }

    public async Task<ChannelGroupImportPreviewResponse> PreviewAsync(
        IFormFile file, string mode, CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(file, mode, ct);
        return ToPreviewResponse(plan);
    }

    public async Task<ChannelGroupImportCommitResponse> CommitAsync(
        IFormFile file, string mode, CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(file, mode, ct);

        var created = 0;
        var updated = 0;
        var touchedIds = new List<Guid>();

        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var group in plan.Groups)
            {
                // Nhóm không còn dòng hợp lệ nào → không đụng (kể cả replace).
                if (group.ValidChannelIds.Count == 0)
                    continue;

                if (group.IsNew)
                {
                    var entity = await repository.CreateAsync(new CreateChannelGroupRequest
                    {
                        Name = group.Name,
                        Description = group.Description,
                        ChannelIds = group.FinalChannelIds
                    }, ct);
                    created++;
                    touchedIds.Add(entity.Id);
                }
                else
                {
                    var entity = await repository.UpdateAsync(group.ExistingGroupId!.Value,
                        new UpdateChannelGroupRequest
                        {
                            Description = group.Description,
                            ChannelIds = group.FinalChannelIds
                        }, ct);
                    if (entity is null)
                        throw new InvalidOperationException($"Không tìm thấy nhóm '{group.Name}'.");
                    updated++;
                    touchedIds.Add(entity.Id);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        var responses = new List<ChannelGroupResponse>();
        foreach (var id in touchedIds)
        {
            var r = await repository.GetResponseByIdAsync(id, ct);
            if (r is not null) responses.Add(r);
        }

        return new ChannelGroupImportCommitResponse
        {
            Mode = plan.Mode,
            GroupsCreated = created,
            GroupsUpdated = updated,
            ValidRowCount = plan.ValidRowCount,
            Errors = plan.Errors,
            Groups = responses
        };
    }

    private async Task<ImportPlan> BuildPlanAsync(IFormFile file, string mode, CancellationToken ct)
    {
        if (!ChannelGroupImportModes.IsValid(mode))
            throw new ArgumentException("mode phải là merge hoặc replace");

        var normalizedMode = ChannelGroupImportModes.Normalize(mode);
        var text = await ReadCsvTextAsync(file, ct);
        var (headerCells, dataRows, delimiter) = ParseTable(text);

        var nameIdx = FindHeaderIndex(headerCells, "tên nhóm", "ten nhom", "name", "group name");
        var pageIdx = FindHeaderIndex(headerCells, "id page", "idpage", "externalpageid", "page id");
        var descIdx = FindHeaderIndex(headerCells, "mô tả", "mo ta", "description", "desc");

        if (nameIdx < 0 || pageIdx < 0)
            throw new ArgumentException("Dòng tiêu đề bắt buộc có cột \"Tên nhóm\" và \"ID Page\".");

        var pageIds = dataRows
            .Select(r => GetCell(r.Cells, pageIdx))
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var channelLookup = await LoadChannelLookupAsync(pageIds, ct);

        var existingGroups = await context.Set<ChannelGroupModel>()
            .Where(g => !g.IsDeleted)
            .Select(g => new { g.Id, g.Name, g.Description })
            .ToListAsync(ct);
        var existingByName = existingGroups
            .ToDictionary(g => g.Name.Trim().ToLowerInvariant(), g => g, StringComparer.Ordinal);

        var memberRows = await context.Set<ChannelGroupMemberModel>()
            .Where(m => !m.IsDeleted)
            .Select(m => new { m.ChannelGroupId, m.SocialChannelId })
            .ToListAsync(ct);
        var membersByGroup = memberRows
            .GroupBy(m => m.ChannelGroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SocialChannelId).ToHashSet());

        var errors = new List<ChannelGroupImportRowError>();
        var seenPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupAcc = new Dictionary<string, GroupAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataRows)
        {
            var line = row.LineNumber;
            var name = GetCell(row.Cells, nameIdx);
            var pageId = GetCell(row.Cells, pageIdx);
            var desc = descIdx >= 0 ? GetCell(row.Cells, descIdx) : string.Empty;

            if (string.IsNullOrEmpty(name))
            {
                errors.Add(new ChannelGroupImportRowError { Line = line, Reason = "Tên nhóm trống" });
                continue;
            }

            if (string.IsNullOrEmpty(pageId))
            {
                errors.Add(new ChannelGroupImportRowError { Line = line, Reason = "ID Page trống" });
                continue;
            }

            var pairKey = name + "\u001f" + pageId;
            if (!seenPairs.Add(pairKey))
            {
                errors.Add(new ChannelGroupImportRowError { Line = line, Reason = "Dòng trùng" });
                continue;
            }

            if (!channelLookup.TryGetValue(pageId, out var match))
            {
                errors.Add(new ChannelGroupImportRowError
                {
                    Line = line,
                    Reason = "ID Page không tìm thấy"
                });
                continue;
            }

            if (match.Kind == ChannelMatchKind.SoftDeleted)
            {
                errors.Add(new ChannelGroupImportRowError
                {
                    Line = line,
                    Reason = "ID Page khớp kênh đã xoá"
                });
                continue;
            }

            if (match.Kind == ChannelMatchKind.Ambiguous)
            {
                errors.Add(new ChannelGroupImportRowError
                {
                    Line = line,
                    Reason = "ID Page khớp nhiều kênh"
                });
                continue;
            }

            if (!groupAcc.TryGetValue(name, out var acc))
            {
                var key = name.Trim().ToLowerInvariant();
                existingByName.TryGetValue(key, out var existing);
                acc = new GroupAccumulator
                {
                    Name = name.Trim(),
                    IsNew = existing is null,
                    ExistingGroupId = existing?.Id,
                    CurrentChannelIds = existing is null
                        ? []
                        : membersByGroup.GetValueOrDefault(existing.Id, [])
                };
                groupAcc[name] = acc;
            }

            if (!string.IsNullOrEmpty(desc))
                acc.Description = desc;

            acc.ValidChannelIds.Add(match.ChannelId!.Value);
            acc.ValidRowCount++;
        }

        var groups = new List<PlannedGroup>();
        foreach (var acc in groupAcc.Values)
        {
            var imported = acc.ValidChannelIds.ToList();
            HashSet<Guid> final;
            if (acc.IsNew)
            {
                final = imported.ToHashSet();
            }
            else if (normalizedMode == ChannelGroupImportModes.Merge)
            {
                final = acc.CurrentChannelIds.ToHashSet();
                foreach (var id in imported) final.Add(id);
            }
            else
            {
                // replace: chỉ áp khi có ≥1 dòng hợp lệ (đã lọc ở trên khi Count==0 skip)
                final = imported.ToHashSet();
            }

            var added = final.Count(id => !acc.CurrentChannelIds.Contains(id));
            var removed = acc.CurrentChannelIds.Count(id => !final.Contains(id));

            groups.Add(new PlannedGroup
            {
                Name = acc.Name,
                Description = acc.Description,
                IsNew = acc.IsNew,
                ExistingGroupId = acc.ExistingGroupId,
                ValidChannelIds = imported,
                FinalChannelIds = final.ToList(),
                ChannelsAdded = added,
                ChannelsRemoved = removed,
                ValidRowCount = acc.ValidRowCount
            });
        }

        _ = delimiter; // delimiter đã dùng khi parse dòng

        return new ImportPlan
        {
            Mode = normalizedMode,
            ValidRowCount = groups.Sum(g => g.ValidRowCount),
            Groups = groups,
            Errors = errors
        };
    }

    private static ChannelGroupImportPreviewResponse ToPreviewResponse(ImportPlan plan)
        => new()
        {
            Mode = plan.Mode,
            ValidRowCount = plan.ValidRowCount,
            Errors = plan.Errors,
            Groups = plan.Groups
                .Where(g => g.ValidChannelIds.Count > 0)
                .Select(g => new ChannelGroupImportGroupPreview
                {
                    Name = g.Name,
                    Description = g.Description,
                    IsNew = g.IsNew,
                    ExistingGroupId = g.ExistingGroupId,
                    ChannelsAdded = g.ChannelsAdded,
                    ChannelsRemoved = g.ChannelsRemoved,
                    ChannelsFinal = g.FinalChannelIds.Count
                })
                .OrderBy(g => g.Name, StringComparer.Create(new CultureInfo("vi-VN"), ignoreCase: true))
                .ToList()
        };

    private async Task<string> ReadCsvTextAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            throw new ArgumentException("File CSV không hợp lệ hoặc rỗng");

        if (file.Length > MaxFileBytes)
            throw new ArgumentException($"File vượt quá giới hạn {MaxFileBytes} bytes");

        var fileName = file.FileName ?? string.Empty;
        if (!string.IsNullOrEmpty(fileName)
            && !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(file.ContentType)
            && !file.ContentType.Contains("csv", StringComparison.OrdinalIgnoreCase)
            && !file.ContentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase)
            && !file.ContentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File không phải CSV");
        }

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("File CSV không hợp lệ hoặc rỗng");

        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static (string[] Header, List<CsvDataRow> Rows, char Delimiter) ParseTable(string text)
    {
        var rawLines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        // Bỏ dòng trống cuối file do trailing newline
        var lines = new List<(int LineNumber, string Text)>();
        for (var i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];
            if (i == rawLines.Length - 1 && string.IsNullOrWhiteSpace(line))
                continue;
            lines.Add((i + 1, line));
        }

        if (lines.Count == 0)
            throw new ArgumentException("File CSV không hợp lệ hoặc rỗng");

        // Giới hạn tổng số dòng (gồm tiêu đề): header + tối đa MaxDataRows
        if (lines.Count - 1 > MaxDataRows)
            throw new ArgumentException($"File vượt quá giới hạn {MaxDataRows} dòng");

        var delimiter = DetectDelimiter(lines[0].Text);
        var header = ParseCsvLine(lines[0].Text, delimiter);
        if (header.Length == 0 || header.All(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Dòng tiêu đề bắt buộc có cột \"Tên nhóm\" và \"ID Page\".");

        var rows = new List<CsvDataRow>();
        for (var i = 1; i < lines.Count; i++)
        {
            var (lineNumber, lineText) = lines[i];
            if (string.IsNullOrWhiteSpace(lineText))
                continue;
            rows.Add(new CsvDataRow
            {
                LineNumber = lineNumber,
                Cells = ParseCsvLine(lineText, delimiter)
            });
        }

        return (header, rows, delimiter);
    }

    private static char DetectDelimiter(string headerLine)
    {
        var inQuotes = false;
        var commas = 0;
        var semis = 0;
        foreach (var ch in headerLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes) continue;
            if (ch == ',') commas++;
            else if (ch == ';') semis++;
        }

        return semis > commas ? ';' : ',';
    }

    /// <summary>Parse một dòng CSV, hỗ trợ field trong dấu "..." và "" escape.</summary>
    public static string[] ParseCsvLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var s = line ?? string.Empty;
        var i = 0;
        while (i <= s.Length)
        {
            if (i == s.Length)
            {
                cells.Add(string.Empty);
                break;
            }

            if (s[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length)
                {
                    if (s[i] == '"' && i + 1 < s.Length && s[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                        continue;
                    }

                    if (s[i] == '"')
                    {
                        i++;
                        break;
                    }

                    sb.Append(s[i]);
                    i++;
                }

                cells.Add(sb.ToString().Trim());
                if (i < s.Length && s[i] == delimiter) i++;
                continue;
            }

            var next = s.IndexOf(delimiter, i);
            if (next < 0)
            {
                cells.Add(s[i..].Trim());
                break;
            }

            cells.Add(s[i..next].Trim());
            i = next + 1;
        }

        return cells.ToArray();
    }

    private static int FindHeaderIndex(string[] header, params string[] aliases)
    {
        for (var i = 0; i < header.Length; i++)
        {
            var h = NormalizeHeader(header[i]);
            foreach (var alias in aliases)
            {
                if (h == NormalizeHeader(alias))
                    return i;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string GetCell(string[] cells, int index)
        => index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;

    private async Task<Dictionary<string, ChannelMatch>> LoadChannelLookupAsync(
        List<string> pageIds, CancellationToken ct)
    {
        var result = new Dictionary<string, ChannelMatch>(StringComparer.Ordinal);
        if (pageIds.Count == 0) return result;

        var live = await context.Set<SocialChannelModel>()
            .Where(c => !c.IsDeleted && pageIds.Contains(c.ExternalPageId))
            .Select(c => new { c.Id, c.ExternalPageId })
            .ToListAsync(ct);

        var deleted = await context.Set<SocialChannelModel>()
            .Where(c => c.IsDeleted && pageIds.Contains(c.ExternalPageId))
            .Select(c => c.ExternalPageId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var group in live.GroupBy(x => x.ExternalPageId, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                result[group.Key] = new ChannelMatch { Kind = ChannelMatchKind.Ambiguous };
            }
            else
            {
                result[group.Key] = new ChannelMatch
                {
                    Kind = ChannelMatchKind.Found,
                    ChannelId = group.First().Id
                };
            }
        }

        foreach (var ext in deleted)
        {
            if (!result.ContainsKey(ext))
                result[ext] = new ChannelMatch { Kind = ChannelMatchKind.SoftDeleted };
        }

        return result;
    }

    private enum ChannelMatchKind
    {
        Found,
        SoftDeleted,
        Ambiguous
    }

    private sealed class ChannelMatch
    {
        public ChannelMatchKind Kind { get; init; }
        public Guid? ChannelId { get; init; }
    }

    private sealed class CsvDataRow
    {
        public int LineNumber { get; init; }
        public string[] Cells { get; init; } = [];
    }

    private sealed class GroupAccumulator
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsNew { get; set; }
        public Guid? ExistingGroupId { get; set; }
        public HashSet<Guid> CurrentChannelIds { get; set; } = [];
        public HashSet<Guid> ValidChannelIds { get; } = [];
        public int ValidRowCount { get; set; }
    }

    private sealed class PlannedGroup
    {
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsNew { get; init; }
        public Guid? ExistingGroupId { get; init; }
        public List<Guid> ValidChannelIds { get; init; } = [];
        public List<Guid> FinalChannelIds { get; init; } = [];
        public int ChannelsAdded { get; init; }
        public int ChannelsRemoved { get; init; }
        public int ValidRowCount { get; init; }
    }

    private sealed class ImportPlan
    {
        public string Mode { get; init; } = ChannelGroupImportModes.Merge;
        public int ValidRowCount { get; init; }
        public List<PlannedGroup> Groups { get; init; } = [];
        public List<ChannelGroupImportRowError> Errors { get; init; } = [];
    }
}
