using Microsoft.Data.Sqlite;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>
/// Runner de migraciones de esquema SQLite, versionadas y transaccionales.
/// Cada migración es un (int Version, string Sql) inmutable. Se aplican en orden,
/// cada una dentro de su propia transacción, registrando la versión en SchemaMigrations.
/// </summary>
public static class SchemaMigrator
{
    public const int LatestVersion = 7;

    private static readonly (int Version, string Sql)[] Migrations =
    {
        (1, Migration1),
        (2, Migration2),
        (3, Migration3),
        (4, Migration4),
        (5, Migration5),
        (6, Migration6),
        (7, Migration7),
    };

    public static void Migrate(SqliteStore store)
    {
        using var conn = store.OpenConnection();
        EnsureMigrationTable(conn);

        var applied = GetAppliedVersions(conn);
        foreach (var (version, sql) in Migrations)
        {
            if (applied.Contains(version))
                continue;

            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO SchemaMigrations (Version, AppliedUtc) VALUES ($v, $t)";
                cmd.Parameters.AddWithValue("$v", version);
                cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public static int CurrentVersion(SqliteStore store)
    {
        using var conn = store.OpenConnection();
        EnsureMigrationTable(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations";
        var result = cmd.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    private static void EnsureMigrationTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SchemaMigrations (
    Version INTEGER PRIMARY KEY,
    AppliedUtc TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection conn)
    {
        var set = new HashSet<int>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Version FROM SchemaMigrations";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetInt32(0));
        return set;
    }

    // ── Migración 1: esquema base ──────────────────────────────
    private const string Migration1 = @"
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
CREATE TABLE IF NOT EXISTS Users (
    Username TEXT PRIMARY KEY,
    PasswordHash TEXT NOT NULL,
    Role TEXT NOT NULL,
    DisplayName TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_Variables_Project ON Variables(ProjectId);
CREATE INDEX IF NOT EXISTS IX_Programs_Project ON LogicPrograms(ProjectId);
CREATE INDEX IF NOT EXISTS IX_Historian_Variable ON HistorianSamples(VariableId, TimestampUtc);
";

    // ── Migración 2: definiciones de alarmas (separadas de las instancias) ──
    private const string Migration2 = @"
CREATE TABLE IF NOT EXISTS AlarmDefinitions (
    Id TEXT PRIMARY KEY,
    Json TEXT NOT NULL
);
";

    // ── Migración 3: columnas de timestamp ordenables para auditoría y proyectos ──
    // Antes se ordenaba por el JSON literal (no cronológico). Añadimos columnas reales
    // para ordenar por fecha de forma fiable.
    private const string Migration3 = @"
ALTER TABLE AuditEvents ADD COLUMN TimestampUtc TEXT NULL;
ALTER TABLE Projects ADD COLUMN CreatedUtc TEXT NULL;
";

    // ── Migración 4: biblioteca de programas PLC persistentes ──
    // Cada fila es una PlcProgramDefinition completa (variables + lógica + failsafe +
    // mapa Modbus + metadata) serializada como JSON. Sustenta cargar/ejecutar múltiples
    // programas dentro del mismo Atlas SoftPLC.
    private const string Migration4 = @"
CREATE TABLE IF NOT EXISTS PlcPrograms (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Json TEXT NOT NULL
);
";

    private const string Migration5 = @"
CREATE TABLE IF NOT EXISTS TargetConfigurations (
    TargetId TEXT PRIMARY KEY,
    Endpoint TEXT NOT NULL,
    Port INTEGER NOT NULL,
    TimeoutMs INTEGER NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
";

    private const string Migration6 = @"
CREATE TABLE IF NOT EXISTS ProgramGraphs (
    ProgramId TEXT PRIMARY KEY,
    SchemaVersion INTEGER NOT NULL,
    Json TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
";

    private const string Migration7 = @"
CREATE TABLE IF NOT EXISTS ProgramTargetSelections (
    ProgramId TEXT PRIMARY KEY,
    TargetId TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
";
}
