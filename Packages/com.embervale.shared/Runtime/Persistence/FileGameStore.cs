using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Embervale.Shared.Data;

namespace Embervale.Shared.Persistence
{
    /// <summary>
    /// In-memory store with optional JSON file backing. Used by tests and by the Unity client's
    /// single-machine "local host" mode. The dedicated server uses SQLite instead.
    /// Writes go to a temp file then atomically replace the original, so a crash mid-save never
    /// corrupts existing data.
    /// </summary>
    public sealed class FileGameStore : IGameStore
    {
        private sealed class Account { public long Id; public string User; public string Hash; public string Salt; }

        private sealed class Snapshot
        {
            public long NextAccountId = 1;
            public long NextCharacterId = 1;
            public List<Account> Accounts = new List<Account>();
            public Dictionary<long, CharacterData> Characters = new Dictionary<long, CharacterData>();
            public WorldStateData World;
            public List<string> TradeLog = new List<string>();
        }

        private readonly string _path;
        private Snapshot _data = new Snapshot();
        private readonly object _lock = new object();

        public FileGameStore(string path = null)
        {
            _path = path;
            if (_path != null && File.Exists(_path))
                _data = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_path), GameDatabase.JsonSettings) ?? new Snapshot();
        }

        private void Flush()
        {
            if (_path == null) return;
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_data, Formatting.Indented, GameDatabase.JsonSettings));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }

        private static CharacterData Copy(CharacterData c) =>
            JsonConvert.DeserializeObject<CharacterData>(JsonConvert.SerializeObject(c, GameDatabase.JsonSettings), GameDatabase.JsonSettings);

        public long CreateAccount(string username, string passwordHash, string salt)
        {
            lock (_lock)
            {
                foreach (var a in _data.Accounts) if (a.User == username) return 0;
                var acc = new Account { Id = _data.NextAccountId++, User = username, Hash = passwordHash, Salt = salt };
                _data.Accounts.Add(acc);
                Flush();
                return acc.Id;
            }
        }

        public bool TryGetAccount(string username, out long accountId, out string passwordHash, out string salt)
        {
            lock (_lock)
            {
                foreach (var a in _data.Accounts)
                    if (a.User == username) { accountId = a.Id; passwordHash = a.Hash; salt = a.Salt; return true; }
                accountId = 0; passwordHash = salt = null;
                return false;
            }
        }

        public List<CharacterData> ListCharacters(long accountId)
        {
            lock (_lock)
            {
                var list = new List<CharacterData>();
                foreach (var c in _data.Characters.Values) if (c.AccountId == accountId) list.Add(Copy(c));
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                return list;
            }
        }

        public long CreateCharacter(long accountId, CharacterData data)
        {
            lock (_lock)
            {
                foreach (var c in _data.Characters.Values)
                    if (string.Equals(c.Name, data.Name, StringComparison.OrdinalIgnoreCase)) return 0;
                var copy = Copy(data);
                copy.Id = _data.NextCharacterId++;
                copy.AccountId = accountId;
                _data.Characters[copy.Id] = copy;
                Flush();
                return copy.Id;
            }
        }

        public CharacterData LoadCharacter(long characterId)
        {
            lock (_lock) return _data.Characters.TryGetValue(characterId, out var c) ? Copy(c) : null;
        }

        public void SaveCharacters(IEnumerable<CharacterData> characters)
        {
            lock (_lock)
            {
                foreach (var c in characters)
                    if (_data.Characters.ContainsKey(c.Id)) _data.Characters[c.Id] = Copy(c);
                Flush();
            }
        }

        public WorldStateData LoadWorldState() { lock (_lock) return _data.World; }

        public void SaveWorldState(WorldStateData state) { lock (_lock) { _data.World = state; Flush(); } }

        public void AppendTradeLog(string fromName, string toName, string itemsJson)
        {
            lock (_lock)
            {
                _data.TradeLog.Add(DateTime.UtcNow.ToString("o") + " " + fromName + " <-> " + toName + " " + itemsJson);
                if (_data.TradeLog.Count > 1000) _data.TradeLog.RemoveAt(0);
            }
        }
    }
}
