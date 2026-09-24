using System.Diagnostics;
using System.Text;
using Backend.Shared.SocialPublish;
using Microsoft.Extensions.Options;

namespace Backend.Modules.MediaAsset;

/// <summary>
/// Ghép N ảnh khung hình (đã ghép chữ qua RichTemplateRenderService) thành 1 video slideshow .mp4
/// cho Facebook Reels (9:16, 1080x1920, H.264 + AAC) bằng FFmpeg — gọi qua Process.Start như tiến
/// trình con tách biệt (KHÔNG link libavcodec/libx264 vào code .NET), giữ an toàn giấy phép GPL
/// khi thương mại hoá (xem REELS_PLAN.md phần C4).
/// </summary>
public class SlideshowVideoRenderService(
    IOptions<ReelsOptions> options,
    ILogger<SlideshowVideoRenderService> logger)
{
    private static readonly string DefaultPadColor = "#0A2846";

    /// <param name="audioTrackPathOverride">
    /// Đường dẫn file nhạc tạm (từ thư viện nhạc, đã copy ra temp file) — có giá trị thì dùng thay
    /// cho <see cref="ReelsOptions.AudioTrackPath"/> mặc định hệ thống. Null = giữ hành vi cũ.
    /// </param>
    public async Task<byte[]> RenderAsync(
        List<byte[]> frames, string? brandColorHex, string? audioTrackPathOverride = null,
        CancellationToken ct = default)
    {
        if (frames.Count == 0)
            throw new ArgumentException("Cần ít nhất 1 khung hình để dựng video", nameof(frames));

        var reels = options.Value;
        var ffmpegPath = ResolveFfmpegPath(reels.FfmpegPath);
        if (!File.Exists(ffmpegPath))
            throw new InvalidOperationException(
                $"Không tìm thấy FFmpeg binary tại {ffmpegPath} — kiểm tra Resources/ffmpeg đã được copy vào thư mục publish chưa.");

        var workDir = Path.Combine(Path.GetTempPath(), "reels-render", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var framePaths = new List<string>();
            for (var i = 0; i < frames.Count; i++)
            {
                var framePath = Path.Combine(workDir, $"frame-{i:D3}.png");
                await File.WriteAllBytesAsync(framePath, frames[i], ct);
                framePaths.Add(framePath);
            }

            var concatListPath = Path.Combine(workDir, "concat.txt");
            await File.WriteAllTextAsync(concatListPath, BuildConcatList(framePaths, reels.SecondsPerFrame), ct);

            var padColor = string.IsNullOrWhiteSpace(brandColorHex) ? DefaultPadColor : brandColorHex.Trim();
            var outputPath = Path.Combine(workDir, "output.mp4");

            var audioTrackPath = string.IsNullOrWhiteSpace(audioTrackPathOverride)
                ? reels.AudioTrackPath
                : audioTrackPathOverride;
            var hasAudio = !string.IsNullOrWhiteSpace(audioTrackPath)
                && File.Exists(ResolveFfmpegPath(audioTrackPath));

            var args = new List<string>
            {
                "-y",
                "-f", "concat", "-safe", "0", "-i", concatListPath
            };
            if (hasAudio)
            {
                args.AddRange(["-i", ResolveFfmpegPath(audioTrackPath!)]);
            }
            args.AddRange([
                "-vf", $"scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2:color={padColor}",
                "-r", "30",
                "-c:v", "libx264", "-pix_fmt", "yuv420p",
            ]);
            if (hasAudio)
            {
                args.AddRange(["-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-shortest"]);
            }
            else
            {
                args.Add("-an");
            }
            args.Add(outputPath);

            logger.LogInformation(
                "Bắt đầu render Reels: {FrameCount} khung hình, hasAudio={HasAudio}", frames.Count, hasAudio);
            await RunFfmpegAsync(ffmpegPath, args, reels.FfmpegTimeoutSeconds, ct);

            if (!File.Exists(outputPath))
                throw new InvalidOperationException("FFmpeg chạy xong nhưng không thấy file video output");

            return await File.ReadAllBytesAsync(outputPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { logger.LogWarning(ex, "Không dọn được thư mục tạm {WorkDir}", workDir); }
        }
    }

    /// <summary>
    /// Concat demuxer của FFmpeg bỏ qua duration của dòng CUỐI trừ khi file đó lặp lại thêm 1 lần
    /// không kèm duration — quirk đã biết của FFmpeg, không phải bug ở đây.
    /// </summary>
    private static string BuildConcatList(List<string> framePaths, double secondsPerFrame)
    {
        var sb = new StringBuilder();
        foreach (var path in framePaths)
        {
            sb.AppendLine($"file '{path.Replace("'", "'\\''")}'");
            sb.AppendLine($"duration {secondsPerFrame.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        sb.AppendLine($"file '{framePaths[^1].Replace("'", "'\\''")}'");
        return sb.ToString();
    }

    private static string ResolveFfmpegPath(string configuredPath)
        => Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Directory.GetCurrentDirectory(), configuredPath);

    private async Task RunFfmpegAsync(
        string ffmpegPath, List<string> args, int timeoutSeconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            // Bắt buộc redirect stdin rồi đóng ngay bên dưới — không thì stdin của FFmpeg kế thừa
            // thẳng từ tiến trình backend. Backend chạy nền dài hạn (không phải terminal thật), nên
            // handle kế thừa xuống có thể trỏ tới 1 pipe không bao giờ đóng/không bao giờ có dữ liệu.
            // FFmpeg mặc định chạy 1 luồng riêng đọc lệnh tương tác từ stdin (q để dừng...) — gặp
            // đúng tình huống đó thì luồng này treo vô thời hạn, không log gì, không lỗi gì, vì phần
            // mã hoá video (đọc qua -i, tách hẳn khỏi stdin) vẫn coi như "đang chạy" bình thường.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // -nostdin: tự bảo FFmpeg đừng đợi input tương tác — cùng mục đích với đóng stdin ở trên,
        // giữ cả hai làm 2 lớp chặn độc lập cho cùng 1 nguyên nhân.
        psi.ArgumentList.Add("-nostdin");
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        // Log nguyên lệnh sẽ chạy. Khi FFmpeg treo, đây là thứ duy nhất cho phép chạy lại y hệt
        // bằng tay trên server để tái hiện — không có nó thì chỉ còn cách đoán tham số.
        logger.LogInformation("Chạy FFmpeg: {Path} {Args}", ffmpegPath, string.Join(' ', psi.ArgumentList));

        var startedAt = DateTime.UtcNow;
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Không khởi động được tiến trình FFmpeg");
        process.StandardInput.Close();

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Hết timeout riêng của FFmpeg (không phải job bị huỷ từ ngoài) — lưới an toàn thứ 2,
            // phòng FFmpeg treo vì lý do khác ngoài stdin (ảnh input hỏng, đĩa đầy...).
            TryKill(process);

            // Vét stderr mà FFmpeg đã kịp in ra TRƯỚC khi treo — phần này chứa banner phiên bản,
            // thông tin stream đã parse được và dòng tiến độ cuối cùng, tức là biết được nó đứng ở
            // bước nào. Sau khi Kill thì pipe đóng nên ReadToEndAsync trả về ngay; vẫn bọc timeout
            // ngắn phòng trường hợp pipe không đóng, để không treo tiếp ngay trong nhánh xử lý treo.
            var partialStderr = await ReadWithGraceAsync(stderrTask);
            logger.LogError(
                "FFmpeg treo quá {Timeout}s — đã kill. stderr đọc được tới lúc treo: {Stderr}",
                timeoutSeconds, string.IsNullOrWhiteSpace(partialStderr) ? "(rỗng)" : Limit(partialStderr, 4000));

            throw new InvalidOperationException(
                $"FFmpeg không hoàn tất sau {timeoutSeconds}s — đã huỷ tiến trình");
        }

        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0)
        {
            logger.LogError("FFmpeg thoát với mã {ExitCode}: {Stderr}", process.ExitCode, Limit(stderr, 2000));
            throw new InvalidOperationException($"FFmpeg lỗi (exit code {process.ExitCode}) — xem log để biết chi tiết");
        }

        logger.LogInformation(
            "FFmpeg xong sau {Seconds:F1}s", (DateTime.UtcNow - startedAt).TotalSeconds);
    }

    /// <summary>Đọc nốt stream đã kill, tối đa 5s — không để nhánh xử lý treo lại treo tiếp.</summary>
    private static async Task<string> ReadWithGraceAsync(Task<string> readTask)
    {
        var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)));
        return completed == readTask ? await readTask : string.Empty;
    }

    private void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) { logger.LogWarning(ex, "Không kill được tiến trình FFmpeg quá hạn"); }
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
