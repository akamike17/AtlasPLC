using Microsoft.Data.Sqlite;

namespace AtlasSoftPlc.Web.Auth;

/// <summary>
/// Repositorio de usuarios sobre SQLite. Los usuarios viven en su propia tabla
/// (Users) dentro de la misma BD del sistema, con hash Argon2id + sal.
/// </summary>
public interface IUserStore
{
    UserAccount? FindByUsername(string username);
    void Upsert(UserAccount user);
    IEnumerable<UserAccount> All();
}

public sealed class SqliteUserStore : IUserStore
{
    private readonly AtlasSoftPlc.Infrastructure.Persistence.SqliteStore _store;

    public SqliteUserStore(AtlasSoftPlc.Infrastructure.Persistence.SqliteStore store)
    {
        _store = store;
        // La tabla Users la crea SchemaMigrator (migración 1); no hace falta EnsureSchema aquí.
    }

    public UserAccount? FindByUsername(string username)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Username, PasswordHash, Role, DisplayName FROM Users WHERE Username = $u COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$u", username);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new UserAccount(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public void Upsert(UserAccount user)
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Users (Username, PasswordHash, Role, DisplayName) VALUES ($u, $h, $r, $d)
ON CONFLICT(Username) DO UPDATE SET PasswordHash = $h, Role = $r, DisplayName = $d";
        cmd.Parameters.AddWithValue("$u", user.Username);
        cmd.Parameters.AddWithValue("$h", user.PasswordHash);
        cmd.Parameters.AddWithValue("$r", user.Role);
        cmd.Parameters.AddWithValue("$d", (object?)user.DisplayName ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<UserAccount> All()
    {
        using var conn = _store.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Username, PasswordHash, Role, DisplayName FROM Users";
        using var reader = cmd.ExecuteReader();
        var list = new List<UserAccount>();
        while (reader.Read())
            list.Add(new UserAccount(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return list;
    }
}