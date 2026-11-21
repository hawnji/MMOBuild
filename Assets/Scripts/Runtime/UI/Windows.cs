using System.Collections.Generic;
using System.Linq;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>Base for interface windows opened by the server (bank, shop...) or by the player (map).</summary>
    public abstract class GameWindow
    {
        public RectTransform Root { get; protected set; }
        protected Hud Hud;
        protected GameClient C => Hud.Client;
        protected GameDatabase Db => Hud.Db;
        /// <summary>True when the server holds the interface open and must be told it closed.</summary>
        protected virtual bool ServerSide => true;

        public void Build(Hud hud)
        {
            Hud = hud;
            Create();
        }

        protected abstract void Create();
        public virtual void Update() { }

        protected RectTransform MakeWindow(string title, Vector2 size)
        {
            var body = UIKit.Window(Hud.UI.WindowLayer, title, size, OnCloseRequested, out var root);
            Root = (RectTransform)root.transform;
            return body;
        }

        public virtual void OnCloseRequested()
        {
            if (ServerSide && C != null) C.Send(ClientPackets.CloseInterface());
            Hud.CloseWindow(this);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }
    }

    // ---------------------------------------------------------------------- dialogue
    public sealed class DialogueWindow : GameWindow
    {
        private Text _speaker, _text;
        private RectTransform _options;

        protected override void Create()
        {
            var panel = UIKit.Panel(Hud.UI.WindowLayer, "Dialogue");
            Root = panel.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 230), new Vector2(620, 220));
            _speaker = UIKit.Text(Root, "Speaker", "", 20, TextAnchor.MiddleCenter, UIKit.TextGold);
            _speaker.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(580, 28));
            _text = UIKit.Text(Root, "Text", "", 17, TextAnchor.UpperCenter);
            _text.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -38), new Vector2(580, 70));
            _options = UIKit.Node("Options", Root).Stretch(20, 20, 112, 8);
            UIKit.VList(_options.gameObject, 3, 0);
        }

        public void Show(DialogueView d)
        {
            _speaker.text = d.Speaker;
            _text.text = d.Text;
            foreach (Transform t in _options) Object.Destroy(t.gameObject);
            if (d.Options.Count == 0)
            {
                var b = UIKit.Button(_options, "Close", "<color=#80c0ff>Click here to continue</color>", OnCloseRequested, 16);
                b.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                b.Size(26);
            }
            for (int i = 0; i < d.Options.Count; i++)
            {
                int idx = i;
                var b = UIKit.Button(_options, "Opt" + i, "<color=#ffffff>" + (i + 1) + ". " + d.Options[i] + "</color>", () => C.Send(ClientPackets.DialogueOption(idx)), 16);
                b.GetComponent<Image>().color = new Color(0.25f, 0.2f, 0.15f, 0.6f);
                b.Size(24);
            }
        }

        public override void Update()
        {
            // Number keys pick options.
            if (UIRoot.IsTyping || C.Dialogue == null) return;
            if (InputAdapter.Down(InputAdapter.Key.Space) && C.Dialogue.Options.Count == 1) C.Send(ClientPackets.DialogueOption(0));
        }
    }

    // ---------------------------------------------------------------------- shop
    public sealed class ShopWindow : GameWindow
    {
        private RectTransform _grid;
        private Text _title, _coins;
        private readonly List<ItemSlotView> _slots = new List<ItemSlotView>();

        protected override void Create()
        {
            var body = MakeWindow("Shop", new Vector2(470, 430));
            Root.anchoredPosition = new Vector2(-200, 40);
            _title = UIKit.Text(body, "Name", "", 16, TextAnchor.MiddleCenter, UIKit.TextGold);
            _title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(440, 22));
            _grid = UIKit.Node("Grid", body).Stretch(0, 0, 26, 26);
            var g = _grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(48, 48);
            g.spacing = new Vector2(6, 6);
            _coins = UIKit.Text(body, "Coins", "", 14, TextAnchor.MiddleLeft, UIKit.TextDim);
            _coins.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(440, 22));
        }

        public void Refresh()
        {
            var shop = C.Shop;
            if (shop == null) return;
            _title.text = shop.Name + "  <size=12><color=#c8c0b0>(left-click: value, right-click: buy)</color></size>";
            while (_slots.Count < shop.Entries.Count)
            {
                var s = ItemSlotView.Create(_grid, "Shop", _slots.Count, 48);
                s.OnLeft = v => Value(v.Index);
                s.OnRight = v => BuyMenu(v.Index);
                s.Tooltip = v =>
                {
                    var e = C.Shop != null && v.Index < C.Shop.Entries.Count ? C.Shop.Entries[v.Index] : null;
                    return e == null ? null : (Db.Item(e.ItemId)?.Name ?? e.ItemId) + "\nBuy: " + e.BuyPrice + " coins\nSell: " + e.SellPrice + " coins\nStock: " + e.Stock;
                };
                _slots.Add(s);
            }
            for (int i = 0; i < _slots.Count; i++)
            {
                bool on = i < shop.Entries.Count;
                _slots[i].gameObject.SetActive(on);
                if (on) _slots[i].Set(shop.Entries[i].ItemId, Mathf.Max(shop.Entries[i].Stock, 0));
                if (on && shop.Entries[i].Stock <= 0) { _slots[i].Icon.enabled = true; _slots[i].Icon.sprite = AssetRegistry.Instance.Icon(Db.Item(shop.Entries[i].ItemId)?.Icon); _slots[i].Icon.color = new Color(1, 1, 1, 0.35f); _slots[i].Qty.text = "<color=#ff6060>0</color>"; }
            }
            _coins.text = "Your coins: " + C.InventoryCount("coins").ToString("N0");
        }

        private void Value(int i)
        {
            var e = C.Shop?.Entries.ElementAtOrDefault(i);
            if (e == null) return;
            Hud.Message((Db.Item(e.ItemId)?.Name ?? e.ItemId) + " costs " + e.BuyPrice + " coins.");
        }

        private void BuyMenu(int i)
        {
            var e = C.Shop?.Entries.ElementAtOrDefault(i);
            if (e == null) return;
            string name = "<color=#ff9a40>" + (Db.Item(e.ItemId)?.Name ?? e.ItemId) + "</color>";
            var items = new List<UIRoot.MenuItem> { new UIRoot.MenuItem("Value " + name, () => Value(i)) };
            foreach (var n in new[] { 1, 5, 10, 50 }) { int q = n; items.Add(new UIRoot.MenuItem("Buy " + q + " " + name, () => C.Send(ClientPackets.ShopBuy(e.ItemId, q)))); }
            items.Add(new UIRoot.MenuItem("Buy X " + name, () => UIRoot.Instance.Prompt("Buy how many?", "", true, s => { if (int.TryParse(s, out var q) && q > 0) C.Send(ClientPackets.ShopBuy(e.ItemId, q)); })));
            items.Add(new UIRoot.MenuItem("Cancel", null));
            UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", items);
        }
    }

    // ---------------------------------------------------------------------- bank
    public sealed class BankWindow : GameWindow
    {
        private RectTransform _grid;
        private Text _info;
        private InputField _search;
        private readonly List<ItemSlotView> _slots = new List<ItemSlotView>();

        protected override void Create()
        {
            var body = MakeWindow("Bank of Embervale", new Vector2(560, 520));
            Root.anchoredPosition = new Vector2(-200, 40);
            _search = UIKit.Input(body, "Search", "Search...", false, 14);
            ((RectTransform)_search.transform).Place(new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(200, 28));
            _search.onValueChanged.AddListener(_ => Refresh());
            var depAll = UIKit.Button(body, "DepositAll", "Deposit inventory", () => C.Send(ClientPackets.BankDepositAll()), 14);
            ((RectTransform)depAll.transform).Place(new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(160, 28));
            var content = UIKit.Scroll(body, "Items", out var scroll);
            ((RectTransform)scroll.transform).Stretch(0, 0, 34, 24);
            _grid = content;
            var g = content.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(48, 48);
            g.spacing = new Vector2(5, 5);
            g.padding = new RectOffset(4, 4, 4, 4);
            _info = UIKit.Text(body, "Info", "", 13, TextAnchor.MiddleLeft, UIKit.TextDim);
            _info.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(540, 20));
        }

        public void Refresh()
        {
            if (Root == null) return;
            var bank = C.Bank;
            while (_slots.Count < bank.Length)
            {
                var s = ItemSlotView.Create(_grid, "Bank", _slots.Count, 48);
                s.OnLeft = v => { if (v.ItemId != null) C.Send(ClientPackets.BankWithdraw(v.Index, 1)); };
                s.OnRight = WithdrawMenu;
                s.OnDropped = (from, to) => { if (from.Container == "Bank" && to.Container == "Bank") C.Send(ClientPackets.BankSwap(from.Index, to.Index)); };
                s.Tooltip = v => v.ItemId != null ? (Db.Item(v.ItemId)?.Name ?? v.ItemId) + " x" + v.Quantity.ToString("N0") : null;
                _slots.Add(s);
            }
            string filter = _search != null ? _search.text.Trim().ToLowerInvariant() : "";
            int used = 0;
            long value = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                bool exists = i < bank.Length;
                var slot = exists ? bank[i] : null;
                var def = slot != null && !slot.IsEmpty ? Db.Item(slot.ItemId) : null;
                bool show = exists && (filter.Length == 0 || (def != null && def.Name.ToLowerInvariant().Contains(filter)));
                _slots[i].gameObject.SetActive(show);
                if (exists) _slots[i].Set(slot.ItemId, slot.Quantity);
                if (def != null) { used++; value += (long)def.Value * slot.Quantity; }
            }
            _info.text = used + " / " + bank.Length + " slots used.  Estimated value " + UIKit.Gp(value) + " coins.  Left-click withdraws 1, right-click for more.";
        }

        private void WithdrawMenu(ItemSlotView v)
        {
            if (v.ItemId == null) return;
            int slot = v.Index;
            string name = "<color=#ff9a40>" + (Db.Item(v.ItemId)?.Name ?? v.ItemId) + "</color>";
            var items = new List<UIRoot.MenuItem>();
            foreach (var n in new[] { 1, 5, 10 }) { int q = n; items.Add(new UIRoot.MenuItem("Withdraw-" + q + " " + name, () => C.Send(ClientPackets.BankWithdraw(slot, q)))); }
            items.Add(new UIRoot.MenuItem("Withdraw-X " + name, () => UIRoot.Instance.Prompt("Withdraw how many?", "", true, s => { if (int.TryParse(s, out var q) && q > 0) C.Send(ClientPackets.BankWithdraw(slot, q)); })));
            items.Add(new UIRoot.MenuItem("Withdraw-All " + name, () => C.Send(ClientPackets.BankWithdraw(slot, int.MaxValue))));
            items.Add(new UIRoot.MenuItem("Cancel", null));
            UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", items);
        }
    }

    // ---------------------------------------------------------------------- trade
    public sealed class TradeWindow : GameWindow
    {
        private Text _title, _status, _myValue, _theirValue;
        private RectTransform _mine, _theirs;
        private Button _accept;
        private readonly List<ItemSlotView> _mySlots = new List<ItemSlotView>(), _theirSlots = new List<ItemSlotView>();

        protected override void Create()
        {
            var body = MakeWindow("Trade", new Vector2(560, 440));
            Root.anchoredPosition = new Vector2(-200, 40);
            _title = UIKit.Text(body, "Title", "", 16, TextAnchor.MiddleCenter, UIKit.TextGold);
            _title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(540, 22));
            UIKit.Text(body, "MineLabel", "Your offer", 15, TextAnchor.MiddleCenter).rectTransform.Place(new Vector2(0.25f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(260, 20));
            UIKit.Text(body, "TheirsLabel", "Their offer", 15, TextAnchor.MiddleCenter).rectTransform.Place(new Vector2(0.75f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(260, 20));
            _mine = Grid(body, 0.25f);
            _theirs = Grid(body, 0.75f);
            _myValue = UIKit.Text(body, "MyValue", "", 13, TextAnchor.MiddleCenter, UIKit.TextDim);
            _myValue.rectTransform.Place(new Vector2(0.25f, 0), new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(260, 20));
            _theirValue = UIKit.Text(body, "TheirValue", "", 13, TextAnchor.MiddleCenter, UIKit.TextDim);
            _theirValue.rectTransform.Place(new Vector2(0.75f, 0), new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(260, 20));
            _status = UIKit.Text(body, "Status", "", 15, TextAnchor.MiddleCenter);
            _status.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 42), new Vector2(540, 22));
            _accept = UIKit.Button(body, "Accept", "Accept", () => C.Send(ClientPackets.TradeAccept()), 17);
            ((RectTransform)_accept.transform).Place(new Vector2(0.35f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(150, 34));
            var decline = UIKit.Button(body, "Decline", "Decline", OnCloseRequested, 17);
            ((RectTransform)decline.transform).Place(new Vector2(0.65f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(150, 34));
        }

        private static RectTransform Grid(Transform parent, float x)
        {
            var bg = UIKit.Panel(parent, "Grid", UIKit.PanelInner, false);
            bg.rectTransform.Place(new Vector2(x, 1), new Vector2(0.5f, 1), new Vector2(0, -48), new Vector2(256, 260));
            var g = bg.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(44, 44);
            g.spacing = new Vector2(4, 4);
            g.padding = new RectOffset(6, 6, 6, 6);
            return bg.rectTransform;
        }

        private void Fill(List<ItemSlotView> slots, RectTransform grid, ClientSlot[] offer, bool mine, Text valueText)
        {
            while (slots.Count < offer.Length)
            {
                var s = ItemSlotView.Create(grid, mine ? "MyOffer" : "TheirOffer", slots.Count, 44);
                if (mine)
                {
                    s.OnLeft = v => { if (v.ItemId != null) C.Send(ClientPackets.TradeRemove(v.Index, 1)); };
                    s.OnRight = v =>
                    {
                        if (v.ItemId == null) return;
                        int idx = v.Index;
                        UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", new List<UIRoot.MenuItem>
                        {
                            new UIRoot.MenuItem("Remove-1", () => C.Send(ClientPackets.TradeRemove(idx, 1))),
                            new UIRoot.MenuItem("Remove-10", () => C.Send(ClientPackets.TradeRemove(idx, 10))),
                            new UIRoot.MenuItem("Remove-All", () => C.Send(ClientPackets.TradeRemove(idx, int.MaxValue))),
                            new UIRoot.MenuItem("Cancel", null),
                        });
                    };
                }
                s.Tooltip = v => v.ItemId != null ? (Db.Item(v.ItemId)?.Name ?? v.ItemId) + " x" + v.Quantity.ToString("N0") : null;
                slots.Add(s);
            }
            long value = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                bool on = i < offer.Length;
                slots[i].gameObject.SetActive(on);
                if (!on) continue;
                slots[i].Set(offer[i].ItemId, offer[i].Quantity);
                var d = offer[i].IsEmpty ? null : Db.Item(offer[i].ItemId);
                if (d != null) value += (long)d.Value * offer[i].Quantity;
            }
            valueText.text = "Value: " + UIKit.Gp(value) + " coins";
        }

        public void Refresh()
        {
            var t = C.Trade;
            if (t == null || Root == null) return;
            _title.text = "Trading with " + t.OtherName + (t.Stage == Embervale.Shared.Simulation.TradeStage.Confirm ? "  -  <color=#ff8060>CONFIRM: check both offers carefully</color>" : "");
            Fill(_mySlots, _mine, t.MyOffer, true, _myValue);
            Fill(_theirSlots, _theirs, t.TheirOffer, false, _theirValue);
            _status.text = t.MeAccepted && !t.ThemAccepted ? "Waiting for the other player..." :
                           t.ThemAccepted && !t.MeAccepted ? "<color=#80ff80>The other player has accepted.</color>" :
                           "They have " + t.TheirFreeSlots + " free inventory slots.";
            _accept.interactable = !t.MeAccepted;
        }
    }

    // ---------------------------------------------------------------------- crafting
    public sealed class CraftWindow : GameWindow
    {
        private RectTransform _list;
        private int _station;
        private Text _subtitle;

        protected override void Create()
        {
            var body = MakeWindow("What would you like to make?", new Vector2(520, 460));
            _subtitle = UIKit.Text(body, "Sub", "", 14, TextAnchor.MiddleCenter, UIKit.TextDim);
            _subtitle.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(500, 20));
            _list = UIKit.Scroll(body, "Recipes", out var scroll);
            ((RectTransform)scroll.transform).Stretch(0, 0, 24, 0);
            UIKit.VList(_list.gameObject, 4, 4);
        }

        /// <summary>stationType: a station type ("Anvil"), "Fire", or "item:a:b" for item-on-item crafting.</summary>
        public void Show(int stationId, string stationType)
        {
            _station = stationId;
            foreach (Transform t in _list) Object.Destroy(t.gameObject);
            IEnumerable<RecipeDef> recipes;
            if (stationType != null && stationType.StartsWith("item:"))
            {
                var parts = stationType.Split(':');
                string a = parts.Length > 1 ? parts[1] : null, b = parts.Length > 2 ? parts[2] : null;
                recipes = Db.Recipes.Values.Where(r => r.Station == null && Uses(r, a) && Uses(r, b));
                _subtitle.text = "Using " + (Db.Item(a)?.Name ?? a) + " with " + (Db.Item(b)?.Name ?? b);
            }
            else
            {
                recipes = Db.Recipes.Values.Where(r => r.StationMatches(stationType));
                _subtitle.text = stationType == "Fire" ? "Cooking on a fire" : "At the " + stationType;
            }
            var sorted = recipes.OrderBy(r => r.Skill).ThenBy(r => r.Level).ToList();
            if (sorted.Count == 0) UIKit.Text(_list, "None", "Nothing can be made here.", 15, TextAnchor.MiddleCenter).Size(30);
            foreach (var r in sorted) Row(r);
        }

        private static bool Uses(RecipeDef r, string item) =>
            item == null || r.Inputs.Any(i => i.Item == item) || (r.Tools != null && r.Tools.Contains(item));

        private int MaxMakeable(RecipeDef r)
        {
            int max = int.MaxValue;
            foreach (var i in r.Inputs) max = Mathf.Min(max, C.InventoryCount(i.Item) / Mathf.Max(1, i.Qty));
            foreach (var t in r.Tools) if (C.InventoryCount(t) == 0) return 0;
            return max == int.MaxValue ? 0 : max;
        }

        private void Row(RecipeDef r)
        {
            var output = r.Outputs.Count > 0 ? Db.Item(r.Outputs[0].Item) : null;
            int lvl = C.SkillLevel(r.Skill);
            int can = MaxMakeable(r);
            bool levelOk = lvl >= r.Level;
            var row = UIKit.Panel(_list, r.Id, can > 0 && levelOk ? UIKit.PanelInner : new Color(0.12f, 0.1f, 0.1f, 0.8f), false);
            row.Size(58);
            var icon = UIKit.Image(row.transform, "Icon", AssetRegistry.Instance.Icon(output?.Icon));
            icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(46, 46));
            string inputs = string.Join(", ", r.Inputs.Select(i => i.Qty + " " + (Db.Item(i.Item)?.Name ?? i.Item)));
            var name = UIKit.Text(row.transform, "Name",
                (output?.Name ?? r.Id) + (r.Outputs.Count > 0 && r.Outputs[0].Qty > 1 ? " x" + r.Outputs[0].Qty : "") +
                "  <size=12><color=" + (levelOk ? "#80ff80" : "#ff6060") + ">" + r.Skill + " " + r.Level + "</color>  <color=#c8c0b0>" + r.Xp + " xp</color></size>\n<size=12><color=#c8c0b0>" + inputs + "</color></size>", 15);
            name.rectTransform.Stretch(58, 170, 2, 2);
            string[] labels = { "1", "5", "X", "All" };
            for (int i = 0; i < labels.Length; i++)
            {
                string lab = labels[i];
                var b = UIKit.Button(row.transform, "Make" + lab, lab, () => Make(r, lab, can), 14);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6 - (3 - i) * 40, 0), new Vector2(36, 30));
                b.interactable = can > 0 && levelOk;
            }
        }

        private void Make(RecipeDef r, string label, int can)
        {
            void Go(int n) { C.Send(ClientPackets.Craft(r.Id, n, _station)); Hud.CloseWindow(this); }
            switch (label)
            {
                case "1": Go(1); break;
                case "5": Go(5); break;
                case "All": Go(Mathf.Max(1, can)); break;
                default: UIRoot.Instance.Prompt("Make how many?", can.ToString(), true, s => { if (int.TryParse(s, out var n) && n > 0) Go(n); }); break;
            }
        }

        protected override bool ServerSide => false;
    }

    // ---------------------------------------------------------------------- quest journal
    public sealed class QuestJournalWindow : GameWindow
    {
        private Text _text;
        protected override bool ServerSide => false;

        protected override void Create()
        {
            var body = MakeWindow("Quest Journal", new Vector2(480, 480));
            var content = UIKit.Scroll(body, "Journal", out _);
            UIKit.VList(content.gameObject, 4, 8);
            _text = UIKit.Text(content, "Text", "", 16, TextAnchor.UpperLeft);
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public void Show(string questId)
        {
            if (!Db.Quests.TryGetValue(questId, out var q)) return;
            C.Quests.TryGetValue(questId, out var st);
            var sb = new System.Text.StringBuilder();
            sb.Append("<size=22><color=#ffd040>").Append(q.Name).Append("</color></size>\n");
            sb.Append("<color=#c8c0b0>").Append(q.Description).Append("</color>\n\n");
            if (st == null)
            {
                sb.Append("<b>Requirements</b>\n");
                if (q.Requirements.Skills.Count == 0 && q.Requirements.Quests.Count == 0) sb.Append("None\n");
                foreach (var r in q.Requirements.Skills)
                    sb.Append("<color=").Append(C.SkillLevel(r.Skill) >= r.Level ? "#80ff80" : "#ff6060").Append(">").Append(r.Skill).Append(" ").Append(r.Level).Append("</color>\n");
                foreach (var rq in q.Requirements.Quests)
                {
                    bool done = C.Quests.TryGetValue(rq, out var s2) && s2.Completed;
                    sb.Append("<color=").Append(done ? "#80ff80" : "#ff6060").Append(">Complete ").Append(Db.Quests.TryGetValue(rq, out var d2) ? d2.Name : rq).Append("</color>\n");
                }
                sb.Append("\nFind the quest giver to begin.");
            }
            else
            {
                for (int i = 0; i < q.Stages.Count; i++)
                {
                    var stage = q.Stages[i];
                    bool done = st.Completed || i < st.Stage;
                    if (i > st.Stage && !st.Completed) break;
                    sb.Append(done ? "<color=#808080>" : "<color=#ffffff>").Append(stage.Journal).Append("</color>\n");
                    if (!done)
                        for (int k = 0; k < stage.Objectives.Count; k++)
                        {
                            var o = stage.Objectives[k];
                            int have = st.Counters != null && k < st.Counters.Length ? st.Counters[k] : 0;
                            string target = Db.Items.ContainsKey(o.Target ?? "") ? Db.Item(o.Target).Name : Db.Monsters.TryGetValue(o.Target ?? "", out var md) ? md.Name : Db.Npcs.TryGetValue(o.Target ?? "", out var nd) ? nd.Name : o.Target;
                            sb.Append("  - ").Append(o.Type).Append(" ").Append(target);
                            if (o.Count > 1) sb.Append(": ").Append(Mathf.Min(have, o.Count)).Append("/").Append(o.Count);
                            sb.Append("\n");
                        }
                    sb.Append("\n");
                }
                if (st.Completed) sb.Append("<color=#50ff50><b>QUEST COMPLETE</b></color>\n");
                sb.Append("\n<b>Rewards</b>\n").Append(q.QuestPoints).Append(" quest point(s)\n");
                foreach (var x in q.Rewards.Xp) sb.Append(x.Amount.ToString("N0")).Append(" ").Append(x.Skill).Append(" xp\n");
                foreach (var it in q.Rewards.Items) sb.Append(it.Qty).Append(" x ").Append(Db.Item(it.Item)?.Name ?? it.Item).Append("\n");
            }
            _text.text = sb.ToString();
        }
    }

    // ---------------------------------------------------------------------- world map
    public sealed class WorldMapWindow : GameWindow
    {
        private RectTransform _map, _marker;
        protected override bool ServerSide => false;

        protected override void Create()
        {
            var body = MakeWindow("World Map", new Vector2(760, 800));
            var img = UIKit.Node("Map", body).Stretch().gameObject.AddComponent<RawImage>();
            img.texture = Hud.Session.World.MapTexture;
            _map = img.rectTransform;
            var def = Db.World;
            foreach (var r in def.Regions)
            {
                var t = UIKit.Text(_map, r.Id, r.Name, 18, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.6f));
                t.fontStyle = FontStyle.Bold;
                Pin(t.rectTransform, (r.X0 + r.X1) / 2f, (r.Z0 + r.Z1) / 2f + 6, new Vector2(240, 26));
            }
            foreach (var kv in def.Locations)
            {
                if (kv.Key.StartsWith("spawn") || kv.Key.StartsWith("_")) continue;
                var dot = UIKit.Image(_map, kv.Key + "Dot", UIKit.Circle, new Color(1f, 0.85f, 0.3f));
                Pin(dot.rectTransform, kv.Value.X, kv.Value.Z, new Vector2(6, 6));
                var t = UIKit.Text(_map, kv.Key, Pretty(kv.Key), 12, TextAnchor.MiddleCenter, Color.white);
                Pin(t.rectTransform, kv.Value.X, kv.Value.Z - 3, new Vector2(160, 18));
            }
            var m = UIKit.Image(_map, "You", UIKit.Circle, new Color(1f, 1f, 1f));
            _marker = m.rectTransform;
            _marker.sizeDelta = new Vector2(10, 10);
            _marker.gameObject.AddComponent<Outline>().effectColor = Color.black;
            Update();
        }

        private static string Pretty(string id)
        {
            var s = id.Replace('_', ' ');
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        private void Pin(RectTransform rt, float x, float z, Vector2 size)
        {
            var w = Db.World;
            rt.anchorMin = rt.anchorMax = new Vector2(x / w.Width, z / w.Height);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        public override void Update()
        {
            var self = Hud.Session.SelfView;
            if (self != null && _marker != null) Pin(_marker, self.transform.position.x, self.transform.position.z, new Vector2(10, 10));
        }
    }
}
