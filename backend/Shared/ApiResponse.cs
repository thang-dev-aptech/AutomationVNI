namespace Backend.Shared;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public T? Data { get; set; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string message = "Thành công") => new()
    {
        Success = true,
        Message = message,
        Data = data
    };

    public static ApiResponse<object?> Ok(string message = "Thành công") => new()
    {
        Success = true,
        Message = message,
        Data = null
    };

    public static ApiResponse<object?> Fail(string errorCode, string message) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        Message = message,
        Data = null
    };
}
