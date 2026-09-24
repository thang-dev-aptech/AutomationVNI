using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Shared;

public class GlobalExceptionFilter(
    IHostEnvironment environment,
    ILogger<GlobalExceptionFilter> logger) : IExceptionFilter
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

        // BẮT BUỘC phải log ở đây. Trước khi có đoạn này, filter nuốt trọn mọi exception mà không
        // để lại dấu vết nào: production chỉ trả về câu "Đã xảy ra lỗi hệ thống" chung chung, còn
        // log server thì sạch bong. Hậu quả thực tế: một sự cố render Reels đã mất nhiều lượt gửi
        // log qua lại mà vẫn không tìm được nguyên nhân, vì exception thật không bao giờ được ghi.
        //
        // Lỗi 4xx là lỗi phía người dùng (nhập sai, không có quyền, không tìm thấy) — ghi mức
        // Warning, không kèm stack trace cho đỡ nhiễu. Lỗi 5xx là bug/sự cố thật — ghi Error kèm
        // nguyên exception để có stack trace.
        var path = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        if (statusCode >= 500)
            logger.LogError(ex, "Lỗi chưa xử lý khi {Path} → trả {StatusCode}", path, statusCode);
        else
            logger.LogWarning("{Path} → {StatusCode} {ErrorCode}: {Message}", path, statusCode, errorCode, ex.Message);

        context.Result = new ObjectResult(ApiResponse.Fail(errorCode, message))
        {
            StatusCode = statusCode
        };
        context.ExceptionHandled = true;
    }
}
