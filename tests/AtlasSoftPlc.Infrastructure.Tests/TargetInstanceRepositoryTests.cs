using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Infrastructure.Tests;

public sealed class TargetInstanceRepositoryTests
{
    [Fact]
    public async Task Saves_two_instances_of_the_same_plugin_without_colliding()
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
}
