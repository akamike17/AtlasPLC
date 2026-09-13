using AtlasSoftPlc.Infrastructure.Persistence;
using System.Text.Json;

namespace AtlasSoftPlc.Web.Services;

public sealed record EffectiveTargetConfiguration(string Endpoint, int Port, int TimeoutMs);

public interface ITargetConfigurationProvider
{
    EffectiveTargetConfiguration? Get(string targetId);
}

public sealed class SqliteTargetConfigurationProvider(SqliteStore store) : ITargetConfigurationProvider
{
    public EffectiveTargetConfiguration? Get(string targetId)
    {
        using var conn = store.OpenConnection();
        using var instance = conn.CreateCommand();
        instance.CommandText = "SELECT ConfigurationJson FROM TargetInstances WHERE Id=$id";
        instance.Parameters.AddWithValue("$id", targetId);
        var json = instance.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(json))
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (values is not null && int.TryParse(values.GetValueOrDefault("port"), out var instancePort) && int.TryParse(values.GetValueOrDefault("timeoutMs"), out var instanceTimeout))
                return new(values.GetValueOrDefault("endpoint") ?? string.Empty, instancePort, instanceTimeout);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Endpoint, Port, TimeoutMs FROM TargetConfigurations WHERE TargetId=$id";
        cmd.Parameters.AddWithValue("$id", targetId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)) : null;
    }
}
