using System;
using System.Collections.Generic;

namespace Embervale.Shared.Data
{
    public enum Skill
    {
        Attack, Strength, Defense, Ranged, Magic, Vitality,
        Woodcutting, Mining, Fishing, Farming,
        Smithing, Cooking, Crafting, Fletching, Herblore, Runecrafting,
        Agility, Thieving, Prayer, Firemaking
    }

    public static class Skills
    {
        public static readonly Skill[] All = (Skill[])Enum.GetValues(typeof(Skill));
        public static int Count => All.Length;
    }

    public enum EquipSlot
    {
        None = -1, Head = 0, Body, Legs, Feet, Hands, Weapon, Shield, Ring, Amulet, Cape, Ammo
    }

    public static class EquipSlots
    {
        public const int Count = 11;
    }

    public enum CombatStyle { Melee, Ranged, Magic }

    /// <summary>Which skill receives experience from melee damage.</summary>
    public enum MeleeStance { Accurate, Aggressive, Defensive }

    // ------------------------------------------------------------------
    // Experience
    // ------------------------------------------------------------------
    public sealed class ExperienceConfig
    {
        public int MaxLevel = 99;
        public string Formula = "geometric";
        public double Base = 80;
        public double Growth = 1.1;
        public double Linear = 20;
        public Dictionary<string, int> StartingLevels = new Dictionary<string, int>();
    }

    public sealed class SkillDef
    {
        public Skill Id;
        public string Category;
        public string Description;
        public string Icon;
    }

    // ------------------------------------------------------------------
    // Items
    // ------------------------------------------------------------------
    public sealed class Requirement
    {
        public Skill Skill;
        public int Level;
    }

    public sealed class ItemStats
    {
        public int AttackMelee, AttackRanged, AttackMagic;
        public int StrengthMelee, StrengthRanged, MagicPower;
        public int DefenseMelee, DefenseRanged, DefenseMagic;
        public int AttackSpeed;   // ticks between attacks (weapons only)
        public int AttackRange;   // tiles (weapons only)

        public void Add(ItemStats o)
        {
            if (o == null) return;
            AttackMelee += o.AttackMelee; AttackRanged += o.AttackRanged; AttackMagic += o.AttackMagic;
            StrengthMelee += o.StrengthMelee; StrengthRanged += o.StrengthRanged; MagicPower += o.MagicPower;
            DefenseMelee += o.DefenseMelee; DefenseRanged += o.DefenseRanged; DefenseMagic += o.DefenseMagic;
        }
    }

    public sealed class ConsumeEffect
    {
        public int Heal;
        public string Buff;
        public int Amount;
        public int DurationTicks;
    }

    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string Icon;
        public string Model;
        public string Tint;
        public string Category;
        public bool Stackable;
        public int Value;
        public float Weight;
        public bool Tradeable = true;
        public EquipSlot Slot = EquipSlot.None;
        public List<Requirement> Requirements = new List<Requirement>();
        public ItemStats Stats;
        public string WeaponType;
        public bool TwoHanded;
        public string ToolType;
        public float ToolPower;
        public int ToolLevel;
        public int Heal;
        public int BuryXp;
        public string AmmoType;
        public ConsumeEffect ConsumeEffect;

