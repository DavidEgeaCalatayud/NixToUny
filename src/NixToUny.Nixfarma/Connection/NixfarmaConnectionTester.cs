using System.Diagnostics;
using NixToUny.Nixfarma.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace NixToUny.Nixfarma.Connection;

public sealed class NixfarmaConnectionTester
{
    public async Task<NixfarmaConnectionTestResult> TestAsync(
        NixfarmaConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await using var connection = NixfarmaConnectionFactory.Create(options);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            command.CommandTimeout = 10;

            var value = await command.ExecuteScalarAsync(cancellationToken);
            sw.Stop();

            var ok = Convert.ToInt32(value) == 1;
            return new NixfarmaConnectionTestResult(
                ok,
                ok ? "Conexión con Nixfarma correcta." : "Oracle respondió, pero la prueba no devolvió el valor esperado.",
                sw.Elapsed);
        }
        catch (OracleException ex)
        {
            sw.Stop();
            return new NixfarmaConnectionTestResult(
                false,
                GetFriendlyOracleMessage(ex),
                sw.Elapsed,
                $"ORA-{ex.Number:00000}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new NixfarmaConnectionTestResult(false, "Prueba de conexión cancelada.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NixfarmaConnectionTestResult(
                false,
                $"No se pudo conectar con Nixfarma: {ex.Message}",
                sw.Elapsed);
        }
    }

    private static string GetFriendlyOracleMessage(OracleException ex) => ex.Number switch
    {
        1017 => "Usuario o contraseña de Oracle incorrectos (ORA-01017).",
        12154 => "Oracle no pudo resolver el servicio indicado (ORA-12154).",
        12514 => "El listener no conoce el SERVICE_NAME indicado (ORA-12514).",
        12541 => "No se ha podido contactar con el listener de Oracle (ORA-12541).",
        _ => $"Oracle devolvió ORA-{ex.Number:00000}: {ex.Message}"
    };
}
