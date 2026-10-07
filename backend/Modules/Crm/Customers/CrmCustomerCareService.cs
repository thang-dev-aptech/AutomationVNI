using System.Globalization;
using System.Text;
using Backend.Data;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialComment;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Customers;

/// <summary>Timeline, ghi chú, tạo tay, CSV, xoá — phần care của CRM-CUSTOMER-01.</summary>
public class CrmCustomerCareService(AppDbContext db, IUserContext userContext, CrmCustomerService customers)
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly TimeZoneInfo VietnamTz = ResolveVietnamTz();

    public async Task<CrmCustomerDetailResponse> CreateManualAsync(
        CreateCrmCustomerRequest request, CancellationToken ct = default)
    {
        var name = (request.DisplayName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên khách bắt buộc");

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneE164))
        {
            phone = VietnamesePhoneNormalizer.TryNormalize(request.PhoneE164)
                    ?? throw new InvalidOperationException("Số điện thoại không hợp lệ");
            var dup = await db.CrmCustomers.AnyAsync(x => !x.IsDeleted && x.PhoneE164 == phone, ct);
            if (dup)
                throw new InvalidOperationException("Số điện thoại đã thuộc khách khác — hãy gộp hồ sơ");
        }

        var entity = new CrmCustomerModel
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            PhoneE164 = phone,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmCustomers.Add(entity);
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.Id, "Create", name, ct);
        return (await customers.GetAsync(entity.Id, ct))!;
    }

    public async Task<CrmCustomerDetailResponse?> UpdateAsync(
        Guid id, UpdateCrmCustomerRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;

        var name = (request.DisplayName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên khách bắt buộc");

        string? phone = entity.PhoneE164;
        if (request.PhoneE164 is not null)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneE164))
                phone = null;
            else
            {
                phone = VietnamesePhoneNormalizer.TryNormalize(request.PhoneE164)
                        ?? throw new InvalidOperationException("Số điện thoại không hợp lệ");
                var dup = await db.CrmCustomers.AnyAsync(x =>
                    !x.IsDeleted && x.PhoneE164 == phone && x.Id != id, ct);
                if (dup)
                    throw new InvalidOperationException("Số điện thoại đã thuộc khách khác");
            }
        }

        entity.DisplayName = name;
        entity.PhoneE164 = phone;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(id, "Update", $"{name}|{phone}", ct);
        return await customers.GetAsync(id, ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                     ?? throw new KeyNotFoundException("Khách không tồn tại");
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userContext.GetCurrentUserName();
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(id, "SoftDelete", null, ct);
    }

    public async Task HardDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmCustomers.FirstOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw new KeyNotFoundException("Khách không tồn tại");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.CrmCustomerIdentities.Where(x => x.CrmCustomerId == id).ExecuteDeleteAsync(ct);
        await db.CrmCustomerNotes.Where(x => x.CrmCustomerId == id).ExecuteDeleteAsync(ct);
        await db.CrmCustomerReminders.Where(x => x.CrmCustomerId == id).ExecuteDeleteAsync(ct);
        await db.CrmCustomerTagLinks.Where(x => x.CrmCustomerId == id).ExecuteDeleteAsync(ct);
        await db.CrmCustomerPhoneSuggestions.Where(x => x.CrmCustomerId == id).ExecuteDeleteAsync(ct);
        await db.CrmCustomerActionLogs.Where(x => x.CrmCustomerId == id).ExecuteDeleteAsync(ct);
        await db.CrmCustomerMergeRecords
            .Where(x => x.KeptCustomerId == id || x.MergedCustomerId == id)
            .ExecuteDeleteAsync(ct);
        db.CrmCustomers.Remove(entity);
        await db.SaveChangesAsync(ct);
        // Log sau khi xoá khách: gắn CrmCustomerId = null, payload giữ id
        await customers.AddLogAsync(null, "HardDelete", id.ToString(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<CrmCustomerNoteResponse>> ListNotesAsync(
        Guid customerId, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(customerId, ct);
        return await db.CrmCustomerNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customerId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CrmCustomerNoteResponse
            {
                Id = x.Id,
                CrmCustomerId = x.CrmCustomerId,
                Body = x.Body,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<CrmCustomerNoteResponse> AddNoteAsync(
        Guid customerId, CreateCrmCustomerNoteRequest request, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(customerId, ct);
        var body = (request.Body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Nội dung ghi chú bắt buộc");

        var note = new CrmCustomerNoteModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customerId,
            Body = body,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmCustomerNotes.Add(note);
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(customerId, "AddNote", note.Id.ToString(), ct);
        return ToNote(note);
    }

    public async Task<CrmCustomerNoteResponse?> UpdateNoteAsync(
        Guid customerId, Guid noteId, UpdateCrmCustomerNoteRequest request, CancellationToken ct = default)
    {
        var note = await db.CrmCustomerNotes.FirstOrDefaultAsync(x =>
            x.Id == noteId && x.CrmCustomerId == customerId && !x.IsDeleted, ct);
        if (note is null) return null;
        var body = (request.Body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Nội dung ghi chú bắt buộc");
        note.Body = body;
        note.UpdatedAt = DateTime.UtcNow;
        note.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(customerId, "UpdateNote", noteId.ToString(), ct);
        return ToNote(note);
    }

    public async Task SoftDeleteNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default)
    {
        var note = await db.CrmCustomerNotes.FirstOrDefaultAsync(x =>
            x.Id == noteId && x.CrmCustomerId == customerId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Ghi chú không tồn tại");
        note.IsDeleted = true;
        note.DeletedAt = DateTime.UtcNow;
        note.DeletedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(customerId, "DeleteNote", noteId.ToString(), ct);
    }

    public async Task<IReadOnlyList<CrmTimelineItemResponse>> GetTimelineAsync(
        Guid customerId, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(customerId, ct);
        var identities = await db.CrmCustomerIdentities.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customerId)
            .ToListAsync(ct);

        var items = new List<CrmTimelineItemResponse>();
        var channelIds = identities.Select(x => x.SocialChannelId).Distinct().ToList();
        var channelNames = await db.SocialChannels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PageName, ct);

        foreach (var idn in identities)
        {
            var conversations = await db.PageConversations.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.SocialChannelId == idn.SocialChannelId
                            && x.ParticipantExternalId == idn.ExternalId)
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (conversations.Count > 0)
            {
                var messages = await db.PageMessages.AsNoTracking()
                    .Where(x => !x.IsDeleted && conversations.Contains(x.PageConversationId))
                    .Select(x => new { x.Id, x.Text, x.SentAt, x.CreatedAt, x.IsFromPage, x.SocialChannelId })
                    .ToListAsync(ct);
                foreach (var m in messages)
                {
                    items.Add(new CrmTimelineItemResponse
                    {
                        Kind = m.IsFromPage ? "page-message" : "customer-message",
                        At = m.SentAt ?? m.CreatedAt,
                        Title = m.IsFromPage ? "Tin trang gửi" : "Tin khách gửi",
                        Body = m.Text,
                        RefId = m.Id,
                        SocialChannelId = m.SocialChannelId,
                        ChannelName = channelNames.GetValueOrDefault(m.SocialChannelId)
                    });
                }

                var msgLogs = await db.MessageActionLogs.AsNoTracking()
                    .Where(x => !x.IsDeleted && conversations.Contains(x.PageConversationId)
                                && (x.ActionType == MessageActionType.Assign
                                    || x.ActionType == MessageActionType.SetStatus))
                    .Select(x => new { x.Id, x.ActionType, x.PayloadJson, x.ActorUserName, x.CreatedAt })
                    .ToListAsync(ct);
                foreach (var log in msgLogs)
                {
                    items.Add(new CrmTimelineItemResponse
                    {
                        Kind = log.ActionType == MessageActionType.Assign ? "assign" : "status",
                        At = log.CreatedAt,
                        Title = log.ActionType == MessageActionType.Assign
                            ? "Đổi người phụ trách (tin nhắn)"
                            : "Đổi trạng thái (tin nhắn)",
                        Body = log.PayloadJson,
                        RefId = log.Id,
                        Actor = log.ActorUserName,
                        SocialChannelId = idn.SocialChannelId,
                        ChannelName = channelNames.GetValueOrDefault(idn.SocialChannelId)
                    });
                }
            }

            var comments = await db.SocialComments.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.SocialChannelId == idn.SocialChannelId
                            && x.AuthorExternalId == idn.ExternalId)
                .Select(x => new
                {
                    x.Id, x.Message, x.CommentedAt, x.CreatedAt, x.InboxStatus,
                    x.AssignedTo, x.SocialChannelId
                })
                .ToListAsync(ct);
            foreach (var c in comments)
            {
                items.Add(new CrmTimelineItemResponse
                {
                    Kind = "comment",
                    At = c.CommentedAt ?? c.CreatedAt,
                    Title = "Bình luận",
                    Body = c.Message,
                    RefId = c.Id,
                    SocialChannelId = c.SocialChannelId,
                    ChannelName = channelNames.GetValueOrDefault(c.SocialChannelId)
                });
            }

            var commentIds = comments.Select(x => x.Id).ToList();
            if (commentIds.Count > 0)
            {
                var cLogs = await db.CommentActionLogs.AsNoTracking()
                    .Where(x => !x.IsDeleted && commentIds.Contains(x.SocialCommentId))
                    .Select(x => new { x.Id, x.ActionType, x.ActorUserName, x.PayloadJson, x.CreatedAt })
                    .ToListAsync(ct);
                foreach (var log in cLogs)
                {
                    items.Add(new CrmTimelineItemResponse
                    {
                        Kind = "comment-action",
                        At = log.CreatedAt,
                        Title = $"Thao tác bình luận ({log.ActionType})",
                        Body = log.PayloadJson,
                        RefId = log.Id,
                        Actor = log.ActorUserName,
                        SocialChannelId = idn.SocialChannelId,
                        ChannelName = channelNames.GetValueOrDefault(idn.SocialChannelId)
                    });
                }
            }
        }

        var notes = await db.CrmCustomerNotes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customerId)
            .Select(x => new { x.Id, x.Body, x.CreatedAt, x.CreatedBy })
            .ToListAsync(ct);
        foreach (var n in notes)
        {
            items.Add(new CrmTimelineItemResponse
            {
                Kind = "note",
                At = n.CreatedAt,
                Title = "Ghi chú",
                Body = n.Body,
                RefId = n.Id,
                Actor = n.CreatedBy
            });
        }

        var custLogs = await db.CrmCustomerActionLogs.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customerId
                        && (x.ActionType == "Merge" || x.ActionType == "Split"
                            || x.ActionType == "Update" || x.ActionType == "ConfirmPhone"
                            || x.ActionType == "Create"))
            .Select(x => new { x.Id, x.ActionType, x.ActorUserName, x.PayloadJson, x.CreatedAt })
            .ToListAsync(ct);
        foreach (var log in custLogs)
        {
            items.Add(new CrmTimelineItemResponse
            {
                Kind = "customer-change",
                At = log.CreatedAt,
                Title = log.ActionType,
                Body = log.PayloadJson,
                RefId = log.Id,
                Actor = log.ActorUserName
            });
        }

        return items.OrderByDescending(x => x.At).ToList();
    }

    public async Task<CrmCsvPreviewResponse> PreviewCsvAsync(IFormFile file, CancellationToken ct = default)
    {
        var rows = await ParseCsvRowsAsync(file, ct);
        return new CrmCsvPreviewResponse
        {
            TotalRows = rows.Count,
            CreateCount = rows.Count(r => r.Status == "create"),
            DuplicatePhoneCount = rows.Count(r => r.Status == "duplicate_phone"),
            InvalidCount = rows.Count(r => r.Status == "invalid"),
            Rows = rows
        };
    }

    public async Task<CrmCsvCommitResponse> CommitCsvAsync(IFormFile file, CancellationToken ct = default)
    {
        var rows = await ParseCsvRowsAsync(file, ct);
        var result = new CrmCsvCommitResponse
        {
            Invalid = rows.Count(r => r.Status == "invalid"),
            SkippedDuplicatePhone = rows.Count(r => r.Status == "duplicate_phone")
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var row in rows.Where(r => r.Status == "create"))
        {
            var entity = new CrmCustomerModel
            {
                Id = Guid.NewGuid(),
                DisplayName = row.DisplayName,
                PhoneE164 = row.PhoneE164,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userContext.GetCurrentUserName()
            };
            db.CrmCustomers.Add(entity);
            if (!string.IsNullOrWhiteSpace(row.Note))
            {
                db.CrmCustomerNotes.Add(new CrmCustomerNoteModel
                {
                    Id = Guid.NewGuid(),
                    CrmCustomerId = entity.Id,
                    Body = row.Note!,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userContext.GetCurrentUserName()
                });
            }

            result.CreatedCustomerIds.Add(entity.Id);
            result.Created++;
        }

        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(null, "CsvImport",
            $"created={result.Created};dup={result.SkippedDuplicatePhone}", ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<(byte[] Bytes, string FileName)> ExportCsvAsync(CancellationToken ct = default)
    {
        var rows = await db.CrmCustomers.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.DisplayName)
            .Select(x => new { x.Id, x.DisplayName, x.PhoneE164, x.CreatedAt })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,Tên,Số điện thoại,Ngày tạo");
        foreach (var c in rows)
        {
            sb.Append(Escape(c.Id.ToString())).Append(',')
                .Append(Escape(c.DisplayName)).Append(',')
                .Append(Escape(c.PhoneE164 ?? "")).Append(',')
                .Append(Escape(c.CreatedAt.ToString("o", CultureInfo.InvariantCulture)))
                .AppendLine();
        }

        await customers.AddLogAsync(null, "CsvExport", $"count={rows.Count}", ct);
        var body = Utf8Bom.GetBytes(sb.ToString());
        var preamble = Utf8Bom.GetPreamble();
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return (bytes, $"crm-customers-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public byte[] GetCsvTemplateBytes()
    {
        var body = Utf8Bom.GetBytes("Tên,Số điện thoại,Ghi chú\r\n");
        var preamble = Utf8Bom.GetPreamble();
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }

    private async Task<List<CrmCsvPreviewRow>> ParseCsvRowsAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > 2 * 1024 * 1024)
            throw new InvalidOperationException("File CSV không hợp lệ hoặc quá lớn");

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            throw new InvalidOperationException("CSV cần dòng tiêu đề và ít nhất một dòng dữ liệu");

        var header = SplitCsv(lines[0]);
        var nameIdx = FindHeader(header, "tên", "ten", "name", "displayName", "họ tên");
        var phoneIdx = FindHeader(header, "số điện thoại", "so dien thoai", "phone", "phonee164", "sđt", "sdt");
        var noteIdx = FindHeader(header, "ghi chú", "ghi chu", "note", "notes");
        if (nameIdx < 0)
            throw new InvalidOperationException("CSV thiếu cột Tên");

        var existingPhones = await db.CrmCustomers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.PhoneE164 != null)
            .Select(x => new { x.Id, x.PhoneE164 })
            .ToListAsync(ct);
        var phoneMap = existingPhones
            .Where(x => !string.IsNullOrWhiteSpace(x.PhoneE164))
            .GroupBy(x => x.PhoneE164!)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var rows = new List<CrmCsvPreviewRow>();
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = SplitCsv(lines[i]);
            var row = new CrmCsvPreviewRow { LineNumber = i + 1 };
            row.DisplayName = GetCell(cells, nameIdx).Trim();
            row.PhoneRaw = phoneIdx >= 0 ? GetCell(cells, phoneIdx).Trim() : null;
            row.Note = noteIdx >= 0 ? GetCell(cells, noteIdx).Trim() : null;
            if (string.IsNullOrWhiteSpace(row.Note)) row.Note = null;

            if (string.IsNullOrWhiteSpace(row.DisplayName))
            {
                row.Status = "invalid";
                row.Message = "Thiếu tên";
                rows.Add(row);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.PhoneRaw))
            {
                var e164 = VietnamesePhoneNormalizer.TryNormalize(row.PhoneRaw);
                if (e164 is null)
                {
                    row.Status = "invalid";
                    row.Message = "Số điện thoại không hợp lệ";
                    rows.Add(row);
                    continue;
                }

                row.PhoneE164 = e164;
                if (!seenInFile.Add(e164))
                {
                    row.Status = "invalid";
                    row.Message = "Trùng số trong file";
                    rows.Add(row);
                    continue;
                }

                if (phoneMap.TryGetValue(e164, out var existingId))
                {
                    row.Status = "duplicate_phone";
                    row.ExistingCustomerId = existingId;
                    row.Message = "Số đã có — gợi ý gộp, không ghi đè";
                    rows.Add(row);
                    continue;
                }
            }

            row.Status = "create";
            rows.Add(row);
        }

        return rows;
    }

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken ct)
    {
        if (!await db.CrmCustomers.AnyAsync(x => x.Id == customerId && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Khách không tồn tại");
    }

    private static CrmCustomerNoteResponse ToNote(CrmCustomerNoteModel n) => new()
    {
        Id = n.Id,
        CrmCustomerId = n.CrmCustomerId,
        Body = n.Body,
        CreatedAt = n.CreatedAt,
        CreatedBy = n.CreatedBy,
        UpdatedAt = n.UpdatedAt
    };

    private static TimeZoneInfo ResolveVietnamTz()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }

    internal static DateTime ToVietnamLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTz);

    internal static DateTime VietnamTodayStartUtc()
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTz);
        var startLocal = localNow.Date;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), VietnamTz);
    }

    private static int FindHeader(string[] header, params string[] aliases)
    {
        for (var i = 0; i < header.Length; i++)
        {
            var h = header[i].Trim().ToLowerInvariant();
            if (aliases.Any(a => h == a.ToLowerInvariant()))
                return i;
        }

        return -1;
    }

    private static string GetCell(string[] cells, int idx)
        => idx >= 0 && idx < cells.Length ? cells[idx] : "";

    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if ((c == ',' || c == ';') && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