        public bool IsEquippable => Slot != EquipSlot.None;
        public bool IsFood => Heal > 0;
        public CombatStyle WeaponStyle =>
            WeaponType == "Bow" || WeaponType == "Crossbow" ? CombatStyle.Ranged :
            WeaponType == "Staff" ? CombatStyle.Magic : CombatStyle.Melee;
    }

    // ------------------------------------------------------------------
    // Recipes
    // ------------------------------------------------------------------
    public sealed class ItemAmount
    {
        public string Item;
        public int Qty = 1;
    }

    public sealed class RecipeDef
    {
        public string Id;
        public Skill Skill;
        public int Level;
        public string Station;              // null = can be made anywhere from inventory
        public List<string> AltStations = new List<string>();
        public List<ItemAmount> Inputs = new List<ItemAmount>();
        public List<string> Tools = new List<string>();
        public List<ItemAmount> Outputs = new List<ItemAmount>();
        public double Xp;
        public int Ticks = 4;
        public string FailOutput;
        public double FailChanceAtLevel;
        public int NoFailLevel;
        public bool SpawnsFire;
        public bool Bulk;

        public bool StationMatches(string station)
        {
            if (Station == null) return true;
            if (station == null) return false;
            return Station == station || (AltStations != null && AltStations.Contains(station));
        }
    }

    // ------------------------------------------------------------------
    // Gathering
    // ------------------------------------------------------------------
    public sealed class NodeYield
    {
        public string Item;
        public int Level;
        public double Xp;
        public int Weight = 1;
    }

    public sealed class NodeDef
    {
        public string Id;
        public string Name;
        public Skill Skill;
        public string ToolType;
        public int Level;
        public List<NodeYield> Yields = new List<NodeYield>();
        public double BaseChance = 0.4;
        public double DepleteChance = 1.0;
        public int RespawnTicks;
        public string Model;
        public string DepletedModel;
        public string Bait;
        public string RequiresItem;
        public double GemChance;

        public int MinLevel
        {
            get
            {
                int min = int.MaxValue;
                foreach (var y in Yields) min = Math.Min(min, y.Level);
                return min == int.MaxValue ? Level : min;
            }
        }
    }

    public sealed class CropDef
    {
        public string Id;
        public string Name;
        public string Seed;
        public string Produce;
        public int Level;
        public double PlantXp;
        public double HarvestXp;
        public int TicksPerStage;
        public int Stages = 4;
        public int YieldMin;
        public int YieldMax;
        public double FailChanceUnwatered;
        public double FailChanceWatered;
    }

    // ------------------------------------------------------------------
    // Monsters + loot
    // ------------------------------------------------------------------
    public sealed class AbilityDef
    {
        public string Id;
        public int EveryAttacks;
        public double AtHpFraction;
        public int Radius;
        public int MaxHit;
        public int TelegraphTicks;
        public int Poison;
        public string Monster;
        public int Count;
    }

    public sealed class MonsterDef
    {
        public string Id;
        public string Name;
        public int Level;
        public int Hp;
        public int Attack, Strength, Defense;
        public int AttackSpeed = 5;
        public bool Aggressive;
        public int AggroRange;
        public int WanderRange;
        public int ChaseRange = 12;
        public int RespawnTicks;
        public string LootTable;
        public string Model;
        public CombatStyle Style = CombatStyle.Melee;
        public int AttackRange = 1;
        public bool Boss;
        public int Poison;
        public List<AbilityDef> Abilities = new List<AbilityDef>();
    }

    public sealed class LootEntry
    {
        public string Item;
        public int Min = 1;
        public int Max = 1;
        public int Weight = 1;
    }

    public sealed class LootTableDef
    {
        public string Id;
        public List<LootEntry> Always = new List<LootEntry>();
        public int Rolls = 1;
        public List<LootEntry> Entries = new List<LootEntry>();
        public int NothingWeight;
        public List<LootEntry> Rare = new List<LootEntry>();
        public double RareChance;
    }

    // ------------------------------------------------------------------
    // NPCs / dialogue / shops / quests
    // ------------------------------------------------------------------
    public sealed class NpcDef
    {
        public string Id;
        public string Name;
        public string Model;
        public string Role;
        public string Dialogue;
        public int Wander;
    }

    public sealed class DialogueCondition
    {
        public string QuestNotStarted;
        public List<string> QuestStage;     // [questId, stageId]
        public string QuestComplete;
    }

    public sealed class DialogueAction
    {
        public string StartQuest;
        public string CompleteQuestStage;
        public string OpenShop;
        public bool OpenBank;
        public bool OpenMarket;
    }

    public sealed class DialogueOption
    {
        public string Text;
        public string Next;
        public DialogueCondition Condition;
        public DialogueAction Action;
    }

    public sealed class DialogueNode
    {
        public string Id;
        public string Speaker;
        public string Text;
        public List<DialogueOption> Options = new List<DialogueOption>();
    }

    public sealed class DialogueDef
    {
        public string Id;
        public List<DialogueNode> Nodes = new List<DialogueNode>();

        public DialogueNode Find(string id)
        {
            foreach (var n in Nodes) if (n.Id == id) return n;
            return null;
        }
    }

    public sealed class ShopStock
    {
        public string Item;
        public int Qty;
        public int RestockTicks = 50;
    }

    public sealed class ShopDef
    {
        public string Id;
        public string Name;
        public bool GeneralStore;
        public double BuyMultiplier = 1.0;
        public double SellMultiplier = 0.4;
        public List<ShopStock> Stock = new List<ShopStock>();
    }

    public enum ObjectiveType { Collect, Kill, Talk, Visit, Craft, Gather }

    public sealed class QuestObjective
    {
        public ObjectiveType Type;
        public string Target;
        public int Count = 1;
        public bool Consume;
    }

    public sealed class QuestStage
    {
        public string Id;
        public string Journal;
        public List<QuestObjective> Objectives = new List<QuestObjective>();
    }

    public sealed class XpReward
    {
        public Skill Skill;
        public double Amount;
    }

    public sealed class QuestRewards
    {
        public List<XpReward> Xp = new List<XpReward>();
        public List<ItemAmount> Items = new List<ItemAmount>();
    }

    public sealed class QuestRequirements
    {
        public List<Requirement> Skills = new List<Requirement>();
        public List<string> Quests = new List<string>();
    }

    public sealed class QuestDef
    {
        public string Id;
        public string Name;
        public string Category;
        public string Description;
        public int QuestPoints;
        public QuestRequirements Requirements = new QuestRequirements();
        public List<QuestStage> Stages = new List<QuestStage>();
        public QuestRewards Rewards = new QuestRewards();

        public int StageIndex(string stageId)
        {
            for (int i = 0; i < Stages.Count; i++) if (Stages[i].Id == stageId) return i;
            return -1;
        }
    }

    // ------------------------------------------------------------------
    // Magic / prayer / thieving / agility
    // ------------------------------------------------------------------
    public enum SpellType { Attack, Heal, Buff, Teleport }

    public sealed class SpellDef
    {
        public string Id;
        public string Name;
        public SpellType Type;
        public string Element;
        public int Level;
        public int MaxHit;
        public int Heal;
        public string Buff;
        public int Amount;
        public int DurationTicks;
        public string Destination;
        public List<ItemAmount> Runes = new List<ItemAmount>();
        public double Xp;
        public int Range;
    }

    public sealed class PrayerDef
    {
        public string Id;
        public string Name;
        public int Level;
        public double DrainPerMinute;
        public Dictionary<string, double> Effect = new Dictionary<string, double>();
    }

    public sealed class ThievingDef
    {
        public string Id;
        public string Name;
        public int Level;
        public double Xp;
        public int StunTicks;
        public int Damage;
        public List<LootEntry> Loot = new List<LootEntry>();
        public List<string> Targets = new List<string>();
        public string Stall;
        public int RespawnTicks;
    }

    public sealed class AgilityDef
    {
        public string Id;
        public string Name;
        public int Level;
        public double Xp;
        public int Ticks;
        public double FailChance;
        public int FailDamage;
        public string Model;
        public bool LapBonus;
    }

    public sealed class StarterDef
    {
        public List<ItemAmount> Items = new List<ItemAmount>();
        public string SpawnPoint;
    }
}
