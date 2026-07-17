using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;

namespace Backend.Modules.ApiLog;

public class ApiLogRepository : GenericRepository<ApiLogModel>
{
    public ApiLogRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<ApiLogResponse>> FilterAsync(
        ApiLogFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Endpoint.Contains(kw) || x.Action.Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(request.Endpoint))
            query = query.Where(x => x.Endpoint.Contains(request.Endpoint.Trim()));

        if (!string.IsNullOrWhiteSpace(request.HttpMethod))
            query = query.Where(x => x.HttpMethod == request.HttpMethod.ToUpper());

        if (request.ResponseStatus.HasValue)
            query = query.Where(x => x.ResponseStatus == request.ResponseStatus.Value);

        if (request.CallByUserId.HasValue)
            query = query.Where(x => x.CallByUserId == request.CallByUserId.Value);

        if (request.FromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.CreatedAt <= request.ToDate.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<ApiLogResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<ApiLogModel> WriteAsync(ApiLogModel log, CancellationToken ct = default)
        => await base.CreateAsync(log, ct);

    public static ApiLogResponse ToResponse(ApiLogModel e) => new()
    {
        Id = e.Id,
        Endpoint = e.Endpoint,
        Controller = e.Controller,
        Action = e.Action,
        HttpMethod = e.HttpMethod,
        ResponseStatus = e.ResponseStatus,
        TimelineMs = e.TimelineMs,
        CallByUserId = e.CallByUserId,
        CallByUserName = e.CallByUserName,
        IpAddress = e.IpAddress,
        ErrorMessage = e.ErrorMessage,
        CreatedAt = e.CreatedAt
    };

    public static ApiLogDetailResponse ToDetailResponse(ApiLogModel e) => new()
    {
        Id = e.Id,
        Endpoint = e.Endpoint,
        Controller = e.Controller,
        Action = e.Action,
        HttpMethod = e.HttpMethod,
        ResponseStatus = e.ResponseStatus,
        TimelineMs = e.TimelineMs,
        CallByUserId = e.CallByUserId,
        CallByUserName = e.CallByUserName,
        IpAddress = e.IpAddress,
        ErrorMessage = e.ErrorMessage,
        RequestPayload = e.RequestPayload,
        ResponsePayload = e.ResponsePayload,
        CreatedAt = e.CreatedAt
    };
}
