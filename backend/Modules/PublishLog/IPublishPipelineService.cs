namespace Backend.Modules.PublishLog;

public interface IPublishPipelineService
{
    Task<ProcessPublishLogResponse> ProcessAsync(Guid publishLogId, CancellationToken ct = default);
    Task<ProcessPublishLogResponse> ProcessRealAsync(Guid publishLogId, CancellationToken ct = default);
    Task<PublishLogModel> FailAsync(Guid publishLogId, FailPublishLogRequest request, CancellationToken ct = default);
    Task<PublishLogModel> RetryAsync(Guid publishLogId, CancellationToken ct = default);
    Task<PublishLogModel> CancelAsync(Guid publishLogId, CancellationToken ct = default);
    Task<ProcessDueScheduledResult> ProcessDueScheduledAsync(int batchSize, CancellationToken ct = default);
}
