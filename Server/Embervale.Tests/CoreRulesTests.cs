using System.Collections.Generic;
using System.Linq;
using Embervale.Shared;
using Embervale.Shared.Data;
using Xunit;

namespace Embervale.Tests
{
    public class DataTests
    {
        [Fact]
        public void GameData_LoadsAndCrossReferencesAreValid()
        {
            var db = TestUtil.Db;
            Assert.Empty(db.Validate());
            Assert.Equal(20, db.SkillDefs.Count);
            Assert.True(db.Items.Count > 200);
            Assert.True(db.Monsters.Count >= 10);
            Assert.NotNull(db.World);
            Assert.Equal(384, db.World.Width);
        }

        [Fact]
        public void EveryRecipeOutputAndNodeYieldIsARealItem()
        {
            var db = TestUtil.Db;
            foreach (var r in db.Recipes.Values)
                foreach (var o in r.Outputs) Assert.NotNull(db.Item(o.Item));
            foreach (var n in db.Nodes.Values)
                foreach (var y in n.Yields) Assert.NotNull(db.Item(y.Item));
        }

        [Fact]
        public void RequiredMonstersExist()
        {
            var ids = new[] { "giant_rat", "goblin", "wolf", "skeleton", "giant_spider", "zombie", "orc", "troll", "bandit", "forest_spirit" };
            foreach (var id in ids) Assert.True(TestUtil.Db.Monsters.ContainsKey(id), id);
            Assert.Contains(TestUtil.Db.Monsters.Values, m => m.Boss);
        }
    }

    public class ExperienceTests
    {
        private static ExperienceTable Table => TestUtil.Db.XpTable;

        [Fact]
        public void Level1IsZeroXp() => Assert.Equal(0, Table.XpForLevel(1));

        [Fact]
        public void Level2MatchesFormula() => Assert.Equal(100, Table.XpForLevel(2)); // 80*(1.1-1)/0.1 + 20

        [Fact]
        public void ThresholdsStrictlyIncreaseToMax()
        {
            for (int l = 2; l <= 99; l++) Assert.True(Table.XpForLevel(l) > Table.XpForLevel(l - 1), "level " + l);
            Assert.InRange(Table.XpForLevel(99), 5_000_000, 20_000_000);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(99, 1)]
        [InlineData(100, 2)]
        [InlineData(200, 2)]
        public void LevelForXpBoundaries(long xp, int level) => Assert.Equal(level, Table.LevelForXp(xp));

        [Fact]
        public void LevelForXpRoundTripsEveryLevel()
        {
            for (int l = 1; l <= 99; l++)
            {
                Assert.Equal(l, Table.LevelForXp(Table.XpForLevel(l)));
                if (l > 1) Assert.Equal(l - 1, Table.LevelForXp(Table.XpForLevel(l) - 1));
            }
        }

        [Fact]
        public void LevelCapsAt99()
        {
            Assert.Equal(99, Table.LevelForXp(long.MaxValue / 4));
            Assert.Equal(0, Table.XpToNextLevel(Table.XpForLevel(99)));
            Assert.Equal(1f, Table.Progress(Table.XpForLevel(99)));
        }

        [Fact]
        public void ConfigurableFormula()
        {
            var t = new ExperienceTable(new ExperienceConfig { MaxLevel = 10, Base = 10, Growth = 1.0, Linear = 0 });
            Assert.Equal(10, t.XpForLevel(2));
            Assert.Equal(90, t.XpForLevel(10));
        }
    }

    public class SkillTests
    {
        [Fact]
        public void StartingLevelsApplied()
        {
            var s = new SkillSet(TestUtil.Db.XpTable, TestUtil.Db.Experience.StartingLevels);
            Assert.Equal(10, s.Level(Skill.Vitality));
            Assert.Equal(1, s.Level(Skill.Mining));
            Assert.Equal(20, s.MaxHitpoints);
        }

        [Fact]
        public void AddXpRaisesLevelAndFiresEvent()
        {
            var s = new SkillSet(TestUtil.Db.XpTable);
            var ups = new List<LevelUp>();
            s.LevelledUp += ups.Add;
            int gained = s.AddXp(Skill.Mining, 1000);
            Assert.True(gained > 0);
            Assert.Single(ups);
            Assert.Equal(Skill.Mining, ups[0].Skill);
            Assert.Equal(s.Level(Skill.Mining), ups[0].NewLevel);
        }

        [Fact]
        public void FractionalXpAccumulatesExactly()
        {
            var s = new SkillSet(TestUtil.Db.XpTable);
            for (int i = 0; i < 10; i++) s.AddXp(Skill.Cooking, 0.1);
            Assert.Equal(1, s.Xp(Skill.Cooking));
        }

        [Fact]
        public void NegativeOrNaNXpIgnored()
        {
            var s = new SkillSet(TestUtil.Db.XpTable);
            s.AddXp(Skill.Attack, -500);
            s.AddXp(Skill.Attack, double.NaN);
            Assert.Equal(0, s.Xp(Skill.Attack));
        }

        [Fact]
        public void RequirementsChecked()
        {
            var s = new SkillSet(TestUtil.Db.XpTable);
            Assert.False(s.MeetsAll(new[] { new Requirement { Skill = Skill.Attack, Level = 10 } }, out var failed));
            Assert.Equal(10, failed.Level);
            s.AddXp(Skill.Attack, TestUtil.Db.XpTable.XpForLevel(10));
            Assert.True(s.MeetsAll(new[] { new Requirement { Skill = Skill.Attack, Level = 10 } }, out _));
        }

        [Fact]
        public void CombatLevelGrowsWithCombatSkills()
        {
            var s = new SkillSet(TestUtil.Db.XpTable, TestUtil.Db.Experience.StartingLevels);
            int before = s.CombatLevel;
            s.AddXp(Skill.Attack, TestUtil.Db.XpTable.XpForLevel(40));
            s.AddXp(Skill.Strength, TestUtil.Db.XpTable.XpForLevel(40));
            Assert.True(s.CombatLevel > before);
        }
    }

    public class InventoryTests
    {
        private static ItemContainer Inv(int n = 28) => new ItemContainer(TestUtil.Db, n);

        [Fact]
        public void StackableItemsShareOneSlot()
        {
            var inv = Inv();
            Assert.True(inv.Add("coins", 100));
            Assert.True(inv.Add("coins", 50));
            Assert.Equal(150, inv.Count("coins"));
            Assert.Equal(27, inv.FreeSlots);
        }

        [Fact]
        public void NonStackableItemsUseOneSlotEach()
        {
            var inv = Inv();
            Assert.True(inv.Add("bronze_sword", 3));
            Assert.Equal(25, inv.FreeSlots);
            Assert.Equal(3, inv.Count("bronze_sword"));
        }

        [Fact]
        public void AddIsAtomicWhenFull()
        {
            var inv = Inv(3);
            Assert.True(inv.Add("bronze_sword", 2));
            Assert.False(inv.Add("bronze_sword", 2));
            Assert.Equal(2, inv.Count("bronze_sword"));
            Assert.Equal(1, inv.FreeSlots);
        }

        [Fact]
        public void RemoveIsAtomic()
        {
            var inv = Inv();
            inv.Add("copper_ore", 2);
            Assert.False(inv.Remove("copper_ore", 3));
            Assert.Equal(2, inv.Count("copper_ore"));
            Assert.True(inv.Remove("copper_ore", 2));
            Assert.Equal(0, inv.Count("copper_ore"));
        }

        [Fact]
        public void UnknownItemsRejected()
        {
            var inv = Inv();
            Assert.False(inv.Add("not_a_real_item", 1));
            Assert.Equal(28, inv.FreeSlots);
        }

        [Fact]
        public void ZeroOrNegativeQuantitiesRejected()
        {
            var inv = Inv();
            Assert.False(inv.Add("coins", 0));
            Assert.False(inv.Add("coins", -5));
            Assert.Equal(0, inv.Count("coins"));
        }

        [Fact]
        public void StackOverflowPrevented()
        {
            var inv = Inv();
            Assert.True(inv.Add("coins", int.MaxValue - 10));
            Assert.False(inv.Add("coins", 11));
            Assert.Equal(int.MaxValue - 10, inv.Count("coins"));
        }

        [Fact]
        public void SwapMovesItems()
        {
            var inv = Inv();
            inv.Add("bronze_sword", 1);
            Assert.True(inv.Swap(0, 5));
            Assert.True(inv[0].IsEmpty);
            Assert.Equal("bronze_sword", inv[5].ItemId);
            Assert.False(inv.Swap(0, 99));
        }

        [Fact]
        public void BankStacksEverything()
        {
            var bank = new ItemContainer(TestUtil.Db, 10, alwaysStack: true);
            Assert.True(bank.Add("bronze_sword", 5));
            Assert.Equal(9, bank.FreeSlots);
            Assert.Equal(5, bank[0].Quantity);
        }

        [Fact]
        public void CanAddAllAccountsForRemovals()
        {
            var inv = Inv(2);
            inv.Add("copper_ore", 1);
            inv.Add("tin_ore", 1);
            Assert.False(inv.CanAddAll(new[] { new ItemStack("bronze_bar", 1) }));
            Assert.True(inv.CanAddAll(new[] { new ItemStack("bronze_bar", 1) }, new[] { new ItemStack("copper_ore", 1), new ItemStack("tin_ore", 1) }));
            Assert.Equal(1, inv.Count("copper_ore"));   // dry run didn't change anything
        }
    }

    public class EquipmentTests
    {
        private static (ItemContainer inv, Equipment eq, SkillSet skills) Setup()
        {
            var db = TestUtil.Db;
            return (new ItemContainer(db, 28), new Equipment(db), new SkillSet(db.XpTable, db.Experience.StartingLevels));
        }

        [Fact]
        public void EquipMovesItemAndAddsStats()
        {
            var (inv, eq, sk) = Setup();
            inv.Add("bronze_sword", 1);
            Assert.Equal(EquipResult.Ok, eq.Equip(inv, 0, sk, out _));
            Assert.Equal("bronze_sword", eq[EquipSlot.Weapon].ItemId);
            Assert.Equal(0, inv.Count("bronze_sword"));
            Assert.True(eq.TotalStats().AttackMelee > 0);
        }

        [Fact]
        public void LevelRequirementEnforced()
        {
            var (inv, eq, sk) = Setup();
            inv.Add("steel_sword", 1);
            Assert.Equal(EquipResult.RequirementNotMet, eq.Equip(inv, 0, sk, out var failed));
            Assert.Equal(Skill.Attack, failed.Skill);
            Assert.Equal(1, inv.Count("steel_sword"));
        }

        [Fact]
        public void SwappingReturnsOldItem()
        {
            var (inv, eq, sk) = Setup();
            inv.Add("bronze_sword", 1);
            inv.Add("bronze_dagger", 1);
            eq.Equip(inv, 0, sk, out _);
            eq.Equip(inv, 1, sk, out _);
            Assert.Equal("bronze_dagger", eq[EquipSlot.Weapon].ItemId);
            Assert.Equal(1, inv.Count("bronze_sword"));
        }

        [Fact]
        public void TwoHandedWeaponRemovesShield()
        {
            var (inv, eq, sk) = Setup();
            sk.AddXp(Skill.Attack, TestUtil.Db.XpTable.XpForLevel(10));
            inv.Add("bronze_shield", 1);
            inv.Add("bronze_greatsword", 1);
            Assert.Equal(EquipResult.Ok, eq.Equip(inv, 0, sk, out _));
            Assert.Equal(EquipResult.Ok, eq.Equip(inv, 1, sk, out _));
            Assert.True(eq[EquipSlot.Shield].IsEmpty);
            Assert.Equal(1, inv.Count("bronze_shield"));
        }

        [Fact]
        public void CannotEquipWhenNoRoomForReturnedItems()
        {
            var (inv, eq, sk) = Setup();
            var small = new ItemContainer(TestUtil.Db, 1);
            small.Add("bronze_shield", 1);
            eq.Equip(small, 0, sk, out _);
            small.Add("bronze_sword", 1);
            eq.Equip(small, 0, sk, out _);
            // weapon + shield worn, inventory now empty but only 1 slot; equipping a 2h needs to return shield AND sword
            small.Add("bronze_greatsword", 1);
            Assert.Equal(EquipResult.InventoryFull, eq.Equip(small, 0, sk, out _));
            Assert.Equal("bronze_sword", eq[EquipSlot.Weapon].ItemId);
        }

        [Fact]
        public void UnequipRequiresSpace()
        {
            var (_, eq, sk) = Setup();
            var one = new ItemContainer(TestUtil.Db, 1);
            one.Add("bronze_helm", 1);
            eq.Equip(one, 0, sk, out _);
            one.Add("copper_ore", 1);
            Assert.Equal(EquipResult.InventoryFull, eq.Unequip(EquipSlot.Head, one));
            Assert.Equal("bronze_helm", eq[EquipSlot.Head].ItemId);
        }

        [Fact]
        public void AmmoStacks()
        {
            var (inv, eq, sk) = Setup();
            inv.Add("bronze_arrow", 30);
            eq.Equip(inv, 0, sk, out _);
            inv.Add("bronze_arrow", 20);
            eq.Equip(inv, 0, sk, out _);
            Assert.Equal(50, eq[EquipSlot.Ammo].Quantity);
            Assert.True(eq.ConsumeAmmo());
            Assert.Equal(49, eq[EquipSlot.Ammo].Quantity);
        }
    }

    public class CombatFormulaTests
    {
        [Fact]
        public void HitChanceClampedAndMonotonic()
        {
            double prev = 0;
            foreach (var atk in new long[] { 1, 100, 1000, 5000, 20000, 1000000 })
            {
                double p = CombatFormulas.HitChance(atk, 1000);
                Assert.InRange(p, 0.05, 0.95);
                Assert.True(p >= prev);
                prev = p;
            }
        }

        [Fact]
        public void MaxHitScalesWithStrengthAndGear()
        {
            var weak = new AttackProfile { Style = CombatStyle.Melee, PowerLevel = 1, PowerBonus = 0, Multiplier = 1 };
            var strong = new AttackProfile { Style = CombatStyle.Melee, PowerLevel = 60, PowerBonus = 50, Multiplier = 1 };
            Assert.True(CombatFormulas.MaxHit(strong) > CombatFormulas.MaxHit(weak));
            Assert.True(CombatFormulas.MaxHit(weak) >= 1);
        }

        [Fact]
        public void RollsNeverExceedCriticalMax()
        {
            var rng = new XorShiftRandom(7);
            var atk = new AttackProfile { Style = CombatStyle.Melee, AccuracyLevel = 50, PowerLevel = 50, AccuracyBonus = 20, PowerBonus = 20, Multiplier = 1 };
            var def = new DefenseProfile { DefenseLevel = 10, DefenseBonus = 0 };
            int max = CombatFormulas.MaxHit(atk);
            int hits = 0;
            for (int i = 0; i < 5000; i++)
            {
                var r = CombatFormulas.Roll(rng, atk, def);
                Assert.InRange(r.Damage, 0, (int)System.Math.Ceiling(max * CombatFormulas.CritMultiplier));
                if (r.Hit) hits++;
            }
            Assert.InRange(hits, 3500, 4900);   // strong attacker vs weak defender usually hits
        }

        [Fact]
        public void DamageReductionApplies()
        {
            var rng = new XorShiftRandom(9);
            var atk = new AttackProfile { Style = CombatStyle.Melee, AccuracyLevel = 99, PowerLevel = 99, AccuracyBonus = 200, PowerBonus = 100, Multiplier = 1 };
            long total = 0, reduced = 0;
            for (int i = 0; i < 2000; i++) total += CombatFormulas.Roll(rng, atk, new DefenseProfile { DefenseLevel = 1 }).Damage;
            rng = new XorShiftRandom(9);
            for (int i = 0; i < 2000; i++) reduced += CombatFormulas.Roll(rng, atk, new DefenseProfile { DefenseLevel = 1, DamageReduction = 0.5 }).Damage;
            Assert.True(reduced < total * 0.6);
        }

        [Fact]
        public void MagicMaxHitUsesSpell()
        {
            var atk = new AttackProfile { Style = CombatStyle.Magic, PowerLevel = 1, SpellMaxHit = 10, Multiplier = 1 };
            Assert.Equal(10, CombatFormulas.MaxHit(atk));
        }
    }

    public class LootTests
    {
        [Fact]
        public void AlwaysDropsAlwaysAppear()
        {
            var rng = new XorShiftRandom(1);
            var table = TestUtil.Db.Loot["wolf"];
            for (int i = 0; i < 200; i++)
            {
                var drops = LootRoller.Roll(table, rng);
                Assert.Contains(drops, d => d.ItemId == "big_bones");
                Assert.Contains(drops, d => d.ItemId == "wolf_pelt");
            }
        }

        [Fact]
        public void QuantitiesWithinRangeAndDistributionReasonable()
        {
            var rng = new XorShiftRandom(2);
            var table = TestUtil.Db.Loot["goblin"];
            int coinDrops = 0;
            for (int i = 0; i < 4000; i++)
                foreach (var d in LootRoller.Roll(table, rng))
                {
                    Assert.True(d.Quantity > 0);
                    if (d.ItemId == "coins") { coinDrops++; Assert.InRange(d.Quantity, 3, 15); }
                }
            // weight 5 of total 25 => ~20%
            Assert.InRange(coinDrops, 600, 1000);
        }

        [Fact]
        public void WeightedNothingCanBePicked()
        {
            var rng = new XorShiftRandom(3);
            var entries = new List<LootEntry> { new LootEntry { Item = "coins", Weight = 1 } };
            int nothing = 0;
            for (int i = 0; i < 1000; i++) if (LootRoller.Weighted(entries, 99, rng) == null) nothing++;
            Assert.InRange(nothing, 970, 1000);
        }
    }

    public class PathfindingTests
    {
        [Fact]
        public void FindsPathAroundWall()
        {
            var blocked = new byte[20 * 20];
            for (int z = 0; z < 15; z++) blocked[z * 20 + 10] = 1;
            var g = new Embervale.Shared.World.WorldGrid(20, 20, blocked);
            var pf = new Embervale.Shared.World.Pathfinder(g);
            var path = pf.FindPath(5, 5, 15, 5);
            Assert.NotNull(path);
            Assert.Equal(new Vec2(15.5f, 5.5f), path.Last());
            foreach (var p in path) Assert.True(g.IsWalkable(p.TileX, p.TileZ));
            Assert.Contains(path, p => p.TileZ >= 15);   // went around the end of the wall
        }

        [Fact]
        public void NoCornerCutting()
        {
            var blocked = new byte[5 * 5];
            blocked[1 * 5 + 2] = 1;   // (2,1)
            blocked[2 * 5 + 1] = 1;   // (1,2)
            var g = new Embervale.Shared.World.WorldGrid(5, 5, blocked);
            Assert.False(g.CanStep(1, 1, 2, 2));
        }

        [Fact]
        public void UnreachableReturnsNull()
        {
            var blocked = new byte[10 * 10];
            for (int i = 0; i < 10; i++) blocked[5 * 10 + i] = 1;
            var g = new Embervale.Shared.World.WorldGrid(10, 10, blocked);
            Assert.Null(new Embervale.Shared.World.Pathfinder(g).FindPath(1, 1, 1, 8));
        }

        [Fact]
        public void RangeStopsShort()
        {
            var g = new Embervale.Shared.World.WorldGrid(30, 30);
            var path = new Embervale.Shared.World.Pathfinder(g).FindPath(0, 0, 20, 0, range: 5);
            Assert.Equal(15, path.Last().TileX);
        }

        [Fact]
        public void RealWorldVillageToCapitalIsReachable()
        {
            var w = TestUtil.NewWorld();
            var a = w.Location("brookhollow_square");
            var b = w.Location("highmarch_plaza");
            var path = w.PlanPath(a, b.TileX, b.TileZ, 0, false, 256);
            Assert.NotNull(path);
            Assert.True(path.Count > 100);
        }
    }
}
