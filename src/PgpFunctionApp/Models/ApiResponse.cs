namespace PgpFunctionApp.Models;

public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? ProcessedCount { get; init; }

    public static ApiResponse Ok(string message, int? processedCount = null)
        => new() { Success = true, Message = message, ProcessedCount = processedCount };

    public static ApiResponse Fail(string message)
        => new() { Success = false, Message = message };
}
