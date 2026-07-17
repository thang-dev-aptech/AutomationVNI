using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Shared;

public class GlobalExceptionFilter(IHostEnvironment environment) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var ex = context.Exception;

        var (statusCode, errorCode, message) = ex switch
        {
            ArgumentException => (400, "VALIDATION_ERROR", ex.Message),
            UnauthorizedAccessException => (403, "FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"),
            KeyNotFoundException => (404, "NOT_FOUND", string.IsNullOrWhiteSpace(ex.Message) ? "Không tìm thấy dữ liệu" : ex.Message),
            _ => (500, "INTERNAL_ERROR", environment.IsDevelopment()
                ? ex.Message
                : "Đã xảy ra lỗi hệ thống")
        };

        context.Result = new ObjectResult(ApiResponse.Fail(errorCode, message))
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;
    }
}
