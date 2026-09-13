using AtlasSoftPlc.Infrastructure.Persistence;

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
        using var conn = store.OpenConnection(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Endpoint, Port, TimeoutMs FROM TargetConfigurations WHERE TargetId=$id";
        cmd.Parameters.AddWithValue("$id", targetId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)) : null;
    }
}
