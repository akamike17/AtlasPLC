using AtlasSoftPlc.Infrastructure.Persistence;

namespace AtlasSoftPlc.Infrastructure.Tests;

/// <summary>Pruebas del migrador de esquema versionado.</summary>
public sealed class SchemaMigratorTests
{
    private static string TempDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AtlasSoftPlcMigratorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test.db");
    }

    [Fact]
    public void Migrate_CreaEsquemaCompleto()
    {
        var path = TempDb();
        var store = new SqliteStore(path);
        store.EnsureCreated();

        Assert.Equal(SchemaMigrator.LatestVersion, store.CurrentSchemaVersion());

        // Las tablas principales existen
        using var conn = store.OpenConnection();
        foreach (var table in new[] { "Projects", "Variables", "Devices", "LogicPrograms", "LogicProgramVersions", "AuditEvents", "HistorianSamples", "AlarmInstances", "AlarmDefinitions", "Settings", "Users", "SchemaMigrations" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
    }

    [Fact]
    public void Migrate_EsIdempotente()
    {
        var path = TempDb();
        var store = new SqliteStore(path);
        store.EnsureCreated();
        var v1 = store.CurrentSchemaVersion();

        store.EnsureCreated(); // segunda llamada no debe duplicar

        Assert.Equal(v1, store.CurrentSchemaVersion());
        using var conn = store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaMigrations";
        Assert.Equal(SchemaMigrator.LatestVersion, Convert.ToInt64(cmd.ExecuteScalar())); // una fila por versión, sin duplicados
    }

    [Fact]
    public void OpenConnection_ApuntarAMismoArchivo_ReutilizaEsquema()
    {
        var path = TempDb();
        var s1 = new SqliteStore(path);
        s1.EnsureCreated();

        // Una segunda instancia apuntando al mismo archivo ve el esquema ya migrado.
        var s2 = new SqliteStore(path);
        Assert.Equal(SchemaMigrator.LatestVersion, s2.CurrentSchemaVersion());
    }
}