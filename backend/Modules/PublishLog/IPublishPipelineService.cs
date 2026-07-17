namespace Backend.Modules.PublishLog;

public interface IPublishPipelineService
{
    Task<ProcessPublishLogResponse> ProcessAsync(Guid publishLogId, CancellationToken ct = default);
    Task<ProcessPublishLogResponse> ProcessRealAsync(Guid publishLogId, CancellationToken ct = default);

    /// <summary>Đăng ngay: xử lý publish log Pending của post → Published. Null nếu không có log Pending.</summary>
    Task<ProcessPublishLogResponse?> ProcessPendingForPostAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Gỡ kẹt: nếu publish lỗi khiến post kẹt ở Publishing, hủy log Pending và đưa post về Approved/Scheduled.</summary>
    Task RevertStuckPublishingAsync(Guid postId, CancellationToken ct = default);
    Task<PublishLogModel> FailAsync(Guid publishLogId, FailPublishLogRequest request, CancellationToken ct = default);
    Task<PublishLogModel> RetryAsync(Guid publishLogId, CancellationToken ct = default);
    Task<PublishLogModel> CancelAsync(Guid publishLogId, CancellationToken ct = default);
    Task<ProcessDueScheduledResult> ProcessDueScheduledAsync(int batchSize, CancellationToken ct = default);
}
