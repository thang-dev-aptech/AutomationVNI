using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialConnection;

namespace Backend.Shared.TikTok;

public class TikTokProfileSyncService(
    SocialChannelRepository socialChannelRepository,
    SocialConnectionRepository socialConnectionRepository)
{
    /// <summary>
    /// Lưu connection + đúng 1 channel TikTok. Giống Threads: không có picker chọn nhiều account,
    /// một lần authorize ra đúng 1 tài khoản TikTok (open_id).
    /// </summary>
    public async Task<TikTokOAuthCallbackResult> SyncAsync(
        TikTokUserProfileDto profile,
        TikTokTokenResult token,
        string? scopes,
        string? auditUser,
        CancellationToken ct = default)
    {
        var connection = await socialConnectionRepository.UpsertFromProviderAsync(
            SocialProvider.TikTok,
            profile.OpenId,
            profile.Label,
            profile.AvatarUrl,
            scopes,
            auditUser,
            ct);

        await socialChannelRepository.UpsertFromMetaAsync(
            SocialPlatform.TikTok,
            SocialChannelType.TikTok,
            profile.OpenId,
            profile.Label,
            token.AccessToken,
            token.AccessTokenExpiresAt,
            connection.Id,
            null, // extraJson — không cần cho TikTok, RefreshToken đã có cột riêng
            auditUser,
            ct,
            refreshToken: token.RefreshToken);

        var result = new TikTokOAuthCallbackResult
        {
            SocialConnectionId = connection.Id,
            ProfilesSynced = 1
        };

        result.ChannelsRemoved = await socialChannelRepository.SoftDeleteMissingFromMetaAsync(
            connection.Id,
            [(SocialPlatform.TikTok, profile.OpenId)],
            auditUser,
            ct);

        return result;
    }
}
