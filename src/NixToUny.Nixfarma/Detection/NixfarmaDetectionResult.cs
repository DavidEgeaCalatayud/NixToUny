namespace NixToUny.Nixfarma.Detection;

public sealed record NixfarmaDetectionResult(
    string TnsNamesPath,
    string Alias,
    string Host,
    int Port,
    string ServiceName,
    string Descriptor)
{
    public string Source => $"tnsnames.ora ({Alias})";

    public override string ToString() =>
        $"{Alias} -> {Host}:{Port}/{ServiceName}";
}
