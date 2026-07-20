using System.Text.Json;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialConnection;

namespace Backend.Shared.Threads;

public class ThreadsProfileSyncService(
    SocialChannelRepository socialChannelRepository,
    SocialConnectionRepository socialConnectionRepository)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Persist the connection + its single Threads channel. Unlike Meta there is no page picker —
    /// one authorization grants exactly one Threads profile.
    /// </summary>
    public async Task<ThreadsOAuthCallbackResult> SyncAsync(
        ThreadsUserProfileDto profile,
        string accessToken,
        DateTime tokenExpiresAt,
        string? scopes,
        string? auditUser,
        CancellationToken ct = default)
    {
        var connection = await socialConnectionRepository.UpsertFromProviderAsync(
            SocialProvider.Threads,
            profile.Id,
            profile.DisplayName,
            profile.PictureUrl,
            scopes,
            auditUser,
            ct);

        var extraJson = JsonSerializer.Serialize(new
        {
            username = profile.Username,
            profileName = profile.Name
        }, JsonOptions);

        // Threads long-lived tokens expire in 60 days — a real TokenExpiresAt is required so
        // ThreadsTokenRefreshService can find and renew them before they die permanently.
        await socialChannelRepository.UpsertFromMetaAsync(
            SocialPlatform.Threads,
            SocialChannelType.Threads,
            profile.Id,
            profile.DisplayName,
            accessToken,
            tokenExpiresAt,
            connection.Id,
            extraJson,
            auditUser,
            ct);

        var result = new ThreadsOAuthCallbackResult
        {
            SocialConnectionId = connection.Id,
            ProfilesSynced = 1
        };

        // Drop channels this connection no longer covers (e.g. a previously synced profile id).
        result.ChannelsRemoved = await socialChannelRepository.SoftDeleteMissingFromMetaAsync(
            connection.Id,
            [(SocialPlatform.Threads, profile.Id)],
            auditUser,
            ct);

        return result;
    }
}
