using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Embervale.Shared.Data
{
    /// <summary>
    /// Immutable, data-driven game definitions loaded from Assets/StreamingAssets/GameData.
    /// Shared by the Unity client and the dedicated server so both use identical rules.
    /// </summary>
    public sealed class GameDatabase
    {
        public ExperienceConfig Experience { get; private set; }
        public Dictionary<Skill, SkillDef> SkillDefs { get; } = new Dictionary<Skill, SkillDef>();
        public Dictionary<string, ItemDef> Items { get; } = new Dictionary<string, ItemDef>();
        public Dictionary<string, RecipeDef> Recipes { get; } = new Dictionary<string, RecipeDef>();
        public Dictionary<string, NodeDef> Nodes { get; } = new Dictionary<string, NodeDef>();
        public Dictionary<string, CropDef> Crops { get; } = new Dictionary<string, CropDef>();
        public Dictionary<string, MonsterDef> Monsters { get; } = new Dictionary<string, MonsterDef>();
        public Dictionary<string, LootTableDef> Loot { get; } = new Dictionary<string, LootTableDef>();
        public Dictionary<string, NpcDef> Npcs { get; } = new Dictionary<string, NpcDef>();
        public Dictionary<string, DialogueDef> Dialogues { get; } = new Dictionary<string, DialogueDef>();
        public Dictionary<string, ShopDef> Shops { get; } = new Dictionary<string, ShopDef>();
        public Dictionary<string, QuestDef> Quests { get; } = new Dictionary<string, QuestDef>();
        public Dictionary<string, SpellDef> Spells { get; } = new Dictionary<string, SpellDef>();
        public Dictionary<string, PrayerDef> Prayers { get; } = new Dictionary<string, PrayerDef>();
        public Dictionary<string, ThievingDef> Thieving { get; } = new Dictionary<string, ThievingDef>();
        public Dictionary<string, AgilityDef> Agility { get; } = new Dictionary<string, AgilityDef>();
        public StarterDef Starter { get; private set; }
        public WorldDef World { get; private set; }
        public ExperienceTable XpTable { get; private set; }

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        /// <summary>Loads every definition file from a directory (e.g. StreamingAssets/GameData).</summary>
        public static GameDatabase LoadFromDirectory(string dir, bool loadWorld = true)
        {
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("GameData directory not found: " + dir);
            return Load(name =>
            {
                var path = Path.Combine(dir, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, loadWorld);
        }

        /// <summary>Loads definitions through a file reader callback (used by Unity on platforms without File IO).</summary>
        public static GameDatabase Load(Func<string, string> readFile, bool loadWorld = true)
        {
            var db = new GameDatabase();
            T Read<T>(string name, bool required = true)
            {
                var text = readFile(name);
                if (text == null)
                {
                    if (required) throw new FileNotFoundException("Missing game data file " + name);
                    return default(T);
                }
                return JsonConvert.DeserializeObject<T>(text, JsonSettings);
            }

            db.Experience = Read<ExperienceConfig>("experience.json");
            db.XpTable = new ExperienceTable(db.Experience);
            foreach (var s in Read<List<SkillDef>>("skills.json")) db.SkillDefs[s.Id] = s;
            foreach (var i in Read<List<ItemDef>>("items.json")) db.Items[i.Id] = i;
            foreach (var r in Read<List<RecipeDef>>("recipes.json")) db.Recipes[r.Id] = r;
            foreach (var n in Read<List<NodeDef>>("nodes.json")) db.Nodes[n.Id] = n;
            foreach (var c in Read<List<CropDef>>("crops.json")) db.Crops[c.Id] = c;
            foreach (var m in Read<List<MonsterDef>>("monsters.json")) db.Monsters[m.Id] = m;
            foreach (var l in Read<List<LootTableDef>>("loot.json")) db.Loot[l.Id] = l;
            foreach (var n in Read<List<NpcDef>>("npcs.json")) db.Npcs[n.Id] = n;
            foreach (var d in Read<List<DialogueDef>>("dialogues.json")) db.Dialogues[d.Id] = d;
            foreach (var s in Read<List<ShopDef>>("shops.json")) db.Shops[s.Id] = s;
            foreach (var q in Read<List<QuestDef>>("quests.json")) db.Quests[q.Id] = q;
            foreach (var s in Read<List<SpellDef>>("spells.json")) db.Spells[s.Id] = s;
            foreach (var p in Read<List<PrayerDef>>("prayers.json")) db.Prayers[p.Id] = p;
            foreach (var t in Read<List<ThievingDef>>("thieving.json")) db.Thieving[t.Id] = t;
            foreach (var a in Read<List<AgilityDef>>("agility.json")) db.Agility[a.Id] = a;
            db.Starter = Read<StarterDef>("starter.json");
            if (loadWorld) db.World = Read<WorldDef>("world.json");
            return db;
        }

        public ItemDef Item(string id)
        {
            if (id != null && Items.TryGetValue(id, out var def)) return def;
            return null;
        }

        public ItemDef RequireItem(string id)
        {
            var def = Item(id);
            if (def == null) throw new KeyNotFoundException("Unknown item '" + id + "'");
            return def;
        }

        public bool IsStackable(string itemId)
        {
            var d = Item(itemId);
            return d != null && d.Stackable;
        }

        /// <summary>Cross-reference validation. Returns a list of human readable problems (empty = valid).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            void Chk(string item, string where)
            {
                if (item != null && !Items.ContainsKey(item)) errors.Add(where + " references unknown item '" + item + "'");
            }
            foreach (var r in Recipes.Values)
            {
                foreach (var i in r.Inputs) Chk(i.Item, "recipe " + r.Id);
                foreach (var o in r.Outputs) Chk(o.Item, "recipe " + r.Id);
                foreach (var t in r.Tools) Chk(t, "recipe " + r.Id);
                Chk(r.FailOutput, "recipe " + r.Id);
            }
            foreach (var m in Monsters.Values)
                if (!Loot.ContainsKey(m.LootTable)) errors.Add("monster " + m.Id + " has unknown loot table " + m.LootTable);
            foreach (var l in Loot.Values)
            {
                foreach (var e in l.Always) Chk(e.Item, "loot " + l.Id);
                foreach (var e in l.Entries) Chk(e.Item, "loot " + l.Id);
                foreach (var e in l.Rare) Chk(e.Item, "loot " + l.Id);
            }
            foreach (var n in Npcs.Values)
                if (n.Dialogue != null && !Dialogues.ContainsKey(n.Dialogue)) errors.Add("npc " + n.Id + " unknown dialogue");
            foreach (var s in Shops.Values) foreach (var st in s.Stock) Chk(st.Item, "shop " + s.Id);
            foreach (var q in Quests.Values) foreach (var it in q.Rewards.Items) Chk(it.Item, "quest " + q.Id);
            if (World != null)
            {
                foreach (var n in World.Nodes) if (!Nodes.ContainsKey(n.Def)) errors.Add("world node unknown def " + n.Def);
                foreach (var s in World.Spawns) if (!Monsters.ContainsKey(s.Monster)) errors.Add("world spawn unknown monster " + s.Monster);
                foreach (var n in World.Npcs) if (!Npcs.ContainsKey(n.Def)) errors.Add("world npc unknown def " + n.Def);
                if (Starter != null && !World.Locations.ContainsKey(Starter.SpawnPoint)) errors.Add("starter spawn point missing");
            }
            return errors;
        }
    }
}
