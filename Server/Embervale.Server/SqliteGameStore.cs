using System;
using System.Collections.Generic;
using System.IO;
using Embervale.Shared.Data;
using Embervale.Shared.Persistence;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace Embervale.Server
{
    /// <summary>
    /// SQLite persistence. Characters are stored as a JSON document plus indexed columns, so adding
    /// fields never requires a migration. Multi-character saves (trades) run in one transaction.
    /// </summary>
    public sealed class SqliteGameStore : IGameStore, IDisposable
    {
        private readonly SqliteConnection _db;
        private readonly object _lock = new object();

        public SqliteGameStore(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
            _db.Open();
            Exec("PRAGMA journal_mode=WAL;");
            Exec("PRAGMA synchronous=NORMAL;");
            Exec("PRAGMA foreign_keys=ON;");
            Migrate();
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private void Migrate()
        {
            Exec(@"CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
            long version;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
                version = (long)cmd.ExecuteScalar();
            }
            if (version < 1)
            {
                Exec(@"
CREATE TABLE accounts (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT NOT NULL UNIQUE COLLATE NOCASE,
  password_hash TEXT NOT NULL,
  salt TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  last_login TEXT
);
CREATE TABLE characters (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id INTEGER NOT NULL REFERENCES accounts(id),
  name TEXT NOT NULL UNIQUE COLLATE NOCASE,
  data TEXT NOT NULL,
  saved_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX idx_characters_account ON characters(account_id);
CREATE TABLE world_state (id INTEGER PRIMARY KEY CHECK (id = 1), data TEXT NOT NULL, saved_at TEXT NOT NULL);
CREATE TABLE trade_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  at TEXT NOT NULL DEFAULT (datetime('now')),
  party_a TEXT NOT NULL,
  party_b TEXT NOT NULL,
  details TEXT
);
INSERT INTO schema_version(version) VALUES (1);");
            }
        }

        private static string Ser(object o) => JsonConvert.SerializeObject(o, GameDatabase.JsonSettings);
        private static T De<T>(string s) => JsonConvert.DeserializeObject<T>(s, GameDatabase.JsonSettings);

        public long CreateAccount(string username, string passwordHash, string salt)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO accounts(username, password_hash, salt) VALUES ($u, $h, $s); SELECT changes();";
                cmd.Parameters.AddWithValue("$u", username);
                cmd.Parameters.AddWithValue("$h", passwordHash);
                cmd.Parameters.AddWithValue("$s", salt);
                if ((long)cmd.ExecuteScalar() == 0) return 0;
                using var id = _db.CreateCommand();
                id.CommandText = "SELECT last_insert_rowid()";
                return (long)id.ExecuteScalar();
            }
        }

        public bool TryGetAccount(string username, out long accountId, out string passwordHash, out string salt)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id, password_hash, salt FROM accounts WHERE username = $u";
                cmd.Parameters.AddWithValue("$u", username);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    accountId = r.GetInt64(0); passwordHash = r.GetString(1); salt = r.GetString(2);
                    r.Close();
                    using var upd = _db.CreateCommand();
                    upd.CommandText = "UPDATE accounts SET last_login = datetime('now') WHERE id = $id";
                    upd.Parameters.AddWithValue("$id", accountId);
                    upd.ExecuteNonQuery();
                    return true;
                }
                accountId = 0; passwordHash = salt = null;
                return false;
            }
        }

        public List<CharacterData> ListCharacters(long accountId)
        {
            lock (_lock)
            {
                var list = new List<CharacterData>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id, data FROM characters WHERE account_id = $a ORDER BY id";
                cmd.Parameters.AddWithValue("$a", accountId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var c = De<CharacterData>(r.GetString(1));
                    c.Id = r.GetInt64(0);
                    c.AccountId = accountId;
                    list.Add(c);
                }
                return list;
            }
        }

        public long CreateCharacter(long accountId, CharacterData data)
        {
            lock (_lock)
            {
                using var tx = _db.BeginTransaction();
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO characters(account_id, name, data) VALUES ($a, $n, '{}'); SELECT changes();";
                cmd.Parameters.AddWithValue("$a", accountId);
                cmd.Parameters.AddWithValue("$n", data.Name);
                if ((long)cmd.ExecuteScalar() == 0) { tx.Rollback(); return 0; }
                using var idCmd = _db.CreateCommand();
                idCmd.Transaction = tx;
                idCmd.CommandText = "SELECT last_insert_rowid()";
                long id = (long)idCmd.ExecuteScalar();
                data.Id = id;
                data.AccountId = accountId;
                using var upd = _db.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE characters SET data = $d WHERE id = $id";
                upd.Parameters.AddWithValue("$d", Ser(data));
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
                tx.Commit();
                return id;
            }
        }

        public CharacterData LoadCharacter(long characterId)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT account_id, data FROM characters WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", characterId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                var c = De<CharacterData>(r.GetString(1));
                c.Id = characterId;
                c.AccountId = r.GetInt64(0);
                return c;
            }
        }

        public void SaveCharacters(IEnumerable<CharacterData> characters)
        {
            lock (_lock)
            {
                using var tx = _db.BeginTransaction();
                foreach (var c in characters)
                {
                    using var cmd = _db.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE characters SET data = $d, saved_at = datetime('now') WHERE id = $id";
                    cmd.Parameters.AddWithValue("$d", Ser(c));
                    cmd.Parameters.AddWithValue("$id", c.Id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        public WorldStateData LoadWorldState()
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT data FROM world_state WHERE id = 1";
                var o = cmd.ExecuteScalar();
                return o is string s ? De<WorldStateData>(s) : null;
            }
        }

        public void SaveWorldState(WorldStateData state)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "INSERT INTO world_state(id, data, saved_at) VALUES (1, $d, datetime('now')) ON CONFLICT(id) DO UPDATE SET data = excluded.data, saved_at = excluded.saved_at";
                cmd.Parameters.AddWithValue("$d", Ser(state));
                cmd.ExecuteNonQuery();
            }
        }

        public void AppendTradeLog(string fromName, string toName, string itemsJson)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "INSERT INTO trade_log(party_a, party_b, details) VALUES ($a, $b, $d)";
                cmd.Parameters.AddWithValue("$a", fromName);
                cmd.Parameters.AddWithValue("$b", toName);
                cmd.Parameters.AddWithValue("$d", itemsJson ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose() => _db.Dispose();
    }
}
