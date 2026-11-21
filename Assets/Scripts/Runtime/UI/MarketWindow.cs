using System.Collections.Generic;
using System.Linq;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>
    /// The Embervale Exchange (player marketplace). Left: your offers with progress, collect and cancel.
    /// Right: a new buy or sell offer with item search, quantity, price and the live order-book quote.
    /// Click an inventory item while the window is open to sell it.
    /// </summary>
    public sealed class MarketWindow : GameWindow
    {
        private RectTransform _orders, _results;
        private Text _slots, _quote, _selectedName, _total;
        private Image _selectedIcon;
        private InputField _search, _qty, _price;
        private Button _buyTab, _sellTab, _place;
        private bool _buy = true;
        private string _item;

        protected override void Create()
        {
            var body = MakeWindow("Embervale Exchange", new Vector2(760, 500));
            Root.anchoredPosition = new Vector2(-160, 40);

            // ---- left: my offers
            var left = UIKit.Node("Offers", body).Place(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(380, 452));
            _slots = UIKit.Text(left, "Slots", "", 15, TextAnchor.MiddleLeft, UIKit.TextGold);
            _slots.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(380, 22));
            _orders = UIKit.Scroll(left, "List", out var scroll);
            ((RectTransform)scroll.transform).Stretch(0, 0, 26, 0);
            UIKit.VList(_orders.gameObject, 4, 4);

            // ---- right: new offer
            var right = UIKit.Panel(body, "NewOffer", UIKit.PanelInner, false).rectTransform;
            right.Place(new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(345, 452));
            _buyTab = UIKit.Button(right, "BuyTab", "Buy", () => SetMode(true), 16);
            ((RectTransform)_buyTab.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -8), new Vector2(160, 30));
            _sellTab = UIKit.Button(right, "SellTab", "Sell", () => SetMode(false), 16);
            ((RectTransform)_sellTab.transform).Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(160, 30));

            _search = UIKit.Input(right, "Search", "Search items to buy...", false, 15);
            ((RectTransform)_search.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -46), new Vector2(329, 30));
            _search.onValueChanged.AddListener(_ => RefreshResults());
            _results = UIKit.Node("Results", right).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(329, 132));
            UIKit.VList(_results.gameObject, 1, 0);

            _selectedIcon = UIKit.Image(right, "SelectedIcon", null);
            _selectedIcon.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -218), new Vector2(44, 44));
            _selectedName = UIKit.Text(right, "SelectedName", "", 16, TextAnchor.MiddleLeft, UIKit.TextGold);
            _selectedName.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -218), new Vector2(270, 44));
            _quote = UIKit.Text(right, "Quote", "", 13, TextAnchor.UpperLeft, UIKit.TextDim);
            _quote.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -266), new Vector2(325, 54));

            UIKit.Text(right, "QtyLabel", "Quantity", 14, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -326), new Vector2(80, 28));
            _qty = UIKit.Input(right, "Qty", "1", false, 15);
            _qty.contentType = InputField.ContentType.IntegerNumber;
            ((RectTransform)_qty.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -326), new Vector2(110, 28));
            _qty.text = "1";
            _qty.onValueChanged.AddListener(_ => UpdateTotal());
            UIKit.Text(right, "PriceLabel", "Price each", 14, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -360), new Vector2(80, 28));
            _price = UIKit.Input(right, "Price", "coins", false, 15);
            _price.contentType = InputField.ContentType.IntegerNumber;
            ((RectTransform)_price.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(90, -360), new Vector2(110, 28));
            _price.onValueChanged.AddListener(_ => UpdateTotal());
            var minus = UIKit.Button(right, "Minus", "-5%", () => Nudge(0.95f), 13);
            ((RectTransform)minus.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(206, -360), new Vector2(56, 28));
            var plus = UIKit.Button(right, "Plus", "+5%", () => Nudge(1.05f), 13);
            ((RectTransform)plus.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(266, -360), new Vector2(56, 28));
            _total = UIKit.Text(right, "Total", "", 14, TextAnchor.MiddleLeft);
            _total.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -392), new Vector2(325, 22));
            _place = UIKit.Button(right, "Place", "Place offer", Place, 17);
            ((RectTransform)_place.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(200, 34));
            SetMode(true);
        }

        private void SetMode(bool buy)
        {
            _buy = buy;
            _buyTab.GetComponent<Image>().color = buy ? new Color(0.6f, 0.45f, 0.22f) : UIKit.ButtonColor;
            _sellTab.GetComponent<Image>().color = !buy ? new Color(0.6f, 0.45f, 0.22f) : UIKit.ButtonColor;
            ((Text)_search.placeholder).text = buy ? "Search items to buy..." : "Click an item in your inventory, or search";
            _place.SetLabel(buy ? "Place buy offer" : "Place sell offer");
            RefreshResults();
            UpdateTotal();
        }

        public void SelectForSale(string itemId)
        {
            SetMode(false);
            Select(itemId);
            _qty.text = C.InventoryCount(itemId).ToString();
        }

        private void Select(string itemId)
        {
            var def = Db.Item(itemId);
            if (def == null) return;
            _item = itemId;
            _selectedIcon.sprite = AssetRegistry.Instance.Icon(def.Icon);
            _selectedName.text = def.Name + "\n<size=12><color=#c8c0b0>Shop value " + UIKit.Gp(def.Value) + "</color></size>";
            _quote.text = "Checking the order book...";
            if (string.IsNullOrEmpty(_price.text)) _price.text = Mathf.Max(1, def.Value).ToString();
            C.Send(ClientPackets.MarketQuote(itemId));
            UpdateTotal();
        }

        public void ShowQuote(MarketQuoteView q)
        {
            if (q == null || q.ItemId != _item || Root == null) return;
            _quote.text = "Highest buy offer: " + (q.BestBuy > 0 ? q.BestBuy.ToString("N0") + " (" + q.BuyQty + " wanted)" : "none") +
                          "\nLowest sell offer: " + (q.BestSell > 0 ? q.BestSell.ToString("N0") + " (" + q.SellQty + " for sale)" : "none") +
                          "\nLast trade: " + (q.LastPrice > 0 ? q.LastPrice.ToString("N0") : "never traded");
            int guide = q.LastPrice > 0 ? q.LastPrice : (_buy ? q.BestSell : q.BestBuy);
            if (guide > 0) _price.text = guide.ToString();
        }

        private void Nudge(float f)
        {
            if (!int.TryParse(_price.text, out var p)) p = 1;
            int next = Mathf.Max(1, Mathf.RoundToInt(p * f));
            if (next == p) next = f > 1 ? p + 1 : Mathf.Max(1, p - 1);
            _price.text = next.ToString();
        }

        private void UpdateTotal()
        {
            if (_total == null) return;
            int.TryParse(_qty.text, out var q);
            int.TryParse(_price.text, out var p);
            long total = (long)q * p;
            if (_buy) _total.text = "Total: " + total.ToString("N0") + " coins  (you have " + C.InventoryCount("coins").ToString("N0") + ")";
            else _total.text = "You receive up to " + (total - (long)System.Math.Floor(total * 0.01)).ToString("N0") + " coins after the 1% fee";
            _place.interactable = _item != null && q > 0 && p > 0;
        }

        private void Place()
        {
            if (_item == null || !int.TryParse(_qty.text, out var q) || !int.TryParse(_price.text, out var p) || q <= 0 || p <= 0) return;
            C.Send(ClientPackets.MarketPlace(_buy, _item, q, p));
        }

        private void RefreshResults()
        {
            foreach (Transform t in _results) Object.Destroy(t.gameObject);
            var term = _search.text.Trim().ToLowerInvariant();
            if (term.Length < 2) return;
            IEnumerable<ItemDef> items = Db.Items.Values.Where(i => i.Tradeable && i.Id != "coins" && i.Name.ToLowerInvariant().Contains(term));
            if (!_buy) items = items.Where(i => C.InventoryCount(i.Id) > 0);
            foreach (var def in items.OrderBy(i => i.Name.Length).ThenBy(i => i.Name).Take(5))
            {
                var d = def;
                var b = UIKit.Button(_results, d.Id, "  " + d.Name, () => { Select(d.Id); _search.text = ""; }, 14);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                b.GetComponent<Image>().color = new Color(0.18f, 0.15f, 0.12f, 0.9f);
                b.Size(25);
                var icon = UIKit.Image(b.transform, "Icon", AssetRegistry.Instance.Icon(d.Icon));
                icon.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(22, 22));
            }
        }

        public void Refresh()
        {
            if (Root == null) return;
            foreach (Transform t in _orders) Object.Destroy(t.gameObject);
            var list = C.MarketOrders;
            _slots.text = "Your offers (" + list.Count + "/6)";
            if (list.Count == 0)
            {
                var hint = UIKit.Text(_orders, "Empty", "You have no offers. Create one on the right: buy offers hold your coins, sell offers hold your items until they trade.", 14, TextAnchor.UpperLeft, UIKit.TextDim);
                hint.Size(80);
            }
            foreach (var o in list) OrderRow(o);
            UpdateTotal();
            if (!_buy) RefreshResults();
        }

        private void OrderRow(MarketOrderView o)
        {
            var def = Db.Item(o.ItemId);
            var row = UIKit.Panel(_orders, "Order" + o.Id, UIKit.PanelInner, false);
            row.Size(64);
            var icon = UIKit.Image(row.transform, "Icon", AssetRegistry.Instance.Icon(def?.Icon));
            icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(40, 40));
            string state = o.Cancelled ? "<color=#ff8060>Cancelled</color>" : o.Active ? "<color=#80c0ff>Active</color>" : "<color=#80ff80>Complete</color>";
            var t = UIKit.Text(row.transform, "Info",
                "<b>" + (o.Buy ? "Buy" : "Sell") + "</b> " + (def?.Name ?? o.ItemId) + "  " + state +
                "\n<size=12>" + o.Filled + "/" + o.Quantity + " at " + o.Price.ToString("N0") + " each" +
                (o.CollectItems > 0 || o.CollectCoins > 0 ? "  <color=#ffd040>To collect: " + (o.CollectItems > 0 ? o.CollectItems + " items " : "") + (o.CollectCoins > 0 ? o.CollectCoins.ToString("N0") + " coins" : "") + "</color>" : "") + "</size>", 14);
            t.rectTransform.Stretch(52, 92, 4, 14);
            var barBg = UIKit.Image(row.transform, "Bar", null, new Color(0, 0, 0, 0.5f));
            barBg.preserveAspect = false;
            barBg.rectTransform.anchorMin = new Vector2(0, 0);
            barBg.rectTransform.anchorMax = new Vector2(1, 0);
            barBg.rectTransform.offsetMin = new Vector2(52, 5);
            barBg.rectTransform.offsetMax = new Vector2(-92, 10);
            var fill = UIKit.Image(barBg.transform, "Fill", null, o.Cancelled ? UIKit.Bad : UIKit.Good);
            fill.preserveAspect = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(o.Quantity > 0 ? o.Filled / (float)o.Quantity : 0, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            if (o.CollectItems > 0 || o.CollectCoins > 0)
            {
                var c = UIKit.Button(row.transform, "Collect", "Collect", () => C.Send(ClientPackets.MarketCollect(o.Id)), 13);
                ((RectTransform)c.transform).Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, -6), new Vector2(80, 24));
            }
            if (o.Active)
            {
                var x = UIKit.Button(row.transform, "Cancel", "Cancel", () => C.Send(ClientPackets.MarketCancel(o.Id)), 13);
                ((RectTransform)x.transform).Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-6, 6), new Vector2(80, 24));
            }
        }
    }
}
