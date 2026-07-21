using Backend.Modules.ApiLog;
using Backend.Modules.Category;
using Backend.Modules.GenerationJob;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaEmbedding;
using Backend.Modules.PageContext;
using Backend.Modules.PageMessage;
using Backend.Modules.Post;
using Backend.Modules.PromptTemplate;
using Backend.Modules.PublishLog;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialComment;
using Backend.Modules.SocialConnection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CategoryModel> Categories => Set<CategoryModel>();
    public DbSet<SocialChannelModel> SocialChannels => Set<SocialChannelModel>();
    public DbSet<SocialConnectionModel> SocialConnections => Set<SocialConnectionModel>();
    public DbSet<PageContextModel> PageContexts => Set<PageContextModel>();
    public DbSet<PostModel> Posts => Set<PostModel>();
    public DbSet<MediaAssetModel> MediaAssets => Set<MediaAssetModel>();
    public DbSet<PostMediaModel> PostMedias => Set<PostMediaModel>();
    public DbSet<GenerationJobModel> GenerationJobs => Set<GenerationJobModel>();
    public DbSet<PublishLogModel> PublishLogs => Set<PublishLogModel>();
    public DbSet<MediaEmbeddingModel> MediaEmbeddings => Set<MediaEmbeddingModel>();
    public DbSet<ApiLogModel> ApiLogs => Set<ApiLogModel>();
    public DbSet<PromptTemplateModel> PromptTemplates => Set<PromptTemplateModel>();
    public DbSet<SocialPostModel> SocialPosts => Set<SocialPostModel>();
    public DbSet<SocialCommentModel> SocialComments => Set<SocialCommentModel>();
    public DbSet<CommentActionLogModel> CommentActionLogs => Set<CommentActionLogModel>();
    public DbSet<WebhookEventModel> WebhookEvents => Set<WebhookEventModel>();
    public DbSet<PageConversationModel> PageConversations => Set<PageConversationModel>();
    public DbSet<PageMessageModel> PageMessages => Set<PageMessageModel>();
    public DbSet<MessageActionLogModel> MessageActionLogs => Set<MessageActionLogModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            e.Property(x => x.ToneOfVoice).HasColumnType("TEXT");
            e.Property(x => x.PromptTemplateText).HasColumnType("TEXT");
            e.Property(x => x.PromptTemplateImage).HasColumnType("TEXT");
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
            e.HasIndex(x => x.IsDeleted);
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
            e.HasIndex(x => x.IsDeleted);
            e.Property(x => x.FileName).HasMaxLength(500);
            e.Property(x => x.OriginalFileName).HasMaxLength(500);
            e.Property(x => x.StoragePath).HasMaxLength(1000);
            e.Property(x => x.PublicUrl).HasMaxLength(1000);
            e.Property(x => x.MimeType).HasMaxLength(100);
            e.Property(x => x.AltText).HasMaxLength(500);
            e.Property(x => x.Description).HasColumnType("TEXT");
            e.Property(x => x.Tags).HasColumnType("TEXT");
        });

        modelBuilder.Entity<PostMediaModel>(e =>
        {
            e.ToTable("PostMedias");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PostId, x.MediaId });
            e.HasIndex(x => x.PostId);
            e.HasIndex(x => x.MediaId);
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
    }
}

public class ApplicationUser : IdentityUser<Guid> { }
public class ApplicationRole : IdentityRole<Guid> { }
