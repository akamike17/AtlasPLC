using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Infrastructure.Tests;

public sealed class TargetInstanceRepositoryTests
{
    [Fact]
    public async Task TwoInstancesSamePluginCanCoexist()
    {
        using var db = new TestDb();
        var repository = new SqliteTargetInstanceRepository(db.Store);
        await repository.SaveAsync(new TargetInstance
        {
            Id = "siemens-planta-1", TargetPluginId = "siemens-s7", TargetType = "siemens-s7",
            DisplayName = "Siemens Planta 1",
            Configuration = new Dictionary<string, string> { ["endpoint"] = "10.0.0.11", ["port"] = "102", ["timeoutMs"] = "1000" }
        });
        await repository.SaveAsync(new TargetInstance
        {
            Id = "siemens-planta-2", TargetPluginId = "siemens-s7", TargetType = "siemens-s7",
            DisplayName = "Siemens Planta 2",
            Configuration = new Dictionary<string, string> { ["endpoint"] = "10.0.0.12", ["port"] = "102", ["timeoutMs"] = "2500" }
        });

        var instances = await repository.GetAllAsync();

        Assert.Equal(2, instances.Count);
        Assert.Equal("10.0.0.11", (await repository.GetAsync("siemens-planta-1"))!.Configuration["endpoint"]);
        Assert.Equal("10.0.0.12", (await repository.GetAsync("siemens-planta-2"))!.Configuration["endpoint"]);
    }

    [Fact]
    public async Task SelectionStoresInstanceIdNotPluginId()
    {
        using var db = new TestDb();
        var repository = new SqliteProgramTargetSelectionRepository(db.Store);
        var programId = Guid.NewGuid();
        await repository.SaveAsync(programId, "siemens-planta-2");
        Assert.Equal("siemens-planta-2", await repository.GetAsync(programId));
    }

    [Fact]
    public async Task SensitiveConfigurationKeysAreNeverPersisted()
    {
        using var db = new TestDb();
        var repository = new SqliteTargetInstanceRepository(db.Store);
        await repository.SaveAsync(new TargetInstance
        {
            Id = "openplc-local",
            TargetPluginId = "openplc",
            DisplayName = "OpenPLC local",
            CredentialReference = "openplc-local",
            Configuration = new Dictionary<string, string>
            {
                ["endpoint"] = "127.0.0.1",
                ["password"] = "must-not-reach-sqlite",
                ["access_token"] = "must-not-reach-sqlite",
                ["timeoutMs"] = "3000"
            }
        });

        var loaded = await repository.GetAsync("openplc-local");
        Assert.NotNull(loaded);
        Assert.Equal("openplc-local", loaded!.CredentialReference);
        Assert.DoesNotContain(loaded.Configuration.Keys, key => key.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(loaded.Configuration.Keys, key => key.Contains("token", StringComparison.OrdinalIgnoreCase));

        using var connection = db.Store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ConfigurationJson FROM TargetInstances WHERE Id='openplc-local'";
        var json = (string)command.ExecuteScalar()!;
        Assert.DoesNotContain("must-not-reach-sqlite", json, StringComparison.Ordinal);
    }
}
