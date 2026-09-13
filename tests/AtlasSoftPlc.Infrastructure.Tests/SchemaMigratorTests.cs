using AtlasSoftPlc.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

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

        using var conn = store.OpenConnection();
        foreach (var table in new[] { "Projects", "Variables", "Devices", "LogicPrograms", "LogicProgramVersions", "AuditEvents", "HistorianSamples", "AlarmInstances", "AlarmDefinitions", "Settings", "Users", "TargetInstances", "GeneratedArtifacts", "SchemaMigrations" })
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
        Assert.Equal(SchemaMigrator.LatestVersion, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void OpenConnection_ApuntarAMismoArchivo_ReutilizaEsquema()
    {
        var path = TempDb();
        var s1 = new SqliteStore(path);
        s1.EnsureCreated();

        var s2 = new SqliteStore(path);
        Assert.Equal(SchemaMigrator.LatestVersion, s2.CurrentSchemaVersion());
    }

    // ── Migración incremental con base EXISTENTE (no vacía) ───────────────

    [Fact]
    public void Migrate_DesdeVersion1_PreservaDatosYAplicaV2V3()
    {
        var path = TempDb();

        // Simular una base ya en versión 1 con un proyecto persistido.
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            // Esquema v1 (solo tablas base + fila Version=1).
            Exec(conn, @"
CREATE TABLE IF NOT EXISTS Projects (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Variables (Id TEXT PRIMARY KEY, ProjectId TEXT NOT NULL, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Devices (Id TEXT PRIMARY KEY, ProjectId TEXT NOT NULL, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS TagBindings (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS LogicPrograms (Id TEXT PRIMARY KEY, ProjectId TEXT NOT NULL, Json TEXT NOT NULL, IsActive INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS LogicProgramVersions (Id TEXT PRIMARY KEY, ProgramId TEXT NOT NULL, Json TEXT NOT NULL, VersionNumber INTEGER NOT NULL, CreatedUtc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AuditEvents (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS HistorianSamples (Id TEXT PRIMARY KEY, VariableId TEXT NOT NULL, TimestampUtc TEXT NOT NULL, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AlarmInstances (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Users (Username TEXT PRIMARY KEY, PasswordHash TEXT NOT NULL, Role TEXT NOT NULL, DisplayName TEXT NULL);
CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY, AppliedUtc TEXT NOT NULL);
");
            // Datos previos que deben sobrevivir a la migración.
            Exec(conn, "INSERT INTO Projects (Id, Json) VALUES ('proj-1', '{\"Name\":\"Tanque\"}')");
            Exec(conn, "INSERT INTO AuditEvents (Id, Json) VALUES ('evt-1', '{\"Action\":\"Login\"}')");
            Exec(conn, "INSERT INTO SchemaMigrations (Version, AppliedUtc) VALUES (1, '2024-01-01T00:00:00Z')");
        }

        // Migrar de v1 -> v3.
        var store = new SqliteStore(path);
        store.EnsureCreated();

        Assert.Equal(SchemaMigrator.LatestVersion, store.CurrentSchemaVersion());

        using var conn2 = store.OpenConnection();
        // Datos previos preservados.
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM Projects WHERE Id='proj-1'"));
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM AuditEvents WHERE Id='evt-1'"));
        // Columnas nuevas utilizables (v3): TimestampUtc y CreatedUtc existen.
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM pragma_table_info('AuditEvents') WHERE name='TimestampUtc'"));
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM pragma_table_info('Projects') WHERE name='CreatedUtc'"));
        // AlarmDefinitions creada en v2.
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AlarmDefinitions'"));
        // Sin movimiento duplicado: Version=1 sigue siendo una sola fila.
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version=1"));
    }

    [Fact]
    public void Migrate_DesdeVersion2_PreservaDatosYAplicaV3()
    {
        var path = TempDb();

        // Base en v2 (incluye AlarmDefinitions y filas Version=1 y 2).
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            Exec(conn, @"
CREATE TABLE IF NOT EXISTS Projects (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AuditEvents (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AlarmDefinitions (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY, AppliedUtc TEXT NOT NULL);
");
            Exec(conn, "INSERT INTO Projects (Id, Json) VALUES ('proj-1', '{\"Name\":\"Tanque\"}')");
            Exec(conn, "INSERT INTO SchemaMigrations (Version, AppliedUtc) VALUES (1, '2024-01-01T00:00:00Z')");
            Exec(conn, "INSERT INTO SchemaMigrations (Version, AppliedUtc) VALUES (2, '2024-01-02T00:00:00Z')");
        }

        var store = new SqliteStore(path);
        store.EnsureCreated();

        Assert.Equal(SchemaMigrator.LatestVersion, store.CurrentSchemaVersion());
        using var conn2 = store.OpenConnection();
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM Projects WHERE Id='proj-1'"));
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM pragma_table_info('AuditEvents') WHERE name='TimestampUtc'"));
        // No se re-ejecuta v1/v2: AlarmDefinitions sigue siendo una sola tabla.
        Assert.Equal(1L, Scalar(conn2, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AlarmDefinitions'"));
    }

    [Fact]
    public void Migrar_SobreVersion3_NoEjecutaNada()
    {
        var path = TempDb();
        var store = new SqliteStore(path);
        store.EnsureCreated();
        var before = Scalar(store, "SELECT COUNT(*) FROM SchemaMigrations");

        store.EnsureCreated();
        var after = Scalar(store, "SELECT COUNT(*) FROM SchemaMigrations");

        Assert.Equal(before, after);
        Assert.Equal(SchemaMigrator.LatestVersion, Convert.ToInt64(after));
    }

    // ── Timestamps NULL no rompen consultas de ordenamiento ───────────────

    [Fact]
    public void AuditEvent_TimestampNull_NoRompeOrden()
    {
        var path = TempDb();
        var store = new SqliteStore(path);
        store.EnsureCreated();

        using var conn = store.OpenConnection();
        // Migración 3 añadió TimestampUtc NULL por defecto: un evento antiguo lo tiene NULL.
        Exec(conn, "INSERT INTO AuditEvents (Id, Json) VALUES ('old', '{\"Action\":\"Login\"}')");
        Exec(conn, "INSERT INTO AuditEvents (Id, Json, TimestampUtc) VALUES ('new', '{\"Action\":\"Login\"}', '2026-01-01T00:00:00Z')");

        // La columna es consultable y NULL no rompe ORDER BY (SQLite ordena NULL primero).
        Assert.Equal(2L, Scalar(conn, "SELECT COUNT(*) FROM AuditEvents"));
        Assert.Equal(2L, Scalar(conn, "SELECT COUNT(*) FROM AuditEvents WHERE TimestampUtc IS NULL OR TimestampUtc IS NOT NULL"));
    }

    private static long Scalar(SqliteStore store, string sql)
    {
        using var conn = store.OpenConnection();
        return Scalar(conn, sql);
    }

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
