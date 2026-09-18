using NixToUny.Nixfarma.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace NixToUny.Nixfarma.Connection;

public static class NixfarmaConnectionFactory
{
    public static string BuildDataSource(NixfarmaConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
            throw new ArgumentException("HOST de Oracle no puede estar vacío.", nameof(options));

        if (options.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "El puerto Oracle no es válido.");

        if (string.IsNullOrWhiteSpace(options.ServiceName))
            throw new ArgumentException("SERVICE_NAME de Oracle no puede estar vacío.", nameof(options));

        return $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={options.Host.Trim()})(PORT={options.Port}))" +
               $"(CONNECT_DATA=(SERVICE_NAME={options.ServiceName.Trim()})))";
    }

    public static string BuildConnectionString(NixfarmaConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.UserName))
            throw new ArgumentException("El usuario Oracle no puede estar vacío.", nameof(options));

        var builder = new OracleConnectionStringBuilder
        {
            UserID = options.UserName,
            Password = options.Password,
            DataSource = BuildDataSource(options),
            Pooling = options.Pooling
        };

        return builder.ConnectionString;
    }

    public static OracleConnection Create(NixfarmaConnectionOptions options) =>
        new(BuildConnectionString(options));

    /// <summary>
    /// Devuelve una representación que nunca contiene la contraseña.
    /// </summary>
    public static string BuildSafeDisplay(NixfarmaConnectionOptions options) =>
        $"User Id={options.UserName};Data Source={BuildDataSource(options)};Pooling={options.Pooling};";
}
