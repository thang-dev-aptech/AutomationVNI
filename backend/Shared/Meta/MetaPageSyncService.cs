using System.Text.Json;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialConnection;

namespace Backend.Shared.Meta;

public class MetaPageSyncService(
    SocialChannelRepository socialChannelRepository,
    SocialConnectionRepository socialConnectionRepository)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<MetaOAuthCallbackResult> SyncAsync(
        MetaUserProfileDto profile,
        IEnumerable<MetaPageAccountDto> pages,
        IEnumerable<MetaGroupDto> groups,
        string? scopes,
        string? auditUser,
        CancellationToken ct = default)
    {
        var connection = await socialConnectionRepository.UpsertFromMetaAsync(
            profile.Id,
            profile.Name,
            profile.PictureUrl,
            scopes,
            auditUser,
            ct);

        var result = new MetaOAuthCallbackResult
        {
            SocialConnectionId = connection.Id
        };

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id) || string.IsNullOrWhiteSpace(page.AccessToken))
                continue;

            await socialChannelRepository.UpsertFromMetaAsync(
                SocialPlatform.Facebook,
                SocialChannelType.Page,
                page.Id,
                page.Name,
                page.AccessToken,
                connection.Id,
                extraJson: null,
                auditUser,
                ct);
            result.FacebookPagesSynced++;

            var ig = page.InstagramBusinessAccount;
            if (ig is null || string.IsNullOrWhiteSpace(ig.Id))
                continue;

            var igName = !string.IsNullOrWhiteSpace(ig.Username)
                ? $"@{ig.Username.Trim()}"
                : ig.Name?.Trim() ?? ig.Id;

            var extraJson = JsonSerializer.Serialize(new
            {
                linkedFacebookPageId = page.Id,
                linkedFacebookPageName = page.Name,
                username = ig.Username,
                profileName = ig.Name
            }, JsonOptions);

            await socialChannelRepository.UpsertFromMetaAsync(
                SocialPlatform.Instagram,
                SocialChannelType.Instagram,
                ig.Id,
                igName,
                page.AccessToken,
                connection.Id,
                extraJson,
                auditUser,
                ct);
            result.InstagramAccountsSynced++;
        }

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
                continue;

            var extraJson = JsonSerializer.Serialize(new
            {
                privacy = group.Privacy,
                administrator = group.Administrator
            }, JsonOptions);

            // Groups often use user token; store user token for later publish experiments.
            await socialChannelRepository.UpsertFromMetaAsync(
                SocialPlatform.Facebook,
                SocialChannelType.Group,
                group.Id,
                group.Name,
                group.AccessToken ?? string.Empty,
                connection.Id,
                extraJson,
                auditUser,
                ct);
            result.FacebookGroupsSynced++;
        }

        return result;
    }
}
