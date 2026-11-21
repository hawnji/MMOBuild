using System.Linq;
using Embervale.Shared;
using Embervale.Shared.Data;
using Embervale.Shared.Persistence;
using Embervale.Shared.Simulation;
using Xunit;

namespace Embervale.Tests
{
    public class GatheringTests
    {
        [Fact]
        public void MiningCopperGivesOreXpAndDepletesRock()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var rock = TestUtil.Nearest<ResourceNode>(w, p.Position, n => n.DefId == "copper_rock");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, rock));
            w.RequestInteract(p, rock.Id, "Mine");
            Assert.True(TestUtil.RunUntil(w, () => p.Inventory.Count("copper_ore") > 0, 300));
            Assert.True(p.Skills.Xp(Skill.Mining) >= 17);
            Assert.True(rock.Depleted);
            Assert.Null(p.Activity);
            // respawns
            Assert.True(TestUtil.RunUntil(w, () => !rock.Depleted, 50));
        }

        [Fact]
        public void GatheringRequiresTool()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Remove("bronze_pickaxe", 1);
            var rock = TestUtil.Nearest<ResourceNode>(w, p.Position, n => n.DefId == "copper_rock");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, rock));
            w.RequestInteract(p, rock.Id, "Mine");
            TestUtil.Run(w, 40);
            Assert.Equal(0, p.Inventory.Count("copper_ore"));
            Assert.Contains(w.Events, e => e.Type == WorldEventType.GameMessage && e.Text.Contains("pickaxe"));
        }

        [Fact]
        public void GatheringRequiresLevel()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var rock = TestUtil.Nearest<ResourceNode>(w, p.Position, n => n.DefId == "iron_rock");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, rock));
            w.RequestInteract(p, rock.Id, "Mine");
            TestUtil.Run(w, 40);
            Assert.Equal(0, p.Inventory.Count("iron_ore"));
            Assert.Contains(w.Events, e => e.Type == WorldEventType.GameMessage && e.Text.Contains("level 15 Mining"));
        }

        [Fact]
        public void FullInventoryStopsGathering()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            while (p.Inventory.FreeSlots > 0) p.Inventory.Add("bones", 1);
            var tree = TestUtil.Nearest<ResourceNode>(w, p.Position, n => n.DefId == "oak_tree");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, tree));
            w.RequestInteract(p, tree.Id, "Chop");
            TestUtil.Run(w, 20);
            Assert.Null(p.Activity);
            Assert.Equal(0, p.Inventory.Count("oak_logs"));
        }

        [Fact]
        public void TwoPlayersCannotBothTakeTheLastOre()
        {
            var w = TestUtil.NewWorld(5);
            var a = TestUtil.AddPlayer(w, "Alpha");
            var b = TestUtil.AddPlayer(w, "Bravo");
            var rock = TestUtil.Nearest<ResourceNode>(w, a.Position, n => n.DefId == "tin_rock");
            var spot = TestUtil.StandNextTo(w, rock);
            TestUtil.Teleport(w, a, spot);
            TestUtil.Teleport(w, b, spot);
            w.RequestInteract(a, rock.Id, "Mine");
            w.RequestInteract(b, rock.Id, "Mine");
            Assert.True(TestUtil.RunUntil(w, () => rock.Depleted, 400));
            int total = a.Inventory.Count("tin_ore") + b.Inventory.Count("tin_ore");
            Assert.Equal(1, total);   // a copper/tin rock yields exactly one ore before depleting
            TestUtil.Run(w, 2);
            Assert.False(a.Activity is GatherActivity ga && ga.Node == rock);
            Assert.False(b.Activity is GatherActivity gb && gb.Node == rock);
        }

        [Fact]
        public void PlayerWalksToNodeBeforeGathering()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var tree = TestUtil.Nearest<ResourceNode>(w, p.Position, n => n.DefId == "oak_tree");
            Assert.True(Vec2.TileDistance(p.Position, tree.Position) > 1);
            w.RequestInteract(p, tree.Id, "Chop");
            Assert.True(TestUtil.RunUntil(w, () => p.Activity is GatherActivity, 200));
            Assert.True(Vec2.TileDistance(p.Position, tree.Position) <= 1);
        }

        [Fact]
        public void FishingConsumesBaitAndNeedsRod()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Skills.AddXp(Skill.Fishing, w.Db.XpTable.XpForLevel(10));
            p.Inventory.Add("fishing_rod", 1);
            p.Inventory.Add("bait_worms", 20);
            var spot = TestUtil.Nearest<ResourceNode>(w, p.Position, n => n.DefId == "rod_spot");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, spot));
            w.RequestInteract(p, spot.Id, "Fish");
            Assert.True(TestUtil.RunUntil(w, () => p.Inventory.Count("raw_perch") > 0, 600));
            Assert.Equal(20 - p.Inventory.Count("raw_perch"), p.Inventory.Count("bait_worms"));
        }
    }

    public class CraftingTests
    {
        private static (GameWorld w, Player p, Station furnace, Station anvil) AtSmithy()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var furnace = TestUtil.Nearest<Station>(w, p.Position, s => s.StationType == "Furnace");
            var anvil = TestUtil.Nearest<Station>(w, furnace.Position, s => s.StationType == "Anvil");
            return (w, p, furnace, anvil);
        }

        [Fact]
        public void SmeltAndForgeBronzeDagger()
        {
            var (w, p, furnace, anvil) = AtSmithy();
            p.Inventory.Add("copper_ore", 1);
            p.Inventory.Add("tin_ore", 1);
            p.Inventory.Add("hammer", 1);
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, furnace));
            w.RequestCraft(p, "smelt_bronze", 1, furnace.Id);
            Assert.True(TestUtil.RunUntil(w, () => p.Inventory.Count("bronze_bar") == 1, 20));
            Assert.Equal(0, p.Inventory.Count("copper_ore"));
            Assert.Equal(0, p.Inventory.Count("tin_ore"));
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, anvil));
            int daggersBefore = p.Inventory.Count("bronze_dagger");
            w.RequestCraft(p, "forge_bronze_dagger", 1, anvil.Id);
            Assert.True(TestUtil.RunUntil(w, () => p.Inventory.Count("bronze_dagger") == daggersBefore + 1, 20));
            Assert.Equal(0, p.Inventory.Count("bronze_bar"));
            Assert.True(p.Skills.Xp(Skill.Smithing) > 0);
        }

        [Fact]
        public void CraftingRequiresStationProximity()
        {
            var (w, p, furnace, _) = AtSmithy();
            p.Inventory.Add("copper_ore", 1);
            p.Inventory.Add("tin_ore", 1);
            TestUtil.Teleport(w, p, w.Location("brookhollow_square"));
            w.RequestCraft(p, "smelt_bronze", 1, furnace.Id);
            TestUtil.Run(w, 10);
            Assert.Equal(0, p.Inventory.Count("bronze_bar"));
            Assert.Equal(1, p.Inventory.Count("copper_ore"));
        }

        [Fact]
        public void CraftingRequiresLevelAndIngredients()
        {
            var (w, p, furnace, _) = AtSmithy();
            p.Inventory.Add("iron_ore", 1);
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, furnace));
            w.RequestCraft(p, "smelt_iron", 1, furnace.Id);
            TestUtil.Run(w, 10);
            Assert.Equal(0, p.Inventory.Count("iron_bar"));
            w.RequestCraft(p, "smelt_bronze", 1, furnace.Id);   // has no copper/tin
            TestUtil.Run(w, 10);
            Assert.Equal(0, p.Inventory.Count("bronze_bar"));
            Assert.Equal(1, p.Inventory.Count("iron_ore"));
        }

        [Fact]
        public void MakeMultipleStopsWhenOutOfMaterials()
        {
            var (w, p, furnace, _) = AtSmithy();
            p.Inventory.Add("copper_ore", 3);
            p.Inventory.Add("tin_ore", 3);
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, furnace));
            w.RequestCraft(p, "smelt_bronze", 10, furnace.Id);
            TestUtil.Run(w, 60);
            Assert.Equal(3, p.Inventory.Count("bronze_bar"));
            Assert.Null(p.Activity);
        }

        [Fact]
        public void FletchingWithKnifeOnLogs()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("knife", 1);
            p.Inventory.Add("oak_logs", 1);
            int knife = p.Inventory.IndexOf("knife"), logs = p.Inventory.IndexOf("oak_logs");
            w.RequestUseItemOnItem(p, knife, logs);
            // several recipes use knife+oak logs, so a menu is offered
            Assert.Contains(w.Events, e => e.Type == WorldEventType.CraftMenu);
            w.RequestCraft(p, "fletch_oak_shortbow", 1, 0);
            TestUtil.Run(w, 6);
            Assert.Equal(1, p.Inventory.Count("oak_shortbow"));
            Assert.Equal(1, p.Inventory.Count("knife"));    // tools are not consumed
        }

        [Fact]
        public void FiremakingSpawnsFireUsableForCooking()
        {
            var w = TestUtil.NewWorld(11);
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("oak_logs", 1);
            p.Inventory.Add("raw_meat", 1);
            TestUtil.Teleport(w, p, new Vec2(205.5f, 120.5f));
            w.RequestCraft(p, "burn_oak_logs", 1, 0);
            TestUtil.Run(w, 6);
            var fire = TestUtil.Nearest<Fire>(w, p.Position);
            Assert.NotNull(fire);
            Assert.True(p.Skills.Xp(Skill.Firemaking) > 0);
            w.RequestUseItemOnEntity(p, p.Inventory.IndexOf("raw_meat"), fire.Id);
            TestUtil.Run(w, 12);
            Assert.Equal(0, p.Inventory.Count("raw_meat"));
            Assert.Equal(1, p.Inventory.Count("cooked_meat") + p.Inventory.Count("burnt_meat"));
        }

        [Fact]
        public void BurnChanceFallsToZeroAtNoFailLevel()
        {
            var r = TestUtil.Db.Recipes["cook_trout"];
            Assert.True(GatheringFormulas.FailChance(r, r.Level) > 0);
            Assert.Equal(0, GatheringFormulas.FailChance(r, r.NoFailLevel));
            Assert.True(GatheringFormulas.FailChance(r, r.Level + 10) < GatheringFormulas.FailChance(r, r.Level));
        }
    }

    public class CombatSimTests
    {
        [Fact]
        public void PlayerKillsRatGetsXpAndLoot()
        {
            var w = TestUtil.NewWorld(3);
            var p = TestUtil.AddPlayer(w);
            p.Skills.AddXp(Skill.Attack, w.Db.XpTable.XpForLevel(20));
            p.Skills.AddXp(Skill.Strength, w.Db.XpTable.XpForLevel(20));
            p.Skills.AddXp(Skill.Defense, w.Db.XpTable.XpForLevel(20));
            p.Hp = p.MaxHp;
            var rat = TestUtil.Nearest<Monster>(w, p.Position, m => m.DefId == "giant_rat");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, rat));
            long atkBefore = p.Skills.Xp(Skill.Attack);
            w.RequestInteract(p, rat.Id, "Attack");
            Assert.True(TestUtil.RunUntil(w, () => rat.State == MonsterState.Dead, 400));
            Assert.True(p.Skills.Xp(Skill.Attack) > atkBefore);
            Assert.True(p.Skills.Xp(Skill.Vitality) > w.Db.XpTable.XpForLevel(10));
            var bones = TestUtil.Nearest<GroundItem>(w, rat.Position, g => g.Stack.ItemId == "bones");
            Assert.NotNull(bones);
            Assert.Equal(p.Id, bones.OwnerId);
            // monster respawns with full health
            Assert.True(TestUtil.RunUntil(w, () => rat.State != MonsterState.Dead, 100));
            Assert.Equal(rat.MaxHp, rat.Hp);
        }

        [Fact]
        public void LootIsPrivateThenPublic()
        {
            var w = TestUtil.NewWorld();
            var a = TestUtil.AddPlayer(w, "Owner");
            var b = TestUtil.AddPlayer(w, "Other");
            var g = w.DropToGround(new ItemStack("bones", 1), a.Position, a, publicAfterTicks: 5);
            Assert.True(w.CanSee(a, g));
            Assert.False(w.CanSee(b, g));
            TestUtil.Run(w, 6);
            Assert.True(w.CanSee(b, g));
        }

        [Fact]
        public void AggressiveMonsterAttacksLowLevelPlayer()
        {
            var w = TestUtil.NewWorld(8);
            var p = TestUtil.AddPlayer(w);
            var wolf = TestUtil.Nearest<Monster>(w, new Vec2(50, 160), m => m.DefId == "wolf");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, wolf));
            Assert.True(TestUtil.RunUntil(w, () => wolf.TargetId == p.Id, 20));
            Assert.True(TestUtil.RunUntil(w, () => p.Hp < p.MaxHp, 60));
        }

        [Fact]
        public void PlayerDeathDropsInventoryAndRespawnsAtVillage()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var troll = TestUtil.Nearest<Monster>(w, new Vec2(330, 330), m => m.DefId == "troll");
            int coins = p.Inventory.Count("coins");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, troll));
            w.DamageActor(p, p.Hp, troll, false, false);
            Assert.True(p.IsDead);
            Assert.Equal(0, p.Inventory.Count("coins"));
            Assert.Contains(w.GroundItems, g => g.Stack.ItemId == "coins" && g.Stack.Quantity == coins && g.OwnerId == p.Id);
            Assert.True(TestUtil.RunUntil(w, () => !p.IsDead, 20));
            Assert.True(Vec2.Distance(p.Position, w.Location("brookhollow_square")) < 1);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void EatingHealsButNotAboveMax()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Hp = 5;
            int slot = p.Inventory.IndexOf("bread");
            w.RequestItemOption(p, slot, "Eat");
            Assert.Equal(10, p.Hp);
            p.Hp = p.MaxHp;
            TestUtil.Run(w, 3);
            w.RequestItemOption(p, p.Inventory.IndexOf("bread"), "Eat");
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void RangedCombatConsumesAmmoAndNeedsArrows()
        {
            var w = TestUtil.NewWorld(4);
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("oak_shortbow", 1);
            w.RequestEquip(p, p.Inventory.IndexOf("oak_shortbow"));
            var rat = TestUtil.Nearest<Monster>(w, p.Position, m => m.DefId == "giant_rat");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, rat));
            w.RequestInteract(p, rat.Id, "Attack");
            TestUtil.Run(w, 5);
            Assert.Equal(rat.MaxHp, rat.Hp);   // no arrows: can't attack
            p.Inventory.Add("bronze_arrow", 50);
            w.RequestEquip(p, p.Inventory.IndexOf("bronze_arrow"));
            w.RequestInteract(p, rat.Id, "Attack");
            TestUtil.Run(w, 30);
            Assert.True(p.Equipment[EquipSlot.Ammo].Quantity < 50);
            Assert.True(p.Skills.Xp(Skill.Ranged) > 0 || rat.Hp == rat.MaxHp);
        }

        [Fact]
        public void MagicCombatConsumesRunes()
        {
            var w = TestUtil.NewWorld(6);
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("air_rune", 10);
            p.Inventory.Add("mind_rune", 10);
            var rat = TestUtil.Nearest<Monster>(w, p.Position, m => m.DefId == "giant_rat");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, rat));
            w.RequestCastSpell(p, "gale_bolt", rat.Id);
            TestUtil.Run(w, 3);
            Assert.Equal(9, p.Inventory.Count("air_rune"));
            Assert.Equal(9, p.Inventory.Count("mind_rune"));
            Assert.True(p.Skills.Xp(Skill.Magic) >= 6);
        }

        [Fact]
        public void BossTelegraphsAbility()
        {
            var w = TestUtil.NewWorld(12);
            var p = TestUtil.AddPlayer(w);
            foreach (var s in new[] { Skill.Attack, Skill.Strength, Skill.Defense, Skill.Vitality })
                p.Skills.AddXp(s, w.Db.XpTable.XpForLevel(90));
            p.Hp = p.MaxHp;
            var boss = TestUtil.Nearest<Monster>(w, p.Position, m => m.DefId == "ruin_warden");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, boss));
            bool telegraphed = TestUtil.RunUntil(w, () => w.Events.Any(e => e.Type == WorldEventType.Telegraph), 120);
            Assert.True(telegraphed);
        }
    }

    public class QuestTests
    {
        [Fact]
        public void FirstForgeQuestCompletesEndToEnd()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var smith = TestUtil.Nearest<Npc>(w, p.Position, n => n.DefId == "hollin_smith");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, smith));
            w.RequestInteract(p, smith.Id, "Talk-to");
            Assert.NotNull(p.DialogueId);
            var opts = w.DialogueOptions(p, out _, out _);
            w.RequestDialogueOption(p, opts.IndexOf("Teach me to smith."));
            opts = w.DialogueOptions(p, out _, out _);
            w.RequestDialogueOption(p, 0);
            Assert.True(p.Quests.IsStarted("first_forge"));

            // make the dagger
            p.Inventory.Add("bronze_bar", 1);
            p.Inventory.Add("hammer", 1);
            var anvil = TestUtil.Nearest<Station>(w, p.Position, s => s.StationType == "Anvil");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, anvil));
            w.RequestCraft(p, "forge_bronze_dagger", 1, anvil.Id);
            TestUtil.Run(w, 8);
            Assert.Equal("return", p.Quests.CurrentStageId("first_forge"));

            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, smith));
            w.RequestInteract(p, smith.Id, "Talk-to");
            opts = w.DialogueOptions(p, out _, out _);
            w.RequestDialogueOption(p, opts.IndexOf("I've made the dagger."));
            opts = w.DialogueOptions(p, out _, out _);
            w.RequestDialogueOption(p, 0);
            Assert.True(p.Quests.IsComplete("first_forge"));
            Assert.True(p.Skills.Xp(Skill.Smithing) >= 250);
            Assert.Equal(1, p.Quests.QuestPoints);
        }

        [Fact]
        public void QuestRequirementsBlockStart()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            Assert.False(p.Quests.CanStart("hollow_warden", p.Skills, out var reason));
            Assert.Contains("Attack", reason);
        }

        [Fact]
        public void KillAndCollectObjectivesConsumeItemsOnAdvance()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            w.StartQuest(p, "goblin_trouble");
            for (int i = 0; i < 5; i++) p.Quests.Record(ObjectiveType.Kill, "goblin", 1);
            w.EvaluateQuests(p);
            Assert.Equal("hunt", p.Quests.CurrentStageId("goblin_trouble"));   // charms still missing
            p.Inventory.Add("goblin_charm", 3);
            w.EvaluateQuests(p);
            Assert.Equal("return", p.Quests.CurrentStageId("goblin_trouble"));
            Assert.Equal(0, p.Inventory.Count("goblin_charm"));
        }
    }

    public class EconomyTests
    {
        private static (GameWorld w, Player p, Npc banker) AtBank()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var banker = TestUtil.Nearest<Npc>(w, p.Position, n => n.DefId == "banker");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, banker));
            w.RequestInteract(p, banker.Id, "Bank");
            return (w, p, banker);
        }

        [Fact]
        public void BankDepositAndWithdraw()
        {
            var (w, p, _) = AtBank();
            Assert.NotEqual(0, p.OpenBankEntityId);
            int coins = p.Inventory.Count("coins");
            w.RequestBankDeposit(p, p.Inventory.IndexOf("coins"), coins);
            Assert.Equal(0, p.Inventory.Count("coins"));
            Assert.Equal(coins, p.Bank.Count("coins"));
            w.RequestBankWithdraw(p, p.Bank.IndexOf("coins"), 10);
            Assert.Equal(10, p.Inventory.Count("coins"));
            Assert.Equal(coins - 10, p.Bank.Count("coins"));
        }

        [Fact]
        public void BankRejectsWhenNotOpenOrFarAway()
        {
            var (w, p, _) = AtBank();
            TestUtil.Teleport(w, p, w.Location("highmarch_plaza"));
            int coins = p.Inventory.Count("coins");
            w.RequestBankDeposit(p, p.Inventory.IndexOf("coins"), coins);
            Assert.Equal(coins, p.Inventory.Count("coins"));
        }

        [Fact]
        public void WithdrawNonStackablesLimitedByInventorySpace()
        {
            var (w, p, _) = AtBank();
            p.Bank.Add("bronze_sword", 50);
            int free = p.Inventory.FreeSlots;
            w.RequestBankWithdraw(p, p.Bank.IndexOf("bronze_sword"), 50);
            Assert.Equal(free, p.Inventory.Count("bronze_sword"));
            Assert.Equal(50 - free, p.Bank.Count("bronze_sword"));
        }

        [Fact]
        public void ShopBuyAndSellChangeCoinsAndStock()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("coins", 1000);
            var tilda = TestUtil.Nearest<Npc>(w, p.Position, n => n.DefId == "tilda_merchant");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, tilda));
            w.RequestInteract(p, tilda.Id, "Trade");
            Assert.Equal("brookhollow_general", p.OpenShopId);
            var shop = w.Shops["brookhollow_general"];
            int stock = shop.StockOf("knife");
            int coins = p.Inventory.Count("coins");
            w.RequestShopBuy(p, "knife", 1);
            Assert.Equal(1, p.Inventory.Count("knife"));
            Assert.Equal(stock - 1, shop.StockOf("knife"));
            Assert.True(p.Inventory.Count("coins") < coins);
            coins = p.Inventory.Count("coins");
            w.RequestShopSell(p, p.Inventory.IndexOf("knife"), 1);
            Assert.Equal(0, p.Inventory.Count("knife"));
            Assert.True(p.Inventory.Count("coins") >= coins);
        }

        [Fact]
        public void CannotBuyWithoutMoney()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Remove("coins", p.Inventory.Count("coins"));
            var tilda = TestUtil.Nearest<Npc>(w, p.Position, n => n.DefId == "tilda_merchant");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, tilda));
            w.RequestInteract(p, tilda.Id, "Trade");
            w.RequestShopBuy(p, "knife", 1);
            Assert.Equal(0, p.Inventory.Count("knife"));
        }

        [Fact]
        public void CoinsCannotBeSoldToShops()
        {
            var shop = new ShopState(TestUtil.Db.Shops["brookhollow_general"]);
            Assert.False(shop.WillBuy(TestUtil.Db.Item("coins")));
            Assert.False(shop.WillBuy(TestUtil.Db.Item("old_map_scrap")));   // untradeable
        }
    }

    public class TradeTests
    {
        private static (GameWorld w, Player a, Player b) Pair()
        {
            var w = TestUtil.NewWorld();
            var a = TestUtil.AddPlayer(w, "Alice");
            var b = TestUtil.AddPlayer(w, "Bob");
            w.RequestTrade(a, b);
            w.RequestTrade(b, a);
            return (w, a, b);
        }

        [Fact]
        public void SuccessfulTradeSwapsItemsAtomically()
        {
            var (w, a, b) = Pair();
            Assert.NotNull(a.Trade);
            Assert.Same(a.Trade, b.Trade);
            a.Inventory.Add("copper_ore", 5);
            w.RequestTradeOffer(a, a.Inventory.IndexOf("copper_ore"), 5);
            w.RequestTradeOffer(b, b.Inventory.IndexOf("coins"), 20);
            Assert.Equal(0, a.Inventory.Count("copper_ore"));   // escrowed
            w.RequestTradeAccept(a); w.RequestTradeAccept(b);    // offer screen
            Assert.Equal(TradeStage.Confirm, a.Trade.Stage);
            w.RequestTradeAccept(a); w.RequestTradeAccept(b);    // confirm screen
            Assert.Null(a.Trade);
            Assert.Equal(5, b.Inventory.Count("copper_ore"));
            Assert.Equal(45, a.Inventory.Count("coins"));
            Assert.Equal(5, b.Inventory.Count("coins"));
        }

        [Fact]
        public void ChangingOfferResetsAcceptance()
        {
            var (w, a, b) = Pair();
            w.RequestTradeOffer(a, a.Inventory.IndexOf("coins"), 10);
            w.RequestTradeAccept(a);
            Assert.True(a.Trade.AcceptedA);
            w.RequestTradeOffer(b, b.Inventory.IndexOf("bread"), 1);
            Assert.False(a.Trade.AcceptedA);
            Assert.False(a.Trade.AcceptedB);
        }

        [Fact]
        public void CancelReturnsEscrow()
        {
            var (w, a, b) = Pair();
            int coins = a.Inventory.Count("coins");
            w.RequestTradeOffer(a, a.Inventory.IndexOf("coins"), coins);
            Assert.Equal(0, a.Inventory.Count("coins"));
            w.RequestCloseInterface(b);
            Assert.Null(a.Trade);
            Assert.Equal(coins, a.Inventory.Count("coins"));
        }

        [Fact]
        public void TradeFailsCleanlyWhenReceiverLacksSpace()
        {
            var (w, a, b) = Pair();
            while (b.Inventory.FreeSlots > 0) b.Inventory.Add("bones", 1);
            a.Inventory.Add("bronze_sword", 1);
            w.RequestTradeOffer(a, a.Inventory.IndexOf("bronze_sword"), 1);
            for (int i = 0; i < 2; i++) { w.RequestTradeAccept(a); w.RequestTradeAccept(b); }
            Assert.NotNull(a.Trade);                       // still open
            Assert.Equal(0, b.Inventory.Count("bronze_sword"));
            w.RequestCloseInterface(a);
            Assert.Equal(1, a.Inventory.Count("bronze_sword"));
        }

        [Fact]
        public void UntradeableItemsCannotBeOffered()
        {
            var (w, a, _) = Pair();
            a.Inventory.Add("old_map_scrap", 1);
            w.RequestTradeOffer(a, a.Inventory.IndexOf("old_map_scrap"), 1);
            Assert.Equal(1, a.Inventory.Count("old_map_scrap"));
        }

        [Fact]
        public void ItemsInEscrowArePersistedWithOwner()
        {
            var (w, a, _) = Pair();
            int coins = a.Inventory.Count("coins");
            w.RequestTradeOffer(a, a.Inventory.IndexOf("coins"), coins);
            var data = CharacterMapper.Capture(a);
            Assert.Contains(data.Inventory, s => s.Item == "coins" && s.Qty == coins);
        }

        [Fact]
        public void NoDuplicationAcrossManyRandomTrades()
        {
            var w = TestUtil.NewWorld();
            var a = TestUtil.AddPlayer(w, "Ann");
            var b = TestUtil.AddPlayer(w, "Ben");
            a.Inventory.Add("coins", 1000);
            b.Inventory.Add("copper_ore", 10);
            long totalCoins = a.Inventory.Count("coins") + b.Inventory.Count("coins");
            int totalOre = a.Inventory.Count("copper_ore") + b.Inventory.Count("copper_ore");
            var rng = new XorShiftRandom(99);
            for (int round = 0; round < 200; round++)
            {
                w.RequestTrade(a, b); w.RequestTrade(b, a);
                if (a.Inventory.Contains("coins")) w.RequestTradeOffer(a, a.Inventory.IndexOf("coins"), rng.Next(1, 50));
                if (b.Inventory.Contains("copper_ore")) w.RequestTradeOffer(b, b.Inventory.IndexOf("copper_ore"), 1);
                int action = rng.Next(0, 3);
                if (action == 0) w.RequestCloseInterface(a);
                else if (action == 1) { w.RequestTradeAccept(a); w.RequestTradeAccept(b); w.RequestTradeOffer(a, 0, 1); w.RequestCloseInterface(b); }
                else { for (int i = 0; i < 2; i++) { w.RequestTradeAccept(a); w.RequestTradeAccept(b); } }
                if (a.Trade != null) w.RequestCloseInterface(a);
                Assert.Equal(totalCoins, a.Inventory.Count("coins") + b.Inventory.Count("coins"));
                Assert.Equal(totalOre, a.Inventory.Count("copper_ore") + b.Inventory.Count("copper_ore"));
            }
        }
    }

    public class FarmingTests
    {
        [Fact]
        public void PlantWaterGrowHarvest()
        {
            long now = 1_700_000_000;
            var w = TestUtil.NewWorld();
            w.Clock = () => now;
            var p = TestUtil.AddPlayer(w);
            foreach (var t in new[] { "rake", "spade", "watering_can" }) p.Inventory.Add(t, 1);
            p.Inventory.Add("potato_seed", 1);
            var patch = TestUtil.Nearest<FarmPatch>(w, p.Position);
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, patch));
            w.RequestInteract(p, patch.Id, "Rake");
            Assert.Equal(PatchStatus.Empty, p.Farming.Get(patch.Id).Status);
            w.RequestUseItemOnEntity(p, p.Inventory.IndexOf("potato_seed"), patch.Id);
            Assert.Equal(PatchStatus.Growing, p.Farming.Get(patch.Id).Status);
            var crop = w.Db.Crops["potato"];
            for (int stage = 0; stage < crop.Stages; stage++)
            {
                w.RequestUseItemOnEntity(p, p.Inventory.IndexOf("watering_can"), patch.Id);
                now += FarmingRules.StageSeconds(crop);
            }
            w.RequestInteract(p, patch.Id, "Harvest");
            var st = p.Farming.Get(patch.Id);
            // a watered crop has a 3% chance per stage to fail; this seed is deterministic
            Assert.True(p.Inventory.Count("potato") >= crop.YieldMin || st.Status == PatchStatus.Dead);
            Assert.True(p.Skills.Xp(Skill.Farming) > 0);
        }

        [Fact]
        public void GrowthIsDeterministicFromPlantTime()
        {
            var crop = TestUtil.Db.Crops["sunroot"];
            var s1 = new PatchState { PatchId = 5, Status = PatchStatus.Growing, Crop = "sunroot", PlantedAtUnix = 1000 };
            var s2 = new PatchState { PatchId = 5, Status = PatchStatus.Growing, Crop = "sunroot", PlantedAtUnix = 1000 };
            long t = 1000 + FarmingRules.StageSeconds(crop) * 4;
            Assert.Equal(FarmingRules.CurrentStage(s1, crop, t, out var d1), FarmingRules.CurrentStage(s2, crop, t, out var d2));
            Assert.Equal(d1, d2);
        }
    }

    public class PersistenceTests
    {
        [Fact]
        public void CaptureRestoreRoundTrip()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w, "Saver", 77);
            p.Skills.AddXp(Skill.Mining, 1234.5);
            p.Inventory.Add("iron_ore", 3);
            p.Bank.Add("coins", 5000);
            w.RequestEquip(p, p.Inventory.IndexOf("bronze_dagger"));
            w.StartQuest(p, "first_forge");
            p.Farming.Get(1234).Status = PatchStatus.Empty;
            p.Settings["volume"] = "0.5";
            var data = CharacterMapper.Capture(p);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(data, GameDatabase.JsonSettings);
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<CharacterData>(json, GameDatabase.JsonSettings);
            var r = CharacterMapper.Restore(w, back);
            Assert.Equal(p.Skills.XpTenths(Skill.Mining), r.Skills.XpTenths(Skill.Mining));
            Assert.Equal(3, r.Inventory.Count("iron_ore"));
            Assert.Equal(5000, r.Bank.Count("coins"));
            Assert.Equal("bronze_dagger", r.Equipment[EquipSlot.Weapon].ItemId);
            Assert.True(r.Quests.IsStarted("first_forge"));
            Assert.Equal(PatchStatus.Empty, r.Farming.Get(1234).Status);
            Assert.Equal("0.5", r.Settings["volume"]);
            Assert.Equal(p.Position, r.Position);
        }

        [Fact]
        public void RestoreDropsUnknownItemsSafely()
        {
            var w = TestUtil.NewWorld();
            var d = new CharacterData { Name = "Ghost", Inventory = { new SlotData { Slot = 0, Item = "removed_item", Qty = 1 }, new SlotData { Slot = 99, Item = "coins", Qty = 5 } } };
            var p = CharacterMapper.Restore(w, d);
            Assert.Equal(28, p.Inventory.FreeSlots);
        }

        [Fact]
        public void SqliteStoreRoundTripAndUniqueness()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emb_test_" + System.Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var store = new Embervale.Server.SqliteGameStore(path))
                {
                    long acc = store.CreateAccount("alice", "h", "s");
                    Assert.True(acc > 0);
                    Assert.Equal(0, store.CreateAccount("ALICE", "h", "s"));   // case-insensitive unique
                    long id = store.CreateCharacter(acc, new CharacterData { Name = "Alice", X = 5 });
                    Assert.True(id > 0);
                    Assert.Equal(0, store.CreateCharacter(acc, new CharacterData { Name = "alice" }));
                    var c = store.LoadCharacter(id);
                    c.X = 42;
                    store.SaveCharacters(new[] { c });
                }
                using (var store = new Embervale.Server.SqliteGameStore(path))
                {
                    Assert.True(store.TryGetAccount("alice", out var acc, out _, out _));
                    var list = store.ListCharacters(acc);
                    Assert.Single(list);
                    Assert.Equal(42, list[0].X);
                }
            }
            finally
            {
                foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
            }
        }

        [Fact]
        public void WorldStateRestoresDepletedNodesAndShopStock()
        {
            var w = TestUtil.NewWorld();
            var node = w.Nodes.First(n => n.DefId == "coal_rock");
            node.DepletedUntilTick = w.Tick + 30;
            w.Shops["brookhollow_general"].Stock["knife"] = 1;
            var data = WorldStateData.Capture(w);
            var w2 = TestUtil.NewWorld();
            data.Apply(w2);
            Assert.True(w2.Get<ResourceNode>(node.Id).Depleted);
            Assert.Equal(1, w2.Shops["brookhollow_general"].StockOf("knife"));
        }

        [Fact]
        public void PasswordHashVerify()
        {
            var salt = Embervale.Shared.Net.PasswordHasher.NewSalt();
            var h = Embervale.Shared.Net.PasswordHasher.Hash("hunter22", salt);
            Assert.True(Embervale.Shared.Net.PasswordHasher.Verify("hunter22", salt, h));
            Assert.False(Embervale.Shared.Net.PasswordHasher.Verify("hunter23", salt, h));
        }
    }

    public class SkillingMiscTests
    {
        [Fact]
        public void BuryingBonesGivesPrayerXp()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("bones", 1);
            w.RequestItemOption(p, p.Inventory.IndexOf("bones"), "Bury");
            Assert.Equal(5, p.Skills.Xp(Skill.Prayer));
            Assert.Equal(0, p.Inventory.Count("bones"));
        }

        [Fact]
        public void RunecraftingBindsAllEssence()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Inventory.Add("blank_essence", 25);
            var altar = TestUtil.Nearest<Station>(w, p.Position, s => s.StationType == "Altar_air");
            TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, altar));
            w.RequestInteract(p, altar.Id, "Craft-rune");
            TestUtil.Run(w, 4);
            Assert.Equal(25, p.Inventory.Count("air_rune"));
            Assert.Equal(0, p.Inventory.Count("blank_essence"));
        }

        [Fact]
        public void TeleportSpellMovesPlayer()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            p.Skills.AddXp(Skill.Magic, w.Db.XpTable.XpForLevel(30));
            p.Inventory.Add("path_rune", 2);
            p.Inventory.Add("air_rune", 4);
            w.RequestCastSpell(p, "capital_path", 0);
            Assert.True(Vec2.Distance(p.Position, w.Location("highmarch_plaza")) < 1);
            Assert.Equal(0, p.Inventory.Count("path_rune"));
        }

        [Fact]
        public void ChatIsSanitizedAndRateLimited()
        {
            Assert.Equal("hello bworld/b", GameWorld.SanitizeChat("  hello <b>world</b>\n "));
            Assert.Null(GameWorld.SanitizeChat("   "));
            Assert.Equal(GameWorld.MaxChatLength, GameWorld.SanitizeChat(new string('a', 500)).Length);
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            for (int i = 0; i < 10; i++) w.RequestChat(p, ChatChannel.Local, "spam " + i);
            Assert.Equal(6, w.Events.Count(e => e.Type == WorldEventType.Chat));
        }

        [Fact]
        public void DoorsBlockUntilOpened()
        {
            var w = TestUtil.NewWorld();
            var door = w.Doors.First();
            Assert.False(w.Grid.IsWalkable(door.Position.TileX, door.Position.TileZ));
            w.SetDoor(door, true);
            Assert.True(w.Grid.IsWalkable(door.Position.TileX, door.Position.TileZ));
            TestUtil.Run(w, 61);
            Assert.False(door.Open);
        }

        [Fact]
        public void PlayerWalksIntoBuildingThroughDoor()
        {
            var w = TestUtil.NewWorld();
            var p = TestUtil.AddPlayer(w);
            var banker = TestUtil.Nearest<Npc>(w, p.Position, n => n.DefId == "banker");
            w.RequestInteract(p, banker.Id, "Bank");
            Assert.True(TestUtil.RunUntil(w, () => p.OpenBankEntityId != 0, 200));
        }
    }
}
