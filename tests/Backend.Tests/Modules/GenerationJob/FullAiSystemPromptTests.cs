using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Backend.Shared.Ai;
using Xunit;

namespace Backend.Tests.Modules.GenerationJob;

/// <summary>
/// R-029: khối "Cấu trúc caption" được dùng chung với caption sinh từ ảnh, nên SystemPrompt của Full AI giờ
/// được ghép từ ba hằng. Văn bản ghép lại phải GIỐNG HỆT bản trước khi tách (SHA-256 + độ dài lấy từ
/// bản cũ) — nếu ai sửa prompt Full AI một cách cố ý thì cập nhật hash này cùng lúc.
/// </summary>
public sealed class FullAiSystemPromptTests
{
    private const string PreviousSha256 = "B2236558FB4A72848C178A974FADBAB47010B65971A7C18CC325F462BCA417F3";
    private const int PreviousLength = 1644;

    private static string FullAiSystemPrompt()
        => (string)typeof(OpenAiCompatibleTextGenerationService)
            .GetField("SystemPrompt", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    [Fact]
    public void FullAiSystemPrompt_IsByteIdenticalToTheVersionBeforeSharing()
    {
        var text = FullAiSystemPrompt();

        Assert.Equal(PreviousLength, text.Length);
        Assert.Equal(PreviousSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void BothPrompts_EmbedTheSameStructureBlock()
    {
        Assert.Contains(FacebookCaptionRules.Structure, FullAiSystemPrompt());
        Assert.Contains(FacebookCaptionRules.Structure, FacebookCaptionRules.ImageCaptionSystemPrompt);
        Assert.Contains("Cấu trúc caption (bắt buộc)", FacebookCaptionRules.Structure);
    }
}
