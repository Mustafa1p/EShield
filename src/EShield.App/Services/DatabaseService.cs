using Microsoft.Data.Sqlite;
using System.IO;
using EShield.Models;

namespace EShield.Services;

/// <summary>SQLite persistence via Microsoft.Data.Sqlite.</summary>
public class DatabaseService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    public DatabaseService(string? directory = null)
    {
        var dir = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EShield");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "eshield.db");
    }

    public void Initialize()
    {
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();
        CreateSchema();
    }

    private void CreateSchema()
    {
        Exec(@"
            CREATE TABLE IF NOT EXISTS process (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                name            TEXT NOT NULL,
                executable_path TEXT NOT NULL,
                created_at      REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS flow (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                hostname        TEXT,
                remote_ip       TEXT NOT NULL,
                remote_port     INTEGER NOT NULL,
                protocol        INTEGER NOT NULL,
                direction       INTEGER NOT NULL,
                bytes_sent      INTEGER NOT NULL DEFAULT 0,
                bytes_received  INTEGER NOT NULL DEFAULT 0,
                process_id      INTEGER NOT NULL REFERENCES process(id),
                created_at      REAL NOT NULL,
                is_blocked      INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS block_rule (
                id              TEXT PRIMARY KEY,
                domain          TEXT,
                executable_path TEXT,
                app_name        TEXT,
                rule_type       INTEGER NOT NULL,
                created_at      REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS allow_rule (
                id              TEXT PRIMARY KEY,
                domain          TEXT,
                executable_path TEXT,
                app_name        TEXT,
                rule_type       INTEGER NOT NULL,
                created_at      REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS traffic_bucket (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                bucket_time REAL NOT NULL,
                bytes_in    INTEGER NOT NULL DEFAULT 0,
                bytes_out   INTEGER NOT NULL DEFAULT 0
            );
        ");
    }

    // ── Traffic Buckets ──────────────────────────────────────────────────────
    public void InsertTrafficBucket(DateTime time, long bytesIn, long bytesOut)
    {
        Exec("INSERT INTO traffic_bucket (bucket_time, bytes_in, bytes_out) VALUES (@t, @i, @o)",
            ("@t", ToEpoch(time)), ("@i", bytesIn), ("@o", bytesOut));
    }

    public List<TrafficPoint> GetTrafficHistory(DateTime since)
    {
        var result = new List<TrafficPoint>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT bucket_time, bytes_in, bytes_out FROM traffic_bucket WHERE bucket_time >= @s ORDER BY bucket_time";
        cmd.Parameters.AddWithValue("@s", ToEpoch(since));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new TrafficPoint { Time = FromEpoch(r.GetDouble(0)), BytesIn = r.GetInt64(1), BytesOut = r.GetInt64(2) });
        return result;
    }

    // ── Block Rules ──────────────────────────────────────────────────────────
    public void SaveBlockRule(BlockRule rule)
    {
        Exec(@"INSERT OR REPLACE INTO block_rule (id, domain, executable_path, app_name, rule_type, created_at)
               VALUES (@id, @d, @e, @a, @t, @c)",
            ("@id", rule.Id.ToString()), ("@d", (object?)rule.Domain ?? DBNull.Value),
            ("@e", (object?)rule.ExecutablePath ?? DBNull.Value), ("@a", (object?)rule.AppName ?? DBNull.Value),
            ("@t", (int)rule.RuleType), ("@c", ToEpoch(rule.CreatedAt)));
    }

    public void DeleteBlockRule(Guid id) =>
        Exec("DELETE FROM block_rule WHERE id = @id", ("@id", id.ToString()));

    public List<BlockRule> GetBlockRules()
    {
        var result = new List<BlockRule>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT id, domain, executable_path, app_name, rule_type, created_at FROM block_rule ORDER BY created_at DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new BlockRule
            {
                Id = Guid.Parse(r.GetString(0)),
                Domain = r.IsDBNull(1) ? null : r.GetString(1),
                ExecutablePath = r.IsDBNull(2) ? null : r.GetString(2),
                AppName = r.IsDBNull(3) ? null : r.GetString(3),
                RuleType = (RuleType)r.GetInt32(4),
                CreatedAt = FromEpoch(r.GetDouble(5))
            });
        return result;
    }

    // ── Allow Rules ──────────────────────────────────────────────────────────
    public void SaveAllowRule(AllowRule rule)
    {
        Exec(@"INSERT OR REPLACE INTO allow_rule (id, domain, executable_path, app_name, rule_type, created_at)
               VALUES (@id, @d, @e, @a, @t, @c)",
            ("@id", rule.Id.ToString()), ("@d", (object?)rule.Domain ?? DBNull.Value),
            ("@e", (object?)rule.ExecutablePath ?? DBNull.Value), ("@a", (object?)rule.AppName ?? DBNull.Value),
            ("@t", (int)rule.RuleType), ("@c", ToEpoch(rule.CreatedAt)));
    }

    public void DeleteAllowRule(Guid id) =>
        Exec("DELETE FROM allow_rule WHERE id = @id", ("@id", id.ToString()));

    public List<AllowRule> GetAllowRules()
    {
        var result = new List<AllowRule>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT id, domain, executable_path, app_name, rule_type, created_at FROM allow_rule ORDER BY created_at DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new AllowRule
            {
                Id = Guid.Parse(r.GetString(0)),
                Domain = r.IsDBNull(1) ? null : r.GetString(1),
                ExecutablePath = r.IsDBNull(2) ? null : r.GetString(2),
                AppName = r.IsDBNull(3) ? null : r.GetString(3),
                RuleType = (RuleType)r.GetInt32(4),
                CreatedAt = FromEpoch(r.GetDouble(5))
            });
        return result;
    }

    public void ClearAllActivity()
    {
        Exec("DELETE FROM flow");
        Exec("DELETE FROM process");
        Exec("DELETE FROM traffic_bucket");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void Exec(string sql, params (string, object?)[] parameters)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static double ToEpoch(DateTime dt) =>
        (dt.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
    private static DateTime FromEpoch(double epoch) =>
        DateTime.UnixEpoch.AddSeconds(epoch).ToLocalTime();

    public void Dispose() => _conn?.Dispose();
}
