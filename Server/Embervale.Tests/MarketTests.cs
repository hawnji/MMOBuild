using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Shared;
using Embervale.Shared.Net;
using Embervale.Shared.Persistence;
using Embervale.Shared.Simulation;
using Xunit;

namespace Embervale.Tests
{
    /// <summary>The Embervale Exchange: order matching, escrow, cancellation, collection and persistence.</summary>
    public class MarketTests
    {
        private static MarketOrder Order(long cid, bool buy, string item, int qty, int price) =>
            new MarketOrder { CharacterId = cid, OwnerName = "c" + cid, Buy = buy, ItemId = item, Quantity = qty, Price = price };

        [Fact]
        public void BuyMatchesCheapestSellAtRestingPriceAndRefundsDifference()
        {
            var m = new Marketplace();
            var s1 = Order(1, false, "air_rune", 10, 30); m.Place(s1);
            var s2 = Order(2, false, "air_rune", 10, 25); m.Place(s2);
            var b = Order(3, true, "air_rune", 15, 40);
            var fills = m.Place(b);
            Assert.Equal(2, fills.Count);
            Assert.Equal(25, fills[0].Price);           // cheapest first
            Assert.Equal(10, fills[0].Quantity);
            Assert.Equal(30, fills[1].Price);
            Assert.Equal(5, fills[1].Quantity);
            Assert.Equal(15, b.CollectItems);
            Assert.Equal(10 * (40 - 25) + 5 * (40 - 30), b.CollectCoins);  // overpay refunded
            Assert.Equal(250 - Marketplace.Tax(250), s2.CollectCoins);
            Assert.Equal(150 - Marketplace.Tax(150), s1.CollectCoins);
            Assert.True(s1.Active);
            Assert.Equal(5, s1.Remaining);
            Assert.Equal(30, m.Quote("air_rune").last);
        }

        [Fact]
        public void SellMatchesHighestBidThenOldest()
        {
            var m = new Marketplace();
            var b1 = Order(1, true, "oak_logs", 5, 10); m.Place(b1);
            var b2 = Order(2, true, "oak_logs", 5, 12); m.Place(b2);
            var b3 = Order(3, true, "oak_logs", 5, 12); m.Place(b3);
            var s = Order(4, false, "oak_logs", 8, 9);
            var fills = m.Place(s);
            Assert.Equal(new[] { b2.Id, b3.Id }, fills.Select(f => f.BuyOrder.Id).ToArray());
            Assert.Equal(5, b2.Filled);
            Assert.Equal(3, b3.Filled);
            Assert.Equal(0, b1.Filled);
            Assert.Equal(8 * 12 - Marketplace.Tax(8 * 12), s.CollectCoins);
        }

        [Fact]
        public void NoMatchAcrossPriceGapOrWithYourself()
        {
            var m = new Marketplace();
            m.Place(Order(1, false, "oak_logs", 5, 20));
            Assert.Empty(m.Place(Order(2, true, "oak_logs", 5, 19)));
            Assert.Empty(m.Place(Order(1, true, "oak_logs", 5, 25)));  // own sell is skipped
            var q = m.Quote("oak_logs");
            Assert.Equal(25, q.bestBuy);
            Assert.Equal(20, q.bestSell);
        }

        [Fact]
        public void CancelReturnsUnfilledEscrow()
        {
            var m = new Marketplace();
            var b = Order(1, true, "oak_logs", 10, 7); m.Place(b);
            m.Place(Order(2, false, "oak_logs", 4, 7));
            Assert.True(m.Cancel(1, b.Id));
            Assert.False(m.Cancel(1, b.Id));
            Assert.Equal(4, b.CollectItems);
            Assert.Equal(6 * 7, b.CollectCoins);
            Assert.False(m.Cancel(2, b.Id + 1)); // not theirs / not active
        }

        // ------------------------------------------------------------------ world integration
        private static (GameWorld w, Player a, Player b, Npc clerk) Setup()
        {
            var w = TestUtil.NewWorld();
            var a = TestUtil.AddPlayer(w, "Seller", 101);
            var b = TestUtil.AddPlayer(w, "Buyer", 102);
            var clerk = TestUtil.Nearest<Npc>(w, a.Position, n => n.DefId == "exchange_clerk");
            Assert.NotNull(clerk);
            foreach (var p in new[] { a, b })
            {
                TestUtil.Teleport(w, p, TestUtil.StandNextTo(w, clerk));
                w.RequestInteract(p, clerk.Id, "Exchange");
                Assert.True(TestUtil.RunUntil(w, () => p.OpenMarketNpcId == clerk.Id, 50), "market opened");
            }
            return (w, a, b, clerk);
        }

        [Fact]
        public void ExchangeMovesItemsAndCoinsThroughEscrow()
        {
            var (w, a, b, _) = Setup();
            a.Inventory.Add("air_rune", 20);
            b.Inventory.Add("coins", 1000);
            int aCoins = a.Inventory.Count("coins"), bCoins = b.Inventory.Count("coins");

            w.RequestMarketPlace(a, false, "air_rune", 20, 30);
            Assert.Equal(0, a.Inventory.Count("air_rune"));
            w.RequestMarketPlace(b, true, "air_rune", 12, 35);
            Assert.Equal(bCoins - 12 * 35, b.Inventory.Count("coins"));

            var bo = w.Market.For(b.CharacterId).Single();
            var so = w.Market.For(a.CharacterId).Single();
            w.RequestMarketCollect(b, bo.Id);
            Assert.Equal(12, b.Inventory.Count("air_rune"));
            Assert.Equal(bCoins - 12 * 30, b.Inventory.Count("coins"));   // refunded 5/each
            w.RequestMarketCollect(a, so.Id);
            Assert.Equal(aCoins + 360 - Marketplace.Tax(360), a.Inventory.Count("coins"));
            Assert.Empty(w.Market.For(b.CharacterId));                    // finished & collected -> pruned
            w.RequestMarketCancel(a, so.Id);
            w.RequestMarketCollect(a, so.Id);
            Assert.Equal(20, a.Inventory.Count("air_rune") + 12);
        }

        [Fact]
        public void ExchangeRejectsBadOrders()
        {
            var (w, a, _, _) = Setup();
            int coins = a.Inventory.Count("coins");
            w.RequestMarketPlace(a, true, "air_rune", 1, coins + 1);        // can't afford
            w.RequestMarketPlace(a, false, "air_rune", 1, 10);              // don't own any
            w.RequestMarketPlace(a, false, "coins", 1, 10);                 // coins are not tradeable here
            w.RequestMarketPlace(a, true, "nonexistent_item", 1, 10);
            w.RequestMarketPlace(a, true, "air_rune", 0, 10);
            w.RequestMarketPlace(a, true, "air_rune", 1, -5);
            Assert.Empty(w.Market.Orders);
            Assert.Equal(coins, a.Inventory.Count("coins"));
            a.Inventory.Add("coins", 100);
            for (int i = 0; i < Marketplace.MaxOrdersPerCharacter + 2; i++) w.RequestMarketPlace(a, true, "air_rune", 1, 1);
            Assert.Equal(Marketplace.MaxOrdersPerCharacter, w.Market.Orders.Count);
        }

        [Fact]
        public void ExchangeRequiresStandingAtAClerk()
        {
            var (w, a, _, _) = Setup();
            a.Inventory.Add("air_rune", 5);
            TestUtil.Teleport(w, a, a.Position + new Vec2(20, 0));
            w.RequestMarketPlace(a, false, "air_rune", 5, 10);
            Assert.Empty(w.Market.Orders);
            Assert.Equal(5, a.Inventory.Count("air_rune"));
        }

        [Fact]
        public void RandomisedExchangeNeverCreatesOrDestroysValue()
        {
            var (w, a, b, _) = Setup();
            var rng = new Random(7);
            a.Inventory.Add("coins", 50_000); b.Inventory.Add("coins", 50_000);
            a.Inventory.Add("air_rune", 200); b.Inventory.Add("air_rune", 200);
            long Total(string item) => a.Inventory.Count(item) + b.Inventory.Count(item) + (item == "coins" ? w.Market.Escrow().coins + w.Market.TaxCollected : w.Market.Escrow().items.GetValueOrDefault(item));
            long coins0 = Total("coins"), ore0 = Total("air_rune");
            for (int i = 0; i < 400; i++)
            {
                var p = rng.Next(2) == 0 ? a : b;
                switch (rng.Next(4))
                {
                    case 0: w.RequestMarketPlace(p, true, "air_rune", rng.Next(1, 30), rng.Next(5, 40)); break;
                    case 1: w.RequestMarketPlace(p, false, "air_rune", rng.Next(1, 30), rng.Next(5, 40)); break;
                    case 2: { var o = w.Market.For(p.CharacterId).FirstOrDefault(); if (o != null) w.RequestMarketCancel(p, o.Id); break; }
                    case 3: { var o = w.Market.For(p.CharacterId).FirstOrDefault(x => x.HasCollection); if (o != null) w.RequestMarketCollect(p, o.Id); break; }
                }
                Assert.Equal(coins0, Total("coins"));
                Assert.Equal(ore0, Total("air_rune"));
            }
        }

        [Fact]
        public void OrdersSurviveServerRestartAndFillWhileOffline()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emb_market_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var cl = new Cluster(new FileGameStore(path));
                var seller = cl.Join("Merra");
                var sp = cl.PlayerOf(seller);
                sp.Inventory.Add("mithril_bar", 4);
                var clerk = TestUtil.Nearest<Npc>(cl.Server.World, sp.Position, n => n.DefId == "exchange_clerk");
                TestUtil.Teleport(cl.Server.World, sp, TestUtil.StandNextTo(cl.Server.World, clerk));
                seller.Interact(clerk.Id, "Exchange");
                Assert.True(cl.TickUntil(() => seller.MarketOpen, 50));
                seller.Send(ClientPackets.MarketPlace(false, "mithril_bar", 4, 500));
                cl.Tick();
                Assert.Single(seller.MarketOrders);
                Assert.Equal(0, sp.Inventory.Count("mithril_bar"));
                cl.Server.Shutdown("restart");

                var cl2 = new Cluster(new FileGameStore(path));
                Assert.Single(cl2.Server.World.Market.Orders);
                var buyer = cl2.Join("Tovin");
                var bp = cl2.PlayerOf(buyer);
                bp.Inventory.Add("coins", 5000);
                TestUtil.Teleport(cl2.Server.World, bp, TestUtil.StandNextTo(cl2.Server.World, clerk));
                buyer.Interact(clerk.Id, "Exchange");
                Assert.True(cl2.TickUntil(() => buyer.MarketOpen, 50));
                buyer.Send(ClientPackets.MarketQuote("mithril_bar"));
                cl2.Tick();
                Assert.Equal(500, buyer.MarketQuote.BestSell);
                buyer.Send(ClientPackets.MarketPlace(true, "mithril_bar", 4, 600));
                cl2.Tick();
                var mine = buyer.MarketOrders.Single();
                Assert.Equal(4, mine.CollectItems);
                buyer.Send(ClientPackets.MarketCollect(mine.Id));
                cl2.Tick();
                Assert.Equal(4, bp.Inventory.Count("mithril_bar"));
                Assert.Empty(buyer.MarketOrders);

                // The seller logs back in later and collects the coins.
                var back = cl2.Join("Merra", register: false);
                var sp2 = cl2.PlayerOf(back);
                int before = sp2.Inventory.Count("coins");
                TestUtil.Teleport(cl2.Server.World, sp2, TestUtil.StandNextTo(cl2.Server.World, clerk));
                back.Interact(clerk.Id, "Exchange");
                Assert.True(cl2.TickUntil(() => back.MarketOpen, 50));
                var so = back.MarketOrders.Single();
                Assert.Equal(2000 - Marketplace.Tax(2000), so.CollectCoins);
                back.Send(ClientPackets.MarketCollect(so.Id));
                cl2.Tick();
                Assert.Equal(before + 2000 - Marketplace.Tax(2000), sp2.Inventory.Count("coins"));
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }
    }
}
