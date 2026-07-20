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
        DateTime? userTokenExpiresAt,
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

        var pageList = pages.ToList();
        var result = new MetaOAuthCallbackResult
        {
            SocialConnectionId = connection.Id,
            PagesReturnedByMeta = pageList.Count,
            PagesMissingToken = pageList.Count(p =>
                !string.IsNullOrWhiteSpace(p.Id) && string.IsNullOrWhiteSpace(p.AccessToken))
        };

        var keepKeys = new List<(SocialPlatform Platform, string ExternalPageId)>();

        foreach (var page in pageList)
        {
            if (string.IsNullOrWhiteSpace(page.Id))
                continue;

            if (string.IsNullOrWhiteSpace(page.AccessToken))
                continue;

            // Page tokens derived from a long-lived user token do not expire → TokenExpiresAt = null.
            await socialChannelRepository.UpsertFromMetaAsync(
                SocialPlatform.Facebook,
                SocialChannelType.Page,
                page.Id,
                page.Name,
                page.AccessToken,
                tokenExpiresAt: null,
                connection.Id,
                extraJson: null,
                auditUser,
                ct);
            keepKeys.Add((SocialPlatform.Facebook, page.Id));
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

            // Instagram publishing uses the linked Page token → also non-expiring.
            await socialChannelRepository.UpsertFromMetaAsync(
                SocialPlatform.Instagram,
                SocialChannelType.Instagram,
                ig.Id,
                igName,
                page.AccessToken,
                tokenExpiresAt: null,
                connection.Id,
                extraJson,
                auditUser,
                ct);
            keepKeys.Add((SocialPlatform.Instagram, ig.Id));
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

            // Groups use the user token → expires with the (long-lived) user token.
            await socialChannelRepository.UpsertFromMetaAsync(
                SocialPlatform.Facebook,
                SocialChannelType.Group,
                group.Id,
                group.Name,
                group.AccessToken ?? string.Empty,
                tokenExpiresAt: userTokenExpiresAt,
                connection.Id,
                extraJson,
                auditUser,
                ct);
            keepKeys.Add((SocialPlatform.Facebook, group.Id));
            result.FacebookGroupsSynced++;
        }

        // Drop DB channels that Meta no longer returns (deleted page, revoked access, unlinked IG).
        result.ChannelsRemoved = await socialChannelRepository.SoftDeleteMissingFromMetaAsync(
            connection.Id,
            keepKeys,
            auditUser,
            ct);

        return result;
    }
}
