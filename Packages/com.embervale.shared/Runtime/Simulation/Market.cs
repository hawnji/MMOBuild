using System;
using System.Collections.Generic;
using System.Linq;

namespace Embervale.Shared.Simulation
{
    /// <summary>
    /// One buy or sell offer on the Embervale Exchange. Escrow is held by the order: a buy order holds
    /// Quantity * Price coins, a sell order holds the items. Proceeds wait in the Collect* fields until the
    /// owner collects them at an Exchange clerk, so orders fill while their owner is offline.
    /// </summary>
    public sealed class MarketOrder
    {
        public long Id;
        public long CharacterId;
        public string OwnerName;
        public bool Buy;
        public string ItemId;
        public int Quantity;
        public int Filled;
        public int Price;              // coins per item
        public long CreatedUnix;
        public bool Cancelled;
        public int CollectItems;       // items waiting to be collected
        public long CollectCoins;      // coins waiting to be collected

        [Newtonsoft.Json.JsonIgnore] public int Remaining => Quantity - Filled;
        [Newtonsoft.Json.JsonIgnore] public bool Active => !Cancelled && Filled < Quantity;
        [Newtonsoft.Json.JsonIgnore] public bool HasCollection => CollectItems > 0 || CollectCoins > 0;
        [Newtonsoft.Json.JsonIgnore] public bool Finished => !Active && !HasCollection;

        public MarketOrder Clone() => (MarketOrder)MemberwiseClone();
    }

    public sealed class MarketFill
    {
        public MarketOrder BuyOrder, SellOrder;
        public int Quantity;
        public int Price;
    }

    /// <summary>
    /// Price-time priority order book for player trading (the "auction house"). Pure logic: no inventories
    /// here, the GameWorld moves items/coins into and out of escrow. Trades execute at the resting order's
    /// price; buyers are refunded any difference. A 1% coin tax on sales removes coins from the economy.
    /// </summary>
    public sealed class Marketplace
    {
        public const int MaxOrdersPerCharacter = 6;
        public const int MaxPrice = 1_000_000_000;
        public const double TaxRate = 0.01;

        public readonly List<MarketOrder> Orders = new List<MarketOrder>();
        public readonly Dictionary<string, int> LastPrice = new Dictionary<string, int>();
        public long NextId = 1;
        public long TaxCollected;

        public IEnumerable<MarketOrder> For(long characterId) => Orders.Where(o => o.CharacterId == characterId);

        public int OpenSlots(long characterId) => MaxOrdersPerCharacter - Orders.Count(o => o.CharacterId == characterId);

        public MarketOrder Find(long id) => Orders.FirstOrDefault(o => o.Id == id);

        public static long Tax(long gross) => (long)Math.Floor(gross * TaxRate);

        /// <summary>
        /// Adds an order whose escrow the caller has already taken, then matches it. Returns the fills
        /// (possibly none). Validation of item/quantity/price is the caller's job.
        /// </summary>
        public List<MarketFill> Place(MarketOrder order)
        {
            if (order.Quantity <= 0 || order.Price <= 0 || order.Price > MaxPrice) throw new ArgumentException("bad order");
            order.Id = NextId++;
            Orders.Add(order);
            return Match(order);
        }

        private List<MarketFill> Match(MarketOrder incoming)
        {
            var fills = new List<MarketFill>();
            IEnumerable<MarketOrder> book = incoming.Buy
                ? Orders.Where(o => !o.Buy && o.Active && o.ItemId == incoming.ItemId && o.Price <= incoming.Price && o.CharacterId != incoming.CharacterId)
                        .OrderBy(o => o.Price).ThenBy(o => o.Id)
                : Orders.Where(o => o.Buy && o.Active && o.ItemId == incoming.ItemId && o.Price >= incoming.Price && o.CharacterId != incoming.CharacterId)
                        .OrderByDescending(o => o.Price).ThenBy(o => o.Id);
            foreach (var resting in book.ToList())
            {
                if (!incoming.Active) break;
                int qty = Math.Min(incoming.Remaining, resting.Remaining);
                if (qty <= 0) continue;
                var buy = incoming.Buy ? incoming : resting;
                var sell = incoming.Buy ? resting : incoming;
                int price = resting.Price;
                long gross = (long)price * qty;
                long tax = Tax(gross);
                buy.Filled += qty;
                sell.Filled += qty;
                buy.CollectItems += qty;
                buy.CollectCoins += (long)(buy.Price - price) * qty; // refund overpay
                sell.CollectCoins += gross - tax;
                TaxCollected += tax;
                LastPrice[incoming.ItemId] = price;
                fills.Add(new MarketFill { BuyOrder = buy, SellOrder = sell, Quantity = qty, Price = price });
            }
            return fills;
        }

        /// <summary>Cancels an active order and moves the unfilled escrow to its collection box.</summary>
        public bool Cancel(long characterId, long orderId)
        {
            var o = Find(orderId);
            if (o == null || o.CharacterId != characterId || !o.Active) return false;
            if (o.Buy) o.CollectCoins += (long)o.Remaining * o.Price;
            else o.CollectItems += o.Remaining;
            o.Cancelled = true;
            return true;
        }

        /// <summary>Removes finished orders (fully filled or cancelled, and fully collected).</summary>
        public void Prune() => Orders.RemoveAll(o => o.Finished);

        /// <summary>Best bid/ask and depth for an item.</summary>
        public (int bestBuy, int buyQty, int bestSell, int sellQty, int last) Quote(string itemId)
        {
            int bestBuy = 0, buyQty = 0, bestSell = 0, sellQty = 0;
            foreach (var o in Orders)
            {
                if (!o.Active || o.ItemId != itemId) continue;
                if (o.Buy) { if (o.Price > bestBuy) bestBuy = o.Price; buyQty += o.Remaining; }
                else { if (bestSell == 0 || o.Price < bestSell) bestSell = o.Price; sellQty += o.Remaining; }
            }
            LastPrice.TryGetValue(itemId, out var last);
            return (bestBuy, buyQty, bestSell, sellQty, last);
        }

        /// <summary>Total escrow currently held (for audits/tests): coins in buy orders + collections, items per id.</summary>
        public (long coins, Dictionary<string, long> items) Escrow()
        {
            long coins = 0;
            var items = new Dictionary<string, long>();
            foreach (var o in Orders)
            {
                coins += o.CollectCoins;
                if (o.Buy && o.Active) coins += (long)o.Remaining * o.Price;
                long it = o.CollectItems + (!o.Buy && o.Active ? o.Remaining : 0);
                if (it > 0) { items.TryGetValue(o.ItemId, out var c); items[o.ItemId] = c + it; }
            }
            return (coins, items);
        }
    }
}
