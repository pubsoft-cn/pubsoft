namespace PubSoft.Core;

public class ApiResponse<T>
{
    public int Code { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? TraceId { get; set; }

    public static ApiResponse<T> Success(T data, string? traceId = null)
    {
        return new ApiResponse<T> { Code = 0, Data = data, TraceId = traceId };
    }

    public static ApiResponse<T> Fail(int code, string message, string? traceId = null)
    {
        if (code == 0) throw new ArgumentOutOfRangeException(nameof(code), "Failure code must be nonzero.");
        return new ApiResponse<T> { Code = code, Message = message, TraceId = traceId };
    }
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Success(string? traceId = null)
    {
        return new() { Code = 0, Data = null, TraceId = traceId };
    }

    public static new ApiResponse Fail(int code, string message, string? traceId = null)
    {
        if (code == 0) throw new ArgumentOutOfRangeException(nameof(code), "Failure code must be nonzero.");
        return new() { Code = code, Message = message, TraceId = traceId };
    }
}
