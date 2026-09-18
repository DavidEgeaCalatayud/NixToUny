namespace NixToUny.Nixfarma.Connection;

public sealed record NixfarmaConnectionTestResult(
    bool Success,
    string Message,
    TimeSpan Duration,
    string? OracleErrorCode = null);
