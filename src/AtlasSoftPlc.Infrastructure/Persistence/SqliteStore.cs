using Microsoft.Data.Sqlite;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>
/// Apertura e inicialización del esquema SQLite (sección 60).
/// Almacena configuración como JSON en columnas de texto para mantener
/// simple la persistencia de modelos ricos.
/// </summary>
public sealed class SqliteStore
{
    private readonly string _connectionString;

    public SqliteStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void EnsureCreated()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Projects (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Variables (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT NOT NULL,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Devices (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT NOT NULL,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS TagBindings (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS LogicPrograms (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT NOT NULL,
    Json TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS LogicProgramVersions (
    Id TEXT PRIMARY KEY,
    ProgramId TEXT NOT NULL,
    Json TEXT NOT NULL,
    VersionNumber INTEGER NOT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS AuditEvents (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS HistorianSamples (
    Id TEXT PRIMARY KEY,
    VariableId TEXT NOT NULL,
    TimestampUtc TEXT NOT NULL,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS AlarmInstances (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Settings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Variables_Project ON Variables(ProjectId);
CREATE INDEX IF NOT EXISTS IX_Programs_Project ON LogicPrograms(ProjectId);
CREATE INDEX IF NOT EXISTS IX_Historian_Variable ON HistorianSamples(VariableId, TimestampUtc);
";
        cmd.ExecuteNonQuery();
    }
}