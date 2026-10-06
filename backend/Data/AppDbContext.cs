using System.Globalization;
using Backend.Modules.ApiLog;
using Backend.Modules.Campaign;
using Backend.Modules.Category;
using Backend.Modules.ChannelGroup;
using Backend.Modules.ContentCrawl;
using Backend.Modules.GenerationJob;
using Backend.Modules.GoogleDrive;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaCaption;
using Backend.Modules.MediaEmbedding;
using Backend.Modules.MediaFolder;
using Backend.Modules.MusicTrack;
using Backend.Modules.PageContext;
using Backend.Modules.PageMessage;
using Backend.Modules.Post;
using Backend.Modules.PromptTemplate;
using Backend.Modules.PublishLog;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialComment;
using Backend.Modules.ShortLink;
using Backend.Modules.SocialConnection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Connection đã mở sẵn (test) không kích hoạt interceptor ConnectionOpened ⇒ đăng ký tại đây,
        // mỗi connection đúng một lần (Microsoft.Data.Sqlite tự áp lại collation/function khi connection mở).
        if (Database.IsRelational() && Database.GetDbConnection() is SqliteConnection sqlite)
            SqliteVietnameseCollation.Register(sqlite);
    }

    /// <summary>
    /// Hàm SQLite <c>vi_lower</c> — lowercase Unicode (vi-VN). Dùng trong LINQ; không gọi trực tiếp.
    /// </summary>
    public static string ViLower(string? value)
        => throw new NotSupportedException("ViLower chỉ dùng trong LINQ dịch sang SQL (vi_lower).");

    public DbSet<CategoryModel> Categories => Set<CategoryModel>();
    public DbSet<SocialChannelModel> SocialChannels => Set<SocialChannelModel>();
    public DbSet<ChannelGroupModel> ChannelGroups => Set<ChannelGroupModel>();
    public DbSet<ChannelGroupMemberModel> ChannelGroupMembers => Set<ChannelGroupMemberModel>();
    public DbSet<CampaignModel> Campaigns => Set<CampaignModel>();
    public DbSet<CampaignChannelModel> CampaignChannels => Set<CampaignChannelModel>();
    public DbSet<CampaignChannelGroupModel> CampaignChannelGroups => Set<CampaignChannelGroupModel>();
    public DbSet<SocialConnectionModel> SocialConnections => Set<SocialConnectionModel>();
    public DbSet<PageContextModel> PageContexts => Set<PageContextModel>();
    public DbSet<PostModel> Posts => Set<PostModel>();
    public DbSet<MediaAssetModel> MediaAssets => Set<MediaAssetModel>();
    public DbSet<MediaFolderModel> MediaFolders => Set<MediaFolderModel>();
    public DbSet<MediaCaptionJobModel> MediaCaptionJobs => Set<MediaCaptionJobModel>();
    public DbSet<MediaCaptionJobItemModel> MediaCaptionJobItems => Set<MediaCaptionJobItemModel>();
    public DbSet<PostMediaModel> PostMedias => Set<PostMediaModel>();
    public DbSet<MusicTrackModel> MusicTracks => Set<MusicTrackModel>();
    public DbSet<GenerationJobModel> GenerationJobs => Set<GenerationJobModel>();
    public DbSet<PublishLogModel> PublishLogs => Set<PublishLogModel>();
    public DbSet<MediaEmbeddingModel> MediaEmbeddings => Set<MediaEmbeddingModel>();
    public DbSet<ApiLogModel> ApiLogs => Set<ApiLogModel>();
    public DbSet<PromptTemplateModel> PromptTemplates => Set<PromptTemplateModel>();
    public DbSet<SocialPostModel> SocialPosts => Set<SocialPostModel>();
    public DbSet<Backend.Modules.PageMetrics.ChannelMetricDailyModel> ChannelMetricDaily
        => Set<Backend.Modules.PageMetrics.ChannelMetricDailyModel>();
    public DbSet<SocialCommentModel> SocialComments => Set<SocialCommentModel>();
    public DbSet<CommentActionLogModel> CommentActionLogs => Set<CommentActionLogModel>();
    public DbSet<WebhookEventModel> WebhookEvents => Set<WebhookEventModel>();
    public DbSet<PageConversationModel> PageConversations => Set<PageConversationModel>();
    public DbSet<PageMessageModel> PageMessages => Set<PageMessageModel>();
    public DbSet<MessageActionLogModel> MessageActionLogs => Set<MessageActionLogModel>();
    public DbSet<CrawlSourceModel> CrawlSources => Set<CrawlSourceModel>();
    public DbSet<CrawlRunModel> CrawlRuns => Set<CrawlRunModel>();
    public DbSet<CrawledArticleModel> CrawledArticles => Set<CrawledArticleModel>();
    public DbSet<ContentFingerprintModel> ContentFingerprints => Set<ContentFingerprintModel>();
    public DbSet<ContentCrawlPipelineStateModel> ContentCrawlPipelineStates
        => Set<ContentCrawlPipelineStateModel>();
    public DbSet<GoogleDriveSyncStateModel> GoogleDriveSyncStates
        => Set<GoogleDriveSyncStateModel>();
    public DbSet<GoogleDriveImportFailureModel> GoogleDriveImportFailures
        => Set<GoogleDriveImportFailureModel>();
    public DbSet<GoogleDriveKnownFolderModel> GoogleDriveKnownFolders
        => Set<GoogleDriveKnownFolderModel>();
    public DbSet<ShortLinkModel> ShortLinks => Set<ShortLinkModel>();
    public DbSet<Backend.Modules.NewsSite.NewsArticleModel> NewsArticles
        => Set<Backend.Modules.NewsSite.NewsArticleModel>();
    public DbSet<Backend.Modules.NewsSite.NewsSubscriberModel> NewsSubscribers
        => Set<Backend.Modules.NewsSite.NewsSubscriberModel>();
    public DbSet<Backend.Modules.Notification.AppNotificationModel> AppNotifications
        => Set<Backend.Modules.Notification.AppNotificationModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDbFunction(typeof(AppDbContext).GetMethod(nameof(ViLower), [typeof(string)])!)
            .HasName("vi_lower");

        modelBuilder.Entity<CategoryModel>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.ParentCategoryId);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(200);
        });

        modelBuilder.Entity<MediaFolderModel>(e =>
        {
            e.ToTable("MediaFolders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ParentFolderId);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.IsDeleted);
            // Một Page chỉ một folder "Ảnh AI" active ngay dưới folder gốc.
            e.HasIndex(x => new { x.SocialChannelId, x.ParentFolderId })
                .IsUnique()
                .HasFilter("IsDeleted = 0 AND ParentFolderId IS NOT NULL AND Name = 'Ảnh AI'")
                .HasDatabaseName("IX_MediaFolders_OneActiveAiFolder");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<MediaCaptionJobModel>(e =>
        {
            e.ToTable("MediaCaptionJobs");
            e.HasKey(x => x.Id);
            // SQLite partial unique index: một folder chỉ có một job đang chờ/chạy;
            // lịch sử Completed vẫn được giữ để ListRecentAsync hiển thị.
            e.HasIndex(x => x.FolderId).IsUnique().HasFilter("IsDeleted = 0 AND Status IN (0, 1)");
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.FolderName).HasMaxLength(200);
        });

        modelBuilder.Entity<MediaCaptionJobItemModel>(e =>
        {
            e.ToTable("MediaCaptionJobItems");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.Status });
            e.HasIndex(x => x.MediaAssetId);
            e.Property(x => x.FileName).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(500);
        });

        modelBuilder.Entity<PromptTemplateModel>(e =>
        {
            e.ToTable("PromptTemplates");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TemplateType);
            e.HasIndex(x => x.IsDefault);
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Body).HasColumnType("TEXT");
            e.Property(x => x.TextBody).HasColumnType("TEXT");
            e.Property(x => x.ImageBody).HasColumnType("TEXT");
        });

        modelBuilder.Entity<SocialChannelModel>(e =>
        {
            e.ToTable("SocialChannels");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Platform);
            e.HasIndex(x => x.ChannelType);
            e.HasIndex(x => x.SocialConnectionId);
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.PageName).HasMaxLength(300);
            e.Property(x => x.ExternalPageId).HasMaxLength(200);
            e.Property(x => x.AccessToken).HasColumnType("TEXT");
            e.Property(x => x.RefreshToken).HasColumnType("TEXT");
        });

        // Nhóm kênh — không FK constraint; unique tên/cặp member enforce ở repo + index.
        modelBuilder.Entity<ChannelGroupModel>(e =>
        {
            e.ToTable("ChannelGroups");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<ChannelGroupMemberModel>(e =>
        {
            e.ToTable("ChannelGroupMembers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ChannelGroupId);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => new { x.ChannelGroupId, x.SocialChannelId })
                .IsUnique()
                .HasFilter("IsDeleted = 0")
                .HasDatabaseName("IX_ChannelGroupMembers_Group_Channel_Active");
        });

        modelBuilder.Entity<SocialConnectionModel>(e =>
        {
            e.ToTable("SocialConnections");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Provider);
            e.HasIndex(x => new { x.Provider, x.ExternalUserId });
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.ExternalUserId).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(300);
            e.Property(x => x.AvatarUrl).HasMaxLength(1000);
            e.Property(x => x.Scopes).HasColumnType("TEXT");
        });

        modelBuilder.Entity<PageContextModel>(e =>
        {
            e.ToTable("PageContexts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.LogoMediaId);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.BrandName).HasMaxLength(300);
            e.Property(x => x.CtaText).HasMaxLength(500);
            e.Property(x => x.CtaUrl).HasMaxLength(1000);
            e.Property(x => x.Hotline).HasMaxLength(50);
            e.Property(x => x.Website).HasMaxLength(500);
            e.Property(x => x.BrandColors).HasMaxLength(200);
            e.Property(x => x.ToneOfVoice).HasColumnType("TEXT");
            e.Property(x => x.PromptTemplateText).HasColumnType("TEXT");
            e.Property(x => x.PromptTemplateImage).HasColumnType("TEXT");
        });

        // Chiến dịch — không FK constraint; JSON lịch + junction kênh/nhóm.
        modelBuilder.Entity<CampaignModel>(e =>
        {
            e.ToTable("Campaigns");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.WeekdaysJson).HasColumnType("TEXT");
            e.Property(x => x.PublishTimesJson).HasColumnType("TEXT");
        });

        modelBuilder.Entity<CampaignChannelModel>(e =>
        {
            e.ToTable("CampaignChannels");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => new { x.CampaignId, x.SocialChannelId })
                .IsUnique()
                .HasFilter("IsDeleted = 0")
                .HasDatabaseName("IX_CampaignChannels_Campaign_Channel_Active");
        });

        modelBuilder.Entity<CampaignChannelGroupModel>(e =>
        {
            e.ToTable("CampaignChannelGroups");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.ChannelGroupId);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => new { x.CampaignId, x.ChannelGroupId })
                .IsUnique()
                .HasFilter("IsDeleted = 0")
                .HasDatabaseName("IX_CampaignChannelGroups_Campaign_Group_Active");
        });

        modelBuilder.Entity<PostModel>(e =>
        {
            e.ToTable("Posts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ScheduledPublishAt);
            e.HasIndex(x => x.BatchId);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.IsDeleted);
            // Idempotent khe chiến dịch — chỉ khi CampaignId có giá trị.
            e.HasIndex(x => new { x.CampaignId, x.SocialChannelId, x.CampaignSlotAt })
                .IsUnique()
                .HasFilter("CampaignId IS NOT NULL")
                .HasDatabaseName("IX_Posts_Campaign_Channel_Slot");
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Content).HasColumnType("TEXT");
            e.Property(x => x.ExternalPostId).HasMaxLength(500);
            e.Property(x => x.PublishedUrl).HasMaxLength(1000);
            e.Property(x => x.GenerationError).HasColumnType("TEXT");
            e.Property(x => x.RejectionReason).HasColumnType("TEXT");
            e.Property(x => x.ApprovedBy).HasMaxLength(200);
            e.Property(x => x.ScheduleTimezone).HasMaxLength(100);
        });

        modelBuilder.Entity<MediaAssetModel>(e =>
        {
            e.ToTable("MediaAssets");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Source);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.FolderId);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.CategoryIds).HasColumnType("TEXT");
            e.Property(x => x.FileName).HasMaxLength(500);
            e.Property(x => x.OriginalFileName).HasMaxLength(500);
            e.Property(x => x.StoragePath).HasMaxLength(1000);
            e.Property(x => x.PublicUrl).HasMaxLength(1000);
            e.Property(x => x.MimeType).HasMaxLength(100);
            e.Property(x => x.AltText).HasMaxLength(500);
            e.Property(x => x.Description).HasColumnType("TEXT");
            e.Property(x => x.Tags).HasColumnType("TEXT");
            e.Property(x => x.GoogleDriveFileId).HasMaxLength(200);
            // SQLite coi mỗi NULL là khác biệt trong chỉ mục UNIQUE, nên upload/AI/overlay
            // (GoogleDriveFileId=null) không đụng ràng buộc — chỉ chặn trùng file Drive thật.
            e.HasIndex(x => x.GoogleDriveFileId).IsUnique();
        });

        modelBuilder.Entity<PostMediaModel>(e =>
        {
            e.ToTable("PostMedias");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PostId, x.MediaId });
            e.HasIndex(x => x.PostId);
            e.HasIndex(x => x.MediaId);
        });

        modelBuilder.Entity<MusicTrackModel>(e =>
        {
            e.ToTable("MusicTracks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.DisplayName).HasMaxLength(300);
            e.Property(x => x.FileName).HasMaxLength(500);
            e.Property(x => x.StoragePath).HasMaxLength(1000);
            e.Property(x => x.MimeType).HasMaxLength(100);
        });

        modelBuilder.Entity<GenerationJobModel>(e =>
        {
            e.ToTable("GenerationJobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PostId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.JobType);
            e.HasIndex(x => x.ScheduledAt);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => x.IdempotencyKey);
            e.Property(x => x.InputPayload).HasColumnType("TEXT");
            e.Property(x => x.OutputPayload).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
        });

        modelBuilder.Entity<PublishLogModel>(e =>
        {
            e.ToTable("PublishLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PostId);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.IdempotencyKey);
            e.Property(x => x.ExternalPostId).HasMaxLength(500);
            e.Property(x => x.PublishedUrl).HasMaxLength(1000);
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.RequestPayload).HasColumnType("TEXT");
            e.Property(x => x.ResponsePayload).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<MediaEmbeddingModel>(e =>
        {
            e.ToTable("MediaEmbeddings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MediaAssetId).IsUnique();
            e.Property(x => x.ModelName).HasMaxLength(200);
            e.Property(x => x.Embedding).HasColumnType("BLOB");
            e.Property(x => x.SourceText).HasColumnType("TEXT");
        });

        modelBuilder.Entity<ApiLogModel>(e =>
        {
            e.ToTable("ApiLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Endpoint);
            e.Property(x => x.Endpoint).HasMaxLength(500);
            e.Property(x => x.Controller).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(200);
            e.Property(x => x.HttpMethod).HasMaxLength(20);
            e.Property(x => x.RequestPayload).HasColumnType("TEXT");
            e.Property(x => x.ResponsePayload).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<SocialPostModel>(e =>
        {
            e.ToTable("SocialPosts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.Platform);
            e.HasIndex(x => x.LocalPostId);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => new { x.SocialChannelId, x.ExternalPostId });
            e.Property(x => x.ExternalPostId).HasMaxLength(200);
            e.Property(x => x.PermalinkUrl).HasMaxLength(1000);
            e.Property(x => x.Message).HasColumnType("TEXT");
            e.Property(x => x.SyncCursor).HasMaxLength(500);
            // Dashboard sắp bài theo tương tác giảm dần để lấy "bài tốt nhất" — không có chỉ mục
            // thì mỗi lần mở là quét toàn bảng.
            e.HasIndex(x => x.PostedAt);
        });

        modelBuilder.Entity<Backend.Modules.PageMetrics.ChannelMetricDailyModel>(e =>
        {
            e.ToTable("ChannelMetricDaily");
            e.HasKey(x => x.Id);
            // Mỗi page mỗi ngày ĐÚNG một dòng. Ràng buộc ở tầng CSDL chứ không chỉ trong code:
            // worker có thể chạy lại sau khi lỗi giữa chừng, và hai dòng cùng ngày sẽ làm biểu đồ
            // xu hướng nhảy gấp đôi mà nhìn số tổng vẫn thấy hợp lý.
            e.HasIndex(x => new { x.SocialChannelId, x.Date }).IsUnique();
            e.HasIndex(x => x.Date);
            e.Property(x => x.SyncError).HasMaxLength(500);
        });

        modelBuilder.Entity<SocialCommentModel>(e =>
        {
            e.ToTable("SocialComments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.SocialPostId);
            e.HasIndex(x => x.Platform);
            e.HasIndex(x => x.InboxStatus);
            e.HasIndex(x => x.ParentCommentId);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => new { x.SocialChannelId, x.ExternalCommentId });
            e.Property(x => x.ExternalCommentId).HasMaxLength(200);
            e.Property(x => x.ParentExternalCommentId).HasMaxLength(200);
            e.Property(x => x.AuthorExternalId).HasMaxLength(200);
            e.Property(x => x.AuthorName).HasMaxLength(300);
            e.Property(x => x.AuthorUsername).HasMaxLength(200);
            e.Property(x => x.PermalinkUrl).HasMaxLength(1000);
            e.Property(x => x.AssignedTo).HasMaxLength(200);
            e.Property(x => x.Message).HasColumnType("TEXT");
            e.Property(x => x.InternalNote).HasColumnType("TEXT");
        });

        modelBuilder.Entity<CommentActionLogModel>(e =>
        {
            e.ToTable("CommentActionLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SocialCommentId);
            e.HasIndex(x => x.ActionType);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.ActorUserName).HasMaxLength(200);
            e.Property(x => x.ExternalResultId).HasMaxLength(200);
            e.Property(x => x.PayloadJson).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<WebhookEventModel>(e =>
        {
            e.ToTable("WebhookEvents");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventKey);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Platform);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.EventKey).HasMaxLength(500);
            e.Property(x => x.ObjectId).HasMaxLength(200);
            e.Property(x => x.Verb).HasMaxLength(50);
            e.Property(x => x.Item).HasMaxLength(50);
            e.Property(x => x.PayloadJson).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<PageConversationModel>(e =>
        {
            e.ToTable("PageConversations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => x.ExternalConversationId);
            e.HasIndex(x => new { x.SocialChannelId, x.ParticipantExternalId });
            e.HasIndex(x => x.InboxStatus);
            e.HasIndex(x => x.LastMessageAt);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.ExternalConversationId).HasMaxLength(300);
            e.Property(x => x.ParticipantExternalId).HasMaxLength(200);
            e.Property(x => x.ParticipantName).HasMaxLength(300);
            e.Property(x => x.ParticipantAvatarUrl).HasMaxLength(1000);
            e.Property(x => x.Snippet).HasColumnType("TEXT");
            e.Property(x => x.AssignedTo).HasMaxLength(200);
            e.Property(x => x.InternalNote).HasColumnType("TEXT");
        });

        modelBuilder.Entity<PageMessageModel>(e =>
        {
            e.ToTable("PageMessages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PageConversationId);
            e.HasIndex(x => x.SocialChannelId);
            e.HasIndex(x => new { x.SocialChannelId, x.ExternalMessageId });
            e.HasIndex(x => x.SentAt);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.ExternalMessageId).HasMaxLength(500);
            e.Property(x => x.SenderExternalId).HasMaxLength(200);
            e.Property(x => x.RecipientExternalId).HasMaxLength(200);
            e.Property(x => x.Text).HasColumnType("TEXT");
            e.Property(x => x.AttachmentsJson).HasColumnType("TEXT");
        });

        modelBuilder.Entity<MessageActionLogModel>(e =>
        {
            e.ToTable("MessageActionLogs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PageConversationId);
            e.HasIndex(x => x.ActionType);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.ActorUserName).HasMaxLength(200);
            e.Property(x => x.ExternalResultId).HasMaxLength(500);
            e.Property(x => x.PayloadJson).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<CrawlSourceModel>(e =>
        {
            e.ToTable("CrawlSources");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SourceType);
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.SiteDomain).HasMaxLength(200);
            e.Property(x => x.BrowserProfile).HasMaxLength(100);
            e.Property(x => x.LastError).HasColumnType("TEXT");
            e.Property(x => x.CrawlTimes).HasColumnType("TEXT");
            e.Property(x => x.IncludeKeywords).HasColumnType("TEXT");
            e.Property(x => x.ExcludeKeywords).HasColumnType("TEXT");
            e.Property(x => x.DefaultChannelIds).HasColumnType("TEXT");
        });

        modelBuilder.Entity<ContentCrawlPipelineStateModel>(e =>
        {
            e.ToTable("ContentCrawlPipelineState");
            e.HasKey(x => x.Id);
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.UpdatedByUserName).HasMaxLength(200);
            e.HasData(new ContentCrawlPipelineStateModel
            {
                Id = ContentCrawlPipelineStateModel.SingletonId,
                IsEnabled = true,
                CreatedAt = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        modelBuilder.Entity<GoogleDriveSyncStateModel>(e =>
        {
            e.ToTable("GoogleDriveSyncState");
            e.HasKey(x => x.Id);
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.UpdatedByUserName).HasMaxLength(200);
            e.Property(x => x.PageToken).HasMaxLength(1000);
            e.HasData(new GoogleDriveSyncStateModel
            {
                Id = GoogleDriveSyncStateModel.SingletonId,
                IsEnabled = true,
                CreatedAt = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        modelBuilder.Entity<GoogleDriveImportFailureModel>(e =>
        {
            e.ToTable("GoogleDriveImportFailures");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GoogleDriveFileId).IsUnique();
            e.HasIndex(x => x.AttemptCount);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.GoogleDriveFileId).HasMaxLength(200);
            e.Property(x => x.FileName).HasMaxLength(500);
            e.Property(x => x.MimeType).HasMaxLength(100);
            e.Property(x => x.LastError).HasColumnType("TEXT");
            e.Property(x => x.DriveParentId).HasMaxLength(200);
        });

        modelBuilder.Entity<GoogleDriveKnownFolderModel>(e =>
        {
            e.ToTable("GoogleDriveKnownFolders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FolderId).IsUnique();
            e.HasIndex(x => x.MediaFolderId);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.FolderId).HasMaxLength(200);
            e.Property(x => x.DriveParentId).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(500);
        });

        modelBuilder.Entity<CrawlRunModel>(e =>
        {
            e.ToTable("CrawlRuns");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CrawlSourceId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.TriggerSource).HasMaxLength(50);
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<CrawledArticleModel>(e =>
        {
            // Khoá sự việc được tra ở MỌI lượt chấm trùng — phải có chỉ mục.
            e.HasIndex(x => x.EventKey);
            e.Property(x => x.EventKey).HasMaxLength(120);
            e.ToTable("CrawledArticles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CrawlSourceId);
            e.HasIndex(x => x.CrawlRunId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ContentHash);
            e.HasIndex(x => x.SimHash);
            e.HasIndex(x => x.PublishedAt);
            e.HasIndex(x => x.FetchedAt);
            e.HasIndex(x => x.DuplicateOfId);
            e.HasIndex(x => x.ResultBatchId);
            e.HasIndex(x => x.NormalizedUrl);
            e.HasIndex(x => x.IsDeleted);
            // KHÔNG unique: soft-delete rồi cào lại có thể va nhau hợp lệ. Chặn trùng bằng
            // code như tiền lệ WebhookEventModel.EventKey.
            e.HasIndex(x => new { x.CrawlSourceId, x.SourceGuid });
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.SourceUrl).HasMaxLength(1000);
            e.Property(x => x.NormalizedUrl).HasMaxLength(1000);
            e.Property(x => x.SourceGuid).HasMaxLength(1000);
            e.Property(x => x.Author).HasMaxLength(200);
            e.Property(x => x.SourceCategory).HasMaxLength(200);
            e.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.ReviewedBy).HasMaxLength(200);
            e.Property(x => x.Summary).HasColumnType("TEXT");
            e.Property(x => x.Content).HasColumnType("TEXT");
            e.Property(x => x.DuplicateReason).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
            e.Property(x => x.RejectReason).HasColumnType("TEXT");
        });

        modelBuilder.Entity<Backend.Modules.NewsSite.NewsArticleModel>(e =>
        {
            e.Property(x => x.ImageUrl).HasMaxLength(1000);
            e.Property(x => x.ImageCredit).HasMaxLength(200);
            e.ToTable("NewsArticles");
            e.HasKey(x => x.Id);
            // Slug là khoá tra cứu duy nhất và là một phần của URL công khai.
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PublishedAt);
            e.HasIndex(x => x.CategorySlug);
            e.HasIndex(x => x.CrawledArticleId);
            // Khoá sự việc được tra ở MỌI lượt xuất bản để bắt "cùng việc, khác chữ".
            e.HasIndex(x => x.EventKey);
            e.Property(x => x.EventKey).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.CategorySlug).HasMaxLength(50);
            e.Property(x => x.SourceName).HasMaxLength(200);
            e.Property(x => x.SourceUrl).HasMaxLength(1000);
            e.Property(x => x.Sapo).HasColumnType("TEXT");
            e.Property(x => x.BodyHtml).HasColumnType("TEXT");
            e.Property(x => x.KeyPointsJson).HasColumnType("TEXT");
            e.Property(x => x.TimelineJson).HasColumnType("TEXT");
            e.Property(x => x.ErrorMessage).HasColumnType("TEXT");
        });

        modelBuilder.Entity<Backend.Modules.NewsSite.NewsSubscriberModel>(e =>
        {
            e.ToTable("NewsSubscribers");
            e.HasKey(x => x.Id);
            // Tra theo Email lúc đăng ký lại (idempotent) và theo UnsubscribeToken lúc bấm huỷ
            // từ email — cả hai chạy trên mỗi lượt độc giả tương tác.
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.UnsubscribeToken).IsUnique();
            e.HasIndex(x => x.IsActive);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.UnsubscribeToken).HasMaxLength(64);
        });

        modelBuilder.Entity<Backend.Modules.Notification.AppNotificationModel>(e =>
        {
            e.ToTable("AppNotifications");
            e.HasKey(x => x.Id);
            // Chuông chỉ hỏi hai câu: 30 dòng mới nhất, và đếm số chưa đọc. Hai index này
            // phục vụ đúng hai câu đó, không thêm gì.
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.ReadAt);
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Message).HasColumnType("TEXT");
            e.Property(x => x.LinkUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<ShortLinkModel>(e =>
        {
            e.ToTable("ShortLinks");
            e.HasKey(x => x.Id);
            // Tra cứu theo Code chạy trên MỌI lượt bấm của độc giả nên phải có index.
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.PostId);
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.TargetUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<ContentFingerprintModel>(e =>
        {
            e.ToTable("ContentFingerprints");
            e.HasKey(x => x.Id);
            // Mỗi band một index riêng: truy vấn ứng viên bắn 4 câu Where(BandN == ...) độc lập
            // rồi hợp nhất trong bộ nhớ. Gộp thành OR thì phải trông chờ vào planner SQLite.
            e.HasIndex(x => x.Band0);
            e.HasIndex(x => x.Band1);
            e.HasIndex(x => x.Band2);
            e.HasIndex(x => x.Band3);
            e.HasIndex(x => x.ContentHash);
            e.HasIndex(x => x.ContentAt);
            e.HasIndex(x => x.IsDeleted);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.TitleSnippet).HasMaxLength(500);
        });
    }
}

/// <summary>
/// Collation SQLite "VI" + hàm <c>vi_lower</c> (Unicode lowercase theo vi-VN).
/// Collation: đối chiếu với Intl.Collator('vi') trên bộ tên dùng chung với channelSort.test.js.
/// vi_lower: tìm kiếm keyword lịch không phân biệt hoa/thường tiếng Việt (giữ dấu).
/// </summary>
public static class SqliteVietnameseCollation
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    private static readonly CompareInfo Vietnamese = VietnameseCulture.CompareInfo;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SqliteConnection, object> Registered = new();

    /// <summary>Đăng ký một lần cho mỗi connection (đăng ký lại khi có statement đang chạy sẽ lỗi).</summary>
    public static void Register(SqliteConnection connection)
    {
        lock (Registered)
        {
            if (Registered.TryGetValue(connection, out _)) return;
            connection.CreateCollation(PageOrdering.VietnameseCollation, Compare);
            connection.CreateFunction(
                "vi_lower",
                (string? value) => string.IsNullOrEmpty(value)
                    ? string.Empty
                    : value.ToLower(VietnameseCulture));
            Registered.Add(connection, new object());
        }
    }

    public static int Compare(string? left, string? right)
        => Vietnamese.Compare(left ?? string.Empty, right ?? string.Empty, CompareOptions.None);

    /// <summary>Lowercase Unicode phía CLR (cùng quy tắc với hàm SQLite vi_lower).</summary>
    public static string Lower(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.ToLower(VietnameseCulture);
}

public class ApplicationUser : IdentityUser<Guid> { }
public class ApplicationRole : IdentityRole<Guid> { }
