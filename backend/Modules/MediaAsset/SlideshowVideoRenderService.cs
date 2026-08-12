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

    public async Task<byte[]> RenderAsync(
        List<byte[]> frames, string? brandColorHex, CancellationToken ct = default)
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

            var hasAudio = !string.IsNullOrWhiteSpace(reels.AudioTrackPath)
                && File.Exists(ResolveFfmpegPath(reels.AudioTrackPath));

            var args = new List<string>
            {
                "-y",
                "-f", "concat", "-safe", "0", "-i", concatListPath
            };
            if (hasAudio)
            {
                args.AddRange(["-i", ResolveFfmpegPath(reels.AudioTrackPath!)]);
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

            await RunFfmpegAsync(ffmpegPath, args, ct);

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

    private async Task RunFfmpegAsync(string ffmpegPath, List<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Không khởi động được tiến trình FFmpeg");

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0)
        {
            logger.LogError("FFmpeg thoát với mã {ExitCode}: {Stderr}", process.ExitCode, Limit(stderr, 2000));
            throw new InvalidOperationException($"FFmpeg lỗi (exit code {process.ExitCode}) — xem log để biết chi tiết");
        }
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
