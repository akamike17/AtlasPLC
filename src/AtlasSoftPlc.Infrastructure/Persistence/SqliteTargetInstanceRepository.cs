using System.Text.Json;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Infrastructure.Persistence;

public sealed class SqliteTargetInstanceRepository(SqliteStore store) : ITargetInstanceRepository
{
    public async Task<IReadOnlyList<TargetInstance>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, TargetPluginId, DisplayName, ConfigurationJson, CredentialReference FROM TargetInstances ORDER BY DisplayName, Id";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<TargetInstance>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public async Task<TargetInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, TargetPluginId, DisplayName, ConfigurationJson, CredentialReference FROM TargetInstances WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", instanceId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task SaveAsync(TargetInstance instance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instance.Id) || string.IsNullOrWhiteSpace(instance.TargetPluginId))
            throw new ArgumentException("La instancia requiere Id y TargetPluginId.", nameof(instance));
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO TargetInstances(Id,TargetPluginId,DisplayName,ConfigurationJson,CredentialReference,UpdatedUtc) VALUES($id,$plugin,$name,$config,$credential,$utc) ON CONFLICT(Id) DO UPDATE SET TargetPluginId=$plugin,DisplayName=$name,ConfigurationJson=$config,CredentialReference=$credential,UpdatedUtc=$utc";
        cmd.Parameters.AddWithValue("$id", instance.Id);
        cmd.Parameters.AddWithValue("$plugin", instance.TargetPluginId);
        cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(instance.DisplayName) ? instance.Id : instance.DisplayName);
        cmd.Parameters.AddWithValue("$config", JsonSerializer.Serialize(instance.Configuration));
        cmd.Parameters.AddWithValue("$credential", (object?)instance.CredentialReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static TargetInstance Read(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(3))
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pluginId = reader.GetString(1);
        return new TargetInstance
        {
            Id = reader.GetString(0), TargetPluginId = pluginId, TargetType = pluginId,
            DisplayName = reader.GetString(2), Configuration = config,
            CredentialReference = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }
}
