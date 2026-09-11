using Microsoft.Data.Sqlite;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>
/// Apertura e inicialización del esquema SQLite (sección 60).
/// El esquema se aplica mediante migraciones versionadas (véase SchemaMigrator),
/// no con CREATE TABLE IF NOT EXISTS ad-hoc, para soportar evolución de esquema
/// entre despliegues sin corrupción silenciosa.
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

    /// <summary>Aplica migraciones pendientes. Idempotente (cada versión corre una sola vez).</summary>
    public void EnsureCreated() => SchemaMigrator.Migrate(this);

    /// <summary>Versión actual del esquema (para diagnóstico / health check).</summary>
    public int CurrentSchemaVersion() => SchemaMigrator.CurrentVersion(this);
}