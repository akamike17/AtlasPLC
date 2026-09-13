using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class TargetConfigurationProviderTests
{
    [Fact]
    public void Get_returns_the_persisted_endpoint_port_and_timeout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atlas_target_config_{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteStore(path);
            store.EnsureCreated();
            using (var connection = store.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO TargetConfigurations(TargetId,Endpoint,Port,TimeoutMs,UpdatedUtc) VALUES($id,$endpoint,$port,$timeout,$updated)";
                command.Parameters.AddWithValue("$id", "rockwell-logix");
                command.Parameters.AddWithValue("$endpoint", "10.20.30.40");
                command.Parameters.AddWithValue("$port", 44818);
                command.Parameters.AddWithValue("$timeout", 2750);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            var configuration = new SqliteTargetConfigurationProvider(store).Get("rockwell-logix");

            Assert.NotNull(configuration);
            Assert.Equal("10.20.30.40", configuration.Endpoint);
            Assert.Equal(44818, configuration.Port);
            Assert.Equal(2750, configuration.TimeoutMs);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(path + suffix); } catch { }
            }
        }
    }
}
