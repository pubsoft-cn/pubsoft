namespace PubSoft.Core;

/// <summary>Generic API envelope: code + data + message + trace id.</summary>
public class ApiResponse<T>
{
    /// <summary>Result code; 0 on success, nonzero on failure.</summary>
    public int Code { get; set; }
    /// <summary>The payload on success.</summary>
    public T? Data { get; set; }
    /// <summary>Human-readable detail, typically on failure.</summary>
    public string? Message { get; set; }
    /// <summary>Correlation id for tracing.</summary>
    public string? TraceId { get; set; }

    /// <summary>Creates a success envelope with payload.</summary>
    public static ApiResponse<T> Success(T data, string? traceId = null)
    {
        return new ApiResponse<T> { Code = 0, Data = data, TraceId = traceId };
    }

    /// <summary>Creates a failure envelope; <paramref name="code"/> must be nonzero.</summary>
    public static ApiResponse<T> Fail(int code, string message, string? traceId = null)
    {
        if (code == 0) throw new ArgumentOutOfRangeException(nameof(code), "Failure code must be nonzero.");
        return new ApiResponse<T> { Code = code, Message = message, TraceId = traceId };
    }
}

/// <summary>Non-generic envelope for responses without a payload.</summary>
public class ApiResponse : ApiResponse<object>
{
    /// <summary>Creates a success envelope without payload.</summary>
    public static ApiResponse Success(string? traceId = null)
    {
        return new() { Code = 0, Data = null, TraceId = traceId };
    }

    /// <summary>Creates a failure envelope; <paramref name="code"/> must be nonzero.</summary>
    public static new ApiResponse Fail(int code, string message, string? traceId = null)
    {
        if (code == 0) throw new ArgumentOutOfRangeException(nameof(code), "Failure code must be nonzero.");
        return new() { Code = code, Message = message, TraceId = traceId };
    }
}
