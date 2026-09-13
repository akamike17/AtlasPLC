using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                ["Auth:SeedPassword"] = "AtlasDemo!2026",
            });
        });
        builder.ConfigureServices(services =>
        {
            // La fábrica no debe leer las claves DPAPI del perfil del desarrollador:
            // pueden pertenecer a otro usuario/contexto y romper cookies/antiforgery.
            services.AddSingleton<IDataProtectionProvider>(
                new EphemeralDataProtectionProvider());
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
