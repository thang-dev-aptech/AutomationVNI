using Backend.Modules.Post.Enums;
using Backend.Modules.PublishLog;

namespace Backend.Modules.Post;

/// <summary>
/// Hành động hàng loạt: gọi lại đúng luật quyền + workflow của endpoint đơn lẻ
/// (cancel-schedule / publish-now / SoftDelete) cho từng bài; một bài lỗi không chặn các bài khác.
/// </summary>
public class PostBulkActionService(
    PostWorkflowService workflow,
    PostRepository postRepository,
    IPublishPipelineService publishPipeline)
{
    public const int MaxPostIds = 100;

    public async Task<PostBulkActionResponse> ExecuteAsync(
        PostBulkActionRequest request, CancellationToken ct = default)
    {
        var results = new List<PostBulkActionItemResult>(request.PostIds.Count);
        foreach (var postId in request.PostIds)
            results.Add(await ExecuteOneAsync(request.Action, postId, ct));

        return new PostBulkActionResponse { Results = results };
    }

    private async Task<PostBulkActionItemResult> ExecuteOneAsync(
        string action, Guid postId, CancellationToken ct)
    {
        try
        {
            return action.ToLowerInvariant() switch
            {
                var a when a == PostBulkActions.CancelSchedule.ToLowerInvariant()
                    => await CancelScheduleOneAsync(postId, ct),
                var a when a == PostBulkActions.PublishNow.ToLowerInvariant()
                    => await PublishNowOneAsync(postId, ct),
                var a when a == PostBulkActions.Delete.ToLowerInvariant()
                    => await DeleteOneAsync(postId, ct),
                _ => Fail(postId, "VALIDATION_ERROR", "Hành động không hợp lệ")
            };
        }
        catch (KeyNotFoundException)
        {
            return Fail(postId, "NOT_FOUND", "Không tìm thấy bài viết");
        }
        catch (ArgumentException ex)
        {
            return Fail(postId, "VALIDATION_ERROR", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(postId, "VALIDATION_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Giống PostController.CancelSchedule: chủ bài hoặc Admin/ContentManager;
    /// trạng thái qua PostWorkflowService.CancelScheduleAsync.
    /// </summary>
    private async Task<PostBulkActionItemResult> CancelScheduleOneAsync(Guid postId, CancellationToken ct)
    {
        var post = await workflow.GetPostAsync(postId, ct);
        if (post is null)
            return Fail(postId, "NOT_FOUND", "Không tìm thấy bài viết");

        if (!CanCancelSchedule(post))
            return Fail(postId, "FORBIDDEN", "Bạn không có quyền thực hiện thao tác này");

        await workflow.CancelScheduleAsync(postId, ct);
        return Ok(postId, "Hủy lịch đăng thành công");
    }

    /// <summary>
    /// Giống PostController.PublishNow: Admin/Reviewer/ContentManager;
    /// PublishNowAsync + ProcessPending; lỗi precondition → RevertStuckPublishingAsync.
    /// </summary>
    private async Task<PostBulkActionItemResult> PublishNowOneAsync(Guid postId, CancellationToken ct)
    {
        if (!workflow.IsInAnyRole("Admin", "Reviewer", "ContentManager"))
            return Fail(postId, "FORBIDDEN", "Bạn không có quyền thực hiện thao tác này");

        var post = await workflow.GetPostAsync(postId, ct);
        if (post is null)
            return Fail(postId, "NOT_FOUND", "Không tìm thấy bài viết");

        await workflow.PublishNowAsync(postId, ct);

        try
        {
            var result = await publishPipeline.ProcessPendingForPostAsync(postId, ct);
            return Ok(postId, result is not null ? "Đã đăng bài thành công" : "Đã tạo job đăng bài");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await publishPipeline.RevertStuckPublishingAsync(postId, ct);
            return Fail(postId, "PUBLISH_FAILED", ex.Message);
        }
    }

    /// <summary>
    /// Giống PostController.SoftDelete: chặn bài Scheduled; soft-delete qua repository.
    /// </summary>
    private async Task<PostBulkActionItemResult> DeleteOneAsync(Guid postId, CancellationToken ct)
    {
        var post = await workflow.GetPostAsync(postId, ct);
        if (post is null)
            return Fail(postId, "NOT_FOUND", "Không tìm thấy bài viết");

        if (post.Status == PostStatus.Scheduled)
            return Fail(postId, "POST_SCHEDULED", "Bài đang có lịch đăng. Hãy huỷ lịch trước khi xoá.");

        var deleted = await postRepository.SoftDeleteAsync(postId, ct);
        if (!deleted)
            return Fail(postId, "NOT_FOUND", "Không tìm thấy bài viết");

        return Ok(postId, "Xóa bài viết thành công");
    }

    /// <summary>Cùng luật CancelSchedule đơn lẻ — tách helper để bulk/đơn lẻ không lệch.</summary>
    public bool CanCancelSchedule(PostModel post)
        => workflow.IsOwner(post) || workflow.IsInAnyRole("Admin", "ContentManager");

    private static PostBulkActionItemResult Ok(Guid postId, string message) => new()
    {
        PostId = postId,
        Success = true,
        Message = message
    };

    private static PostBulkActionItemResult Fail(Guid postId, string errorCode, string message) => new()
    {
        PostId = postId,
        Success = false,
        ErrorCode = errorCode,
        Message = message
    };
}
