using System;
using System.Collections.Generic;
using Embervale.Shared.Data;

namespace Embervale.Shared.Simulation
{
    public enum EntityKind : byte
    {
        Player, Monster, Npc, Node, GroundItem, Door, Station, FarmPatch, Fire, Stall, Obstacle
    }

    /// <summary>Animation/pose state broadcast to clients.</summary>
    public enum ActionAnim : byte
    {
        Idle, Walk, Run, AttackMelee, AttackRanged, CastSpell, Mine, Chop, Fish, Smith, Cook, Craft, Eat, Drink,
        Hurt, Death, Pickpocket, Climb, Farm, Pray, Firemaking
    }

    public abstract class Entity
    {
        public int Id { get; internal set; }
        public abstract EntityKind Kind { get; }
        public string DefId;
        public Vec2 Position;
        public float Facing;              // degrees, 0 = north (+Z), clockwise
        public bool Removed { get; internal set; }

        /// <summary>Incremented whenever networked visual state changes (hp, depleted, door open...).</summary>
        public int StateVersion;
        public void MarkStateChanged() => StateVersion++;

        public virtual string DisplayName => DefId;

        /// <summary>Tiles from which this entity can be interacted with (Chebyshev distance).</summary>
        public virtual int InteractRange => 1;

        public void FaceTowards(Vec2 target)
        {
            var d = target - Position;
            if (Math.Abs(d.X) < 1e-4f && Math.Abs(d.Z) < 1e-4f) return;
            Facing = (float)(Math.Atan2(d.X, d.Z) * 180.0 / Math.PI);
        }
    }

    /// <summary>Entities that can fight and move.</summary>
    public abstract class Actor : Entity
    {
        public int Hp;
        public abstract int MaxHp { get; }
        public bool IsDead => Hp <= 0;
        public readonly List<Vec2> Path = new List<Vec2>();
        public bool Running;
        public ActionAnim Anim;
        public int AnimUntilTick;
        public int NextAttackTick;
        public int TargetId;              // current combat target (0 = none)
        public int LastHitByTick;
        public int LastAttackerId;
        public int PoisonDamage;
        public int PoisonNextTick;
        public int StunnedUntil;

        public bool IsMoving => Path.Count > 0;

        public void SetAnim(ActionAnim anim, int currentTick, int durationTicks)
        {
            Anim = anim;
            AnimUntilTick = currentTick + durationTicks;
            MarkStateChanged();
        }
    }

    public sealed class Appearance
    {
        public byte Gender;      // 0 = male, 1 = female
        public byte SkinTone;
        public byte HairStyle;
        public byte HairColor;
        public byte BodyColor;   // base clothing colour (no armour)
        public byte LegColor;

        public Appearance Clone() => (Appearance)MemberwiseClone();

        public void Sanitize()
        {
            Gender = (byte)(Gender % 2);
            SkinTone = (byte)(SkinTone % 6);
            HairStyle = (byte)(HairStyle % 6);
            HairColor = (byte)(HairColor % 8);
            BodyColor = (byte)(BodyColor % 10);
            LegColor = (byte)(LegColor % 10);
        }
    }

    public sealed class Buff
    {
        public Skill Skill;
        public int Amount;
        public int ExpiresTick;
    }

    public sealed class Player : Actor
    {
        public override EntityKind Kind => EntityKind.Player;
        public long CharacterId;
        public long AccountId;
        public string Name;
        public override string DisplayName => Name;
        public Appearance Appearance = new Appearance();
        public SkillSet Skills;
        public ItemContainer Inventory;
        public Equipment Equipment;
        public ItemContainer Bank;
        public QuestLog Quests;
        public FarmingState Farming = new FarmingState();
        public Dictionary<string, string> Settings = new Dictionary<string, string>();
        public MeleeStance Stance = MeleeStance.Accurate;
        public bool AutoRetaliate = true;
        public string AutocastSpell;
        public double PrayerPoints;
        public readonly HashSet<string> ActivePrayers = new HashSet<string>();
        public readonly List<Buff> Buffs = new List<Buff>();
        public double RunEnergy = 100;
        public long PlayTimeSeconds;
        public int LastAgilityObstacle = -1;

        // Session-only state
        public PlayerActivity Activity;
        public PendingInteraction Pending;
        public int DialogueNpcId;
        public string DialogueId;
        public string DialogueNode;
        public List<int> DialogueOptionMap = new List<int>();
        public string OpenShopId;
        public int OpenShopNpcId;
        public int OpenBankEntityId;
        public int OpenMarketNpcId;
        public TradeSession Trade;
        public Party Party;
        public int RespawnAtTick;
        public int ChatWindowStart;
        public int ChatCountInWindow;
        public string LastVisitedLocation;

