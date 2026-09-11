using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AtlasSoftPlc.Web.Tests;

/// <summary>
/// Factory que aísla la BD SQLite en un directorio temporal por ejecución de test,
/// evitando que los tests toquen la BD real de %LocalAppData%.
/// </summary>
public sealed class AtlasWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir;

    public AtlasWebFactory()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "AtlasSoftPlcTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public string DataDir => _dataDir;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataDir", _dataDir);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDir"] = _dataDir,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup; el temp aislado por GUID no ensucia el repo.
        }
    }
}