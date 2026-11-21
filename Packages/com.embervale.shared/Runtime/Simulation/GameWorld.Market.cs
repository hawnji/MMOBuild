using System;
using System.Collections.Generic;
using System.Linq;

namespace Embervale.Shared.Simulation
{
    /// <summary>Order in which a market change must be persisted so a crash can lose but never duplicate value.</summary>
    public enum MarketPersist
    {
        /// <summary>Escrow left the character (place order): save the character first, then the world.</summary>
        CharacterThenWorld,
        /// <summary>Only the order book changed (fills, cancels).</summary>
        WorldOnly,
        /// <summary>Value moved from the order book to the character (collect): save the world first.</summary>
        WorldThenCharacter,
    }

    public sealed partial class GameWorld
    {
        public readonly Marketplace Market = new Marketplace();

        /// <summary>Raised whenever the market changes; the server persists in the given order.</summary>
        public event Action<Player, MarketPersist> MarketChanged;

        public const string CoinsId = "coins";

        private void OpenMarket(Player p, Npc clerk)
        {
            CloseInterfaces(p);
            p.OpenMarketNpcId = clerk.Id;
            Emit(new WorldEvent { Type = WorldEventType.MarketOpen, PlayerId = p.Id, EntityId = clerk.Id });
            Emit(new WorldEvent { Type = WorldEventType.MarketUpdate, PlayerId = p.Id });
        }

        private bool MarketAccessible(Player p)
        {
            if (p.OpenMarketNpcId == 0 || p.Trade != null || p.IsDead) return false;
            var e = Get(p.OpenMarketNpcId);
            return e != null && Vec2.TileDistance(e.Position, p.Position) <= 4;
        }

        private Player OnlineByCharacter(long characterId)
        {
            foreach (var p in _players) if (p.CharacterId == characterId) return p;
            return null;
        }

        public void RequestMarketQuote(Player p, string itemId)
        {
            if (!MarketAccessible(p) || Db.Item(itemId) == null) return;
            Emit(new WorldEvent { Type = WorldEventType.MarketQuote, PlayerId = p.Id, Text = itemId });
        }

        public void RequestMarketPlace(Player p, bool buy, string itemId, int qty, int price)
        {
            if (!MarketAccessible(p)) return;
            var def = Db.Item(itemId);
            if (def == null || itemId == CoinsId || !def.Tradeable) { Message(p, "That item can't be traded on the Exchange."); return; }
            if (qty <= 0 || price <= 0 || price > Marketplace.MaxPrice) { Message(p, "Enter a valid quantity and price."); return; }
            if (p.CharacterId == 0) return;
            if (Market.OpenSlots(p.CharacterId) <= 0) { Message(p, "You can have at most " + Marketplace.MaxOrdersPerCharacter + " Exchange orders. Collect or cancel one first."); return; }

            if (buy)
            {
                long total = (long)qty * price;
                if (total > int.MaxValue) { Message(p, "That order is too large."); return; }
                if (p.Inventory.Count(CoinsId) < total) { Message(p, "You don't have enough coins for that offer."); return; }
                if (!p.Inventory.Remove(CoinsId, (int)total)) return;
            }
            else
            {
                if (p.Inventory.Count(itemId) < qty) { Message(p, "You don't have that many to sell."); return; }
                if (!p.Inventory.Remove(itemId, qty)) return;
            }

            var order = new MarketOrder
            {
                CharacterId = p.CharacterId, OwnerName = p.Name, Buy = buy, ItemId = itemId, Quantity = qty, Price = price, CreatedUnix = Clock(),
            };
            var fills = Market.Place(order);
            Message(p, (buy ? "Buy" : "Sell") + " offer placed: " + qty + " x " + def.Name + " at " + price + " coins each.");
            MarketChanged?.Invoke(p, MarketPersist.CharacterThenWorld);
            NotifyFills(fills);
            Emit(new WorldEvent { Type = WorldEventType.MarketUpdate, PlayerId = p.Id });
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = "coins" });
        }

        private void NotifyFills(List<MarketFill> fills)
        {
            if (fills.Count == 0) return;
            var touched = new HashSet<long>();
            foreach (var f in fills)
            {
                touched.Add(f.BuyOrder.CharacterId);
                touched.Add(f.SellOrder.CharacterId);
            }
            foreach (var cid in touched)
            {
                var owner = OnlineByCharacter(cid);
                if (owner == null) continue;
                foreach (var f in fills)
                {
                    var mine = f.BuyOrder.CharacterId == cid ? f.BuyOrder : f.SellOrder;
                    if (mine.CharacterId != cid) continue;
                    var name = Db.Item(mine.ItemId)?.Name ?? mine.ItemId;
                    Message(owner, "Exchange: " + (mine.Buy ? "bought " : "sold ") + f.Quantity + " x " + name + " at " + f.Price + " each" + (mine.Active ? "." : ". Your offer is complete - collect it from an Exchange clerk."));
                }
                Emit(new WorldEvent { Type = WorldEventType.MarketUpdate, PlayerId = owner.Id });
            }
        }

        public void RequestMarketCancel(Player p, long orderId)
        {
            if (!MarketAccessible(p)) return;
            if (!Market.Cancel(p.CharacterId, orderId)) return;
            Message(p, "Offer cancelled. Collect the unfilled part from the Exchange.");
            MarketChanged?.Invoke(p, MarketPersist.WorldOnly);
            Emit(new WorldEvent { Type = WorldEventType.MarketUpdate, PlayerId = p.Id });
        }

        public void RequestMarketCollect(Player p, long orderId)
        {
            if (!MarketAccessible(p)) return;
            var o = Market.Find(orderId);
            if (o == null || o.CharacterId != p.CharacterId || !o.HasCollection) return;
            bool moved = false;
            if (o.CollectCoins > 0)
            {
                long room = (long)int.MaxValue - p.Inventory.Count(CoinsId);
                int take = (int)Math.Min(o.CollectCoins, room);
                if (take > 0 && p.Inventory.CanAdd(CoinsId, take) && p.Inventory.Add(CoinsId, take)) { o.CollectCoins -= take; moved = true; }
            }
            if (o.CollectItems > 0)
            {
                int take = Math.Min(o.CollectItems, p.Inventory.SpaceFor(o.ItemId));
                if (take > 0 && p.Inventory.Add(o.ItemId, take)) { o.CollectItems -= take; moved = true; }
            }
            if (o.HasCollection) Message(p, "You don't have enough inventory space to collect everything.");
            if (!moved) return;
            Market.Prune();
            MarketChanged?.Invoke(p, MarketPersist.WorldThenCharacter);
            Emit(new WorldEvent { Type = WorldEventType.MarketUpdate, PlayerId = p.Id });
            Emit(new WorldEvent { Type = WorldEventType.Sound, EntityId = p.Id, Text = "pickup" });
        }
    }
}