        // Dirty flags for network sync
        public bool InventoryDirty = true, EquipmentDirty = true, BankDirty = true, SkillsDirty = true, QuestsDirty = true,
                    StatsDirty = true, FarmingDirty = true;

        public override int MaxHp => Skills.MaxHitpoints;
        public bool InInterface => Trade != null || OpenShopId != null || OpenBankEntityId != 0 || DialogueId != null;

        public int CombatLevel => Skills.CombatLevel;

        public void HookContainers()
        {
            Inventory.Changed += _ => InventoryDirty = true;
            Equipment.Changed += _ => { EquipmentDirty = true; StatsDirty = true; MarkStateChanged(); };
            Bank.Changed += _ => BankDirty = true;
            Skills.LevelledUp += _ => { SkillsDirty = true; StatsDirty = true; };
        }

        public int BuffAmount(Skill s)
        {
            int total = 0;
            foreach (var b in Buffs) if (b.Skill == s) total = Math.Max(total, b.Amount);
            return total;
        }

        public int Effective(Skill s) => Skills.Level(s) + BuffAmount(s);
    }

    public enum MonsterState : byte { Idle, Chasing, Returning, Dead }

    public sealed class Monster : Actor
    {
        public override EntityKind Kind => EntityKind.Monster;
        public MonsterDef Def;
        public override string DisplayName => Def.Name;
        public override int MaxHp => Def.Hp;
        public Vec2 SpawnPoint;
        public float SpawnRadius;
        public int SpawnId;
        public MonsterState State;
        public int RespawnAtTick;
        public int NextWanderTick;
        public int NextRepathTick;
        public int AttacksMade;
        public bool SummonUsed;
        public int PendingAbilityTick;
        public AbilityDef PendingAbility;
        public Vec2 PendingAbilityCenter;
        public readonly Dictionary<int, int> DamageBy = new Dictionary<int, int>(); // playerId -> damage (loot owner)
        public bool IsSummon;
        public override int InteractRange => Def.AttackRange;
    }

    public sealed class Npc : Actor
    {
        public override EntityKind Kind => EntityKind.Npc;
        public NpcDef Def;
        public override string DisplayName => Def.Name;
        public override int MaxHp => 10;
        public Vec2 Home;
        public int NextWanderTick;
    }

    public sealed class ResourceNode : Entity
    {
        public override EntityKind Kind => EntityKind.Node;
        public NodeDef Def;
        public override string DisplayName => Def.Name;
        public int DepletedUntilTick;     // 0 = available
        public bool Depleted => DepletedUntilTick > 0;
        public bool Blocks;
        public override int InteractRange => Def.Skill == Skill.Fishing ? 2 : 1;
    }

    public sealed class GroundItem : Entity
    {
        public override EntityKind Kind => EntityKind.GroundItem;
        public ItemStack Stack;
        public int OwnerId;               // player entity id with exclusive visibility (0 = public)
        public long OwnerCharacterId;
        public int PublicAtTick;
        public int DespawnAtTick;
        public override int InteractRange => 1;
    }

    public sealed class Door : Entity
    {
        public override EntityKind Kind => EntityKind.Door;
        public bool Open;
        public int AutoCloseTick;
        public string Side;
        public override string DisplayName => Open ? "Open door" : "Door";
    }

    public sealed class Station : Entity
    {
        public override EntityKind Kind => EntityKind.Station;
        public string StationType;
        public string Model;
        public override string DisplayName => StationType.StartsWith("Altar_") ? "Runic Altar" : StationType;
        public override int InteractRange => 1;
    }

    public sealed class Fire : Entity
    {
        public override EntityKind Kind => EntityKind.Fire;
        public int ExpiresTick;
        public override string DisplayName => "Fire";
    }

    public sealed class FarmPatch : Entity
    {
        public override EntityKind Kind => EntityKind.FarmPatch;
        public override string DisplayName => "Farming patch";
        public override int InteractRange => 2;
    }

    public sealed class Stall : Entity
    {
        public override EntityKind Kind => EntityKind.Stall;
        public ThievingDef Def;
        public int EmptyUntilTick;
        public override string DisplayName => DefId == "gem_stall" ? "Gem stall" : "Bakery stall";
    }

    public sealed class Obstacle : Entity
    {
        public override EntityKind Kind => EntityKind.Obstacle;
        public AgilityDef Def;
        public int Order;
        public Vec2 End;
        public override string DisplayName => Def.Name;
    }
}
