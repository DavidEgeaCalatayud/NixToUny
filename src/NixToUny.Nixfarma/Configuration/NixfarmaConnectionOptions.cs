namespace NixToUny.Nixfarma.Configuration;

public sealed record NixfarmaConnectionOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 1521;
    public string ServiceName { get; init; } = string.Empty;
    public string UserName { get; init; } = "consu";
    public string Password { get; init; } = "consu";
    public bool Pooling { get; init; } = true;

    public static NixfarmaConnectionOptions FromDetection(
        Detection.NixfarmaDetectionResult detection,
        string userName = "consu",
        string password = "consu")
    {
        ArgumentNullException.ThrowIfNull(detection);

        return new NixfarmaConnectionOptions
        {
            Host = detection.Host,
            Port = detection.Port,
            ServiceName = detection.ServiceName,
            UserName = userName,
            Password = password
        };
    }
}
