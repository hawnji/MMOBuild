using System.Collections.Generic;
using System.Linq;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using Embervale.Shared.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>
    /// The tabbed panel in the bottom-right corner: combat options, skills, quests, inventory, equipment,
    /// prayers, spellbook, party and settings.
    /// </summary>
    public sealed class SidePanel
    {
        public enum Tab { Combat, Skills, Quests, Inventory, Equipment, Prayer, Magic, Party, Settings }

        private readonly Hud _hud;
        private readonly RectTransform _root, _body;
        private readonly Dictionary<Tab, RectTransform> _pages = new Dictionary<Tab, RectTransform>();
        private readonly Dictionary<Tab, Image> _tabButtons = new Dictionary<Tab, Image>();
        private Tab _current = Tab.Inventory;

        private GameClient C => _hud.Client;
        private GameDatabase Db => _hud.Db;

        // page state
        private readonly ItemSlotView[] _inv = new ItemSlotView[28];
        private readonly ItemSlotView[] _equip = new ItemSlotView[EquipSlots.Count];
        private Text _equipStats, _combatInfo, _skillsTotal, _questPoints, _partyText, _prayerPoints;
        private readonly Dictionary<Skill, Text> _skillLevels = new Dictionary<Skill, Text>();
        private readonly Dictionary<string, Image> _prayerIcons = new Dictionary<string, Image>();
        private readonly Dictionary<string, Image> _spellIcons = new Dictionary<string, Image>();
        private readonly Dictionary<MeleeStance, Image> _stanceButtons = new Dictionary<MeleeStance, Image>();
        private Toggle _retaliate;
        private RectTransform _questList;

        private static readonly (Tab tab, string icon, string label)[] TabDefs =
        {
            (Tab.Combat, "bronze_sword", "Combat"), (Tab.Skills, "skill_vitality", "Skills"), (Tab.Quests, "old_map_scrap", "Quests"),
            (Tab.Inventory, "coins", "Inventory"), (Tab.Equipment, "bronze_helm", "Equipment"), (Tab.Prayer, "skill_prayer", "Prayer"),
            (Tab.Magic, "skill_magic", "Magic"), (Tab.Party, "goblin_charm", "Party"), (Tab.Settings, "hammer", "Settings"),
        };

        public SidePanel(Hud hud)
        {
            _hud = hud;
            var panel = UIKit.Panel(hud.UI.HudLayer, "SidePanel");
            _root = panel.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(262, 460));
            for (int i = 0; i < TabDefs.Length; i++)
            {
                var def = TabDefs[i];
                int row = i / 5, col = i % 5;
                var b = UIKit.Panel(_root, "Tab" + def.tab, UIKit.ButtonColor, false);
                b.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(6 + col * 50, -6 - row * 42), new Vector2(46, 38));
                var icon = UIKit.Image(b.transform, "Icon", AssetRegistry.Instance.Icon(def.icon));
                icon.rectTransform.Stretch(5, 5, 3, 3);
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = b;
                var t = def.tab;
                btn.onClick.AddListener(() => Show(t));
                b.gameObject.AddComponent<TooltipSource>().Text = () => def.label;
                _tabButtons[def.tab] = b;
            }
            _body = UIKit.Node("Body", _root).Stretch(6, 6, 96, 6);
            BuildInventory();
            BuildEquipment();
            BuildSkills();
            BuildQuests();
            BuildCombat();
            BuildPrayer();
            BuildMagic();
            BuildParty();
            BuildSettings();

            C.OnInventoryChanged += () => { RefreshInventory(); RefreshMagic(); };
            C.OnEquipmentChanged += () => { RefreshEquipment(); RefreshCombat(); };
            C.OnSkillsChanged += () => { RefreshSkills(); RefreshPrayer(); RefreshMagic(); };
            C.OnStatsChanged += () => { RefreshCombat(); RefreshPrayer(); };
            C.OnQuestsChanged += RefreshQuests;
            C.OnPartyChanged += RefreshParty;
            RefreshAll();
            Show(Tab.Inventory);
        }

        public void RefreshAll()
        {
            RefreshInventory(); RefreshEquipment(); RefreshSkills(); RefreshQuests(); RefreshCombat(); RefreshPrayer(); RefreshMagic(); RefreshParty();
        }

        public void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); }

        public void Show(Tab t)
        {
            _current = t;
            foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == t);
            foreach (var kv in _tabButtons) kv.Value.color = kv.Key == t ? new Color(0.6f, 0.45f, 0.22f) : UIKit.ButtonColor;
        }

        public void Update() { }

        private RectTransform Page(Tab t)
        {
            var p = UIKit.Node("Page" + t, _body).Stretch();
            _pages[t] = p;
            p.gameObject.SetActive(false);
            return p;
        }

        // ------------------------------------------------------------------ inventory
        private void BuildInventory()
        {
            var page = Page(Tab.Inventory);
            const float size = 52, gap = 7;
            for (int i = 0; i < 28; i++)
            {
                var s = ItemSlotView.Create(page, "Inv", i, size);
                ((RectTransform)s.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(8 + (i % 4) * (size + gap), -4 - (i / 4) * (size + gap)), new Vector2(size, size));
                s.OnLeft = InventoryLeft;
                s.OnRight = InventoryRight;
                s.OnDropped = (from, to) =>
                {
                    if (from.Container == "Inv" && to.Container == "Inv") C.Send(ClientPackets.MoveItem(from.Index, to.Index));
                };
                s.Tooltip = v => ItemTooltip(v.ItemId, v.Quantity);
                _inv[i] = s;
            }
        }

        public string ItemTooltip(string itemId, int qty)
        {
            var def = itemId != null ? Db.Item(itemId) : null;
            if (def == null) return null;
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#ffb040><b>").Append(def.Name).Append("</b></color>");
            if (qty > 1) sb.Append(" x").Append(qty.ToString("N0"));
            if (!string.IsNullOrEmpty(def.Description)) sb.Append("\n<color=#d8d0c0>").Append(def.Description).Append("</color>");
            if (def.Stats != null)
            {
                var st = def.Stats;
                void S(string n, int v) { if (v != 0) sb.Append("\n").Append(n).Append(v > 0 ? " +" : " ").Append(v); }
                S("Melee attack", st.AttackMelee); S("Ranged attack", st.AttackRanged); S("Magic attack", st.AttackMagic);
                S("Melee strength", st.StrengthMelee); S("Ranged strength", st.StrengthRanged); S("Magic power", st.MagicPower);
                S("Melee defence", st.DefenseMelee); S("Ranged defence", st.DefenseRanged); S("Magic defence", st.DefenseMagic);
            }
            foreach (var r in def.Requirements)
            {
                bool ok = C.SkillLevel(r.Skill) >= r.Level;
                sb.Append("\n<color=").Append(ok ? "#80ff80" : "#ff6060").Append(">Requires ").Append(r.Skill).Append(" ").Append(r.Level).Append("</color>");
            }
            if (def.Heal > 0) sb.Append("\nHeals ").Append(def.Heal);
            sb.Append("\n<color=#c0a060>Value ").Append(UIKit.Gp(def.Value)).Append(" coins</color>");
            return sb.ToString();
        }

        public List<string> ItemOptions(ItemDef def)
        {
            var o = new List<string>();
            if (def.IsFood) o.Add("Eat");
            else if (def.ConsumeEffect != null) o.Add("Drink");
            if (def.IsEquippable) o.Add(def.Slot == EquipSlot.Weapon || def.Slot == EquipSlot.Shield ? "Wield" : "Wear");
            if (def.BuryXp > 0) o.Add("Bury");
            o.Add("Use");
            o.Add("Drop");
            o.Add("Examine");
            return o;
        }

        private void DoItemOption(int slot, string opt)
        {
            if (opt == "Use") { _hud.Session.Interaction.BeginUse(slot); return; }
            C.Send(ClientPackets.ItemOption(slot, opt));
        }

        private void InventoryLeft(ItemSlotView v)
        {
            var inter = _hud.Session.Interaction;
            if (inter.UseSlot >= 0)
            {
                if (inter.UseSlot != v.Index && v.ItemId != null) C.Send(ClientPackets.UseItemOnItem(inter.UseSlot, v.Index));
                inter.CancelUse();
                return;
            }
            if (v.ItemId == null) return;
            if (_hud.Find<BankWindow>() != null) { C.Send(ClientPackets.BankDeposit(v.Index, 1)); return; }
            if (_hud.Find<TradeWindow>() != null) { C.Send(ClientPackets.TradeOffer(v.Index, 1)); return; }
            if (_hud.Find<ShopWindow>() != null) { ShopValue(v); return; }
            var def = Db.Item(v.ItemId);
            if (def != null) DoItemOption(v.Index, ItemOptions(def)[0]);
        }

        private void ShopValue(ItemSlotView v)
        {
            var shop = C.Shop;
            var def = Db.Item(v.ItemId);
            if (shop == null || def == null) return;
            var entry = shop.Entries.FirstOrDefault(e => e.ItemId == v.ItemId);
            int price = entry != null ? entry.SellPrice : Mathf.FloorToInt(def.Value * 0.4f);
            _hud.Message(def.Name + ": the shop will buy this for " + price + " coins.");
        }

        private void InventoryRight(ItemSlotView v)
        {
            if (v.ItemId == null) return;
            var def = Db.Item(v.ItemId);
            if (def == null) return;
            int slot = v.Index;
            var items = new List<UIRoot.MenuItem>();
            string name = "<color=#ff9a40>" + def.Name + "</color>";
            if (_hud.Find<BankWindow>() != null)
            {
                foreach (var n in new[] { 1, 5, 10 }) { int q = n; items.Add(new UIRoot.MenuItem("Deposit-" + q + " " + name, () => C.Send(ClientPackets.BankDeposit(slot, q)))); }
                items.Add(new UIRoot.MenuItem("Deposit-X " + name, () => UIRoot.Instance.Prompt("Deposit how many?", "", true, s => { if (int.TryParse(s, out var q) && q > 0) C.Send(ClientPackets.BankDeposit(slot, q)); })));
                items.Add(new UIRoot.MenuItem("Deposit-All " + name, () => C.Send(ClientPackets.BankDeposit(slot, int.MaxValue))));
            }
            else if (_hud.Find<TradeWindow>() != null)
            {
                foreach (var n in new[] { 1, 5, 10 }) { int q = n; items.Add(new UIRoot.MenuItem("Offer-" + q + " " + name, () => C.Send(ClientPackets.TradeOffer(slot, q)))); }
                items.Add(new UIRoot.MenuItem("Offer-X " + name, () => UIRoot.Instance.Prompt("Offer how many?", "", true, s => { if (int.TryParse(s, out var q) && q > 0) C.Send(ClientPackets.TradeOffer(slot, q)); })));
                items.Add(new UIRoot.MenuItem("Offer-All " + name, () => C.Send(ClientPackets.TradeOffer(slot, int.MaxValue))));
            }
            else if (_hud.Find<ShopWindow>() != null)
            {
                items.Add(new UIRoot.MenuItem("Value " + name, () => ShopValue(v)));
                foreach (var n in new[] { 1, 5, 10, 50 }) { int q = n; items.Add(new UIRoot.MenuItem("Sell " + q + " " + name, () => C.Send(ClientPackets.ShopSell(slot, q)))); }
            }
            else
            {
                foreach (var o in ItemOptions(def)) { var opt = o; items.Add(new UIRoot.MenuItem(opt + " " + name, () => DoItemOption(slot, opt))); }
            }
            items.Add(new UIRoot.MenuItem("Cancel", null));
            UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", items);
        }

        private void RefreshInventory()
        {
            for (int i = 0; i < _inv.Length && i < C.Inventory.Length; i++)
            {
                _inv[i].Set(C.Inventory[i].ItemId, C.Inventory[i].Quantity);
                _inv[i].SetHighlight(_hud.Session.Interaction != null && _hud.Session.Interaction.UseSlot == i);
            }
        }

        // ------------------------------------------------------------------ equipment
        private static readonly (EquipSlot slot, Vector2 pos, string icon)[] EquipLayout =
        {
            (EquipSlot.Head, new Vector2(0, 0), "bronze_helm"), (EquipSlot.Cape, new Vector2(-1, 1), "wolf_pelt"), (EquipSlot.Amulet, new Vector2(0, 1), "jade_amulet"),
            (EquipSlot.Ammo, new Vector2(1, 1), "bronze_arrow"), (EquipSlot.Weapon, new Vector2(-1, 2), "bronze_sword"), (EquipSlot.Body, new Vector2(0, 2), "bronze_chest"),
            (EquipSlot.Shield, new Vector2(1, 2), "bronze_shield"), (EquipSlot.Legs, new Vector2(0, 3), "bronze_legs"), (EquipSlot.Hands, new Vector2(-1, 4), "bronze_gloves"),
            (EquipSlot.Feet, new Vector2(0, 4), "bronze_boots"), (EquipSlot.Ring, new Vector2(1, 4), "silver_ring"),
        };

        private void BuildEquipment()
        {
            var page = Page(Tab.Equipment);
            foreach (var e in EquipLayout)
            {
                var s = ItemSlotView.Create(page, "Equip", (int)e.slot, 46);
                ((RectTransform)s.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(e.pos.x * 58, -4 - e.pos.y * 52), new Vector2(46, 46));
                s.EmptySprite = AssetRegistry.Instance.Icon(e.icon);
                int slot = (int)e.slot;
                s.OnLeft = v => { if (v.ItemId != null) C.Send(ClientPackets.Unequip(slot)); };
                s.OnRight = v =>
                {
                    if (v.ItemId == null) return;
                    var def = Db.Item(v.ItemId);
                    UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", new List<UIRoot.MenuItem>
                    {
                        new UIRoot.MenuItem("Remove <color=#ff9a40>" + def?.Name + "</color>", () => C.Send(ClientPackets.Unequip(slot))),
                        new UIRoot.MenuItem("Examine <color=#ff9a40>" + def?.Name + "</color>", () => _hud.Message(def?.Description)),
                        new UIRoot.MenuItem("Cancel", null),
                    });
                };
                s.Tooltip = v => v.ItemId != null ? ItemTooltip(v.ItemId, v.Quantity) : e.slot + " slot";
                _equip[slot] = s;
            }
            _equipStats = UIKit.Text(page, "Stats", "", 13, TextAnchor.UpperLeft, UIKit.TextDim);
            _equipStats.rectTransform.Stretch(6, 6, 268, 0);
            _equipStats.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void RefreshEquipment()
        {
            var total = new ItemStats();
            for (int i = 0; i < _equip.Length; i++)
            {
                if (_equip[i] == null) continue;
                var s = C.Equipment[i];
                _equip[i].Set(s.ItemId, s.Quantity);
                var def = s.IsEmpty ? null : Db.Item(s.ItemId);
                if (def?.Stats != null) total.Add(def.Stats);
            }
            _equipStats.text =
                "<color=#ffd040>Attack</color>  Melee " + Sign(total.AttackMelee) + "  Range " + Sign(total.AttackRanged) + "  Magic " + Sign(total.AttackMagic) + "\n" +
                "<color=#ffd040>Defence</color> Melee " + Sign(total.DefenseMelee) + "  Range " + Sign(total.DefenseRanged) + "  Magic " + Sign(total.DefenseMagic) + "\n" +
                "<color=#ffd040>Strength</color> Melee " + Sign(total.StrengthMelee) + "  Range " + Sign(total.StrengthRanged) + "  Magic " + Sign(total.MagicPower);
        }

        private static string Sign(int v) => v >= 0 ? "+" + v : v.ToString();

        // ------------------------------------------------------------------ skills
        private void BuildSkills()
        {
            var page = Page(Tab.Skills);
            var skills = (Skill[])System.Enum.GetValues(typeof(Skill));
            for (int i = 0; i < skills.Length; i++)
            {
                var sk = skills[i];
                var cell = UIKit.Panel(page, "Skill" + sk, UIKit.PanelInner, false);
                cell.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(2 + (i % 3) * 83, -2 - (i / 3) * 44), new Vector2(80, 40));
                var icon = UIKit.Image(cell.transform, "Icon", AssetRegistry.Instance.Icon("skill_" + sk.ToString().ToLowerInvariant()));
                icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(32, 32));
                var lv = UIKit.Text(cell.transform, "Level", "1/1", 15, TextAnchor.MiddleRight, UIKit.TextGold);
                lv.rectTransform.Stretch(36, 4, 0, 0);
                _skillLevels[sk] = lv;
                cell.raycastTarget = true;
                cell.gameObject.AddComponent<TooltipSource>().Text = () => SkillTooltip(sk);
            }
            _skillsTotal = UIKit.Text(page, "Total", "", 16, TextAnchor.MiddleCenter, UIKit.TextGold);
            _skillsTotal.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(240, 24));
        }

        private string SkillTooltip(Skill sk)
        {
            long xp = C.SkillXp(sk);
            int lvl = C.SkillLevel(sk);
            var table = Db.XpTable;
            string s = "<b>" + sk + "</b>\nXP: " + xp.ToString("N0");
            if (lvl < table.MaxLevel)
            {
                long next = table.XpForLevel(lvl + 1);
                s += "\nNext level at: " + next.ToString("N0") + "\nRemaining: " + (next - xp).ToString("N0");
            }
            if (Db.SkillDefs.TryGetValue(sk, out var d) && !string.IsNullOrEmpty(d.Description)) s += "\n<color=#c8c0b0>" + d.Description + "</color>";
            return s;
        }

        private void RefreshSkills()
        {
            int total = 0;
            foreach (var kv in _skillLevels)
            {
                int lvl = C.SkillLevel(kv.Key);
                int boost = C.SkillBoosts[(int)kv.Key];
                total += lvl;
                string cur = boost > 0 ? "<color=#80ff80>" + (lvl + boost) + "</color>" : boost < 0 ? "<color=#ff6060>" + (lvl + boost) + "</color>" : lvl.ToString();
                kv.Value.text = cur + "/" + lvl;
            }
            _skillsTotal.text = "Total level: " + total;
        }

        // ------------------------------------------------------------------ quests
        private void BuildQuests()
        {
            var page = Page(Tab.Quests);
            _questPoints = UIKit.Text(page, "QP", "", 15, TextAnchor.MiddleCenter, UIKit.TextGold);
            _questPoints.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(240, 22));
            _questList = UIKit.Scroll(page, "List", out var scroll);
            ((RectTransform)scroll.transform).Stretch(0, 0, 24, 0);
            UIKit.VList(_questList.gameObject, 2, 4);
        }

        private void RefreshQuests()
        {
            foreach (Transform t in _questList) Object.Destroy(t.gameObject);
            _questPoints.text = "Quest points: " + C.QuestPoints;
            foreach (var q in Db.Quests.Values)
            {
                C.Quests.TryGetValue(q.Id, out var state);
                string col = state == null ? "#ff5050" : state.Completed ? "#50ff50" : "#ffff50";
                var qq = q;
                var b = UIKit.Button(_questList, q.Id, "<color=" + col + ">" + q.Name + "</color>", () => _hud.Open<QuestJournalWindow>().Show(qq.Id), 14);
                b.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                b.Size(24);
            }
        }

        // ------------------------------------------------------------------ combat
        private void BuildCombat()
        {
            var page = Page(Tab.Combat);
            _combatInfo = UIKit.Text(page, "Info", "", 15, TextAnchor.UpperCenter);
            _combatInfo.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(240, 60));
            var stances = new[] { (MeleeStance.Accurate, "Accurate", "Trains Attack"), (MeleeStance.Aggressive, "Aggressive", "Trains Strength"), (MeleeStance.Defensive, "Defensive", "Trains Defence") };
            for (int i = 0; i < stances.Length; i++)
            {
                var st = stances[i];
                var b = UIKit.Button(page, st.Item2, st.Item2 + "\n<size=12><color=#c8b898>" + st.Item3 + "</color></size>", () => C.Send(ClientPackets.SetStance(st.Item1)), 16);
                ((RectTransform)b.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -70 - i * 54), new Vector2(220, 48));
                _stanceButtons[st.Item1] = b.GetComponent<Image>();
            }
            _retaliate = UIKit.Toggle(page, "Retaliate", "Auto retaliate", true, on => { if (on != C.AutoRetaliate) C.Send(ClientPackets.SetAutoRetaliate(on)); });
            ((RectTransform)_retaliate.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -240), new Vector2(200, 26));
            var hint = UIKit.Text(page, "Hint", "Ranged and magic weapons train their own skill. Set a spell to autocast from the spellbook.", 13, TextAnchor.UpperCenter, UIKit.TextDim);
            hint.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -276), new Vector2(236, 60));
        }

        private void RefreshCombat()
        {
            var weapon = C.Equipment[(int)EquipSlot.Weapon];
            var def = weapon.IsEmpty ? null : Db.Item(weapon.ItemId);
            string auto = string.IsNullOrEmpty(C.Autocast) ? "" : "\nAutocast: " + (Db.Spells.TryGetValue(C.Autocast, out var sp) ? sp.Name : C.Autocast);
            _combatInfo.text = "<b>" + (def != null ? def.Name : "Unarmed") + "</b>\nCombat level " + C.CombatLevel + auto;
            foreach (var kv in _stanceButtons) kv.Value.color = kv.Key == C.Stance ? new Color(0.6f, 0.45f, 0.22f) : UIKit.ButtonColor;
            if (_retaliate.isOn != C.AutoRetaliate) _retaliate.SetIsOnWithoutNotify(C.AutoRetaliate);
        }

        // ------------------------------------------------------------------ prayer
        private void BuildPrayer()
        {
            var page = Page(Tab.Prayer);
            _prayerPoints = UIKit.Text(page, "Points", "", 15, TextAnchor.MiddleCenter, UIKit.TextGold);
            _prayerPoints.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(240, 22));
            int i = 0;
            foreach (var p in Db.Prayers.Values.OrderBy(p => p.Level))
            {
                var pr = p;
                var cell = UIKit.Panel(page, "Prayer" + p.Id, UIKit.PanelInner, false);
                cell.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(4 + (i % 4) * 62, -4 - (i / 4) * 62), new Vector2(56, 56));
                var icon = UIKit.Image(cell.transform, "Icon", AssetRegistry.Instance.Icon("prayer_" + p.Id) ?? AssetRegistry.Instance.Icon("skill_prayer"));
                icon.rectTransform.Stretch(6, 6, 4, 14);
                var lvl = UIKit.Text(cell.transform, "Lvl", p.Level.ToString(), 12, TextAnchor.LowerCenter, UIKit.TextDim);
                lvl.rectTransform.Stretch(0, 0, 0, 1);
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = cell;
                b.onClick.AddListener(() => C.Send(ClientPackets.TogglePrayer(pr.Id)));
                cell.gameObject.AddComponent<TooltipSource>().Text = () =>
                    "<b>" + pr.Name + "</b>\nLevel " + pr.Level + "\n" + string.Join("\n", pr.Effect.Select(e => e.Key + " +" + Mathf.RoundToInt((float)e.Value * 100) + "%")) + "\nDrain " + pr.DrainPerMinute + "/min";
                _prayerIcons[p.Id] = cell;
                i++;
            }
        }

        private void RefreshPrayer()
        {
            int lvl = C.SkillLevel(Skill.Prayer);
            foreach (var kv in _prayerIcons)
            {
                var def = Db.Prayers[kv.Key];
                bool active = C.ActivePrayers.Contains(kv.Key);
                kv.Value.color = active ? new Color(0.25f, 0.55f, 0.65f) : def.Level > lvl ? new Color(0.12f, 0.1f, 0.1f, 0.8f) : UIKit.PanelInner;
            }
            _prayerPoints.text = "Prayer: " + Mathf.CeilToInt(C.PrayerPoints) + " / " + lvl;
        }

        // ------------------------------------------------------------------ magic
        private void BuildMagic()
        {
            var page = Page(Tab.Magic);
            int i = 0;
            foreach (var s in Db.Spells.Values.OrderBy(s => s.Level))
            {
                var sp = s;
                var cell = UIKit.Panel(page, "Spell" + s.Id, UIKit.PanelInner, false);
                cell.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(4 + (i % 5) * 50, -4 - (i / 5) * 50), new Vector2(46, 46));
                var runeIcon = s.Runes.Count > 0 ? AssetRegistry.Instance.Icon(Db.Item(s.Runes[s.Runes.Count - 1].Item)?.Icon) : null;
                var icon = UIKit.Image(cell.transform, "Icon", runeIcon ?? AssetRegistry.Instance.Icon("skill_magic"));
                icon.rectTransform.Stretch(5, 5, 5, 5);
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = cell;
                b.onClick.AddListener(() => CastFromBook(sp));
                var rc = cell.gameObject.AddComponent<RightClick>();
                rc.Action = () =>
                {
                    var items = new List<UIRoot.MenuItem> { new UIRoot.MenuItem("Cast <color=#40e0ff>" + sp.Name + "</color>", () => CastFromBook(sp)) };
                    if (sp.Type == SpellType.Attack)
                    {
                        items.Add(new UIRoot.MenuItem("Autocast <color=#40e0ff>" + sp.Name + "</color>", () => C.Send(ClientPackets.SetAutocast(sp.Id))));
                        if (!string.IsNullOrEmpty(C.Autocast)) items.Add(new UIRoot.MenuItem("Clear autocast", () => C.Send(ClientPackets.SetAutocast(""))));
                    }
                    items.Add(new UIRoot.MenuItem("Cancel", null));
                    UIRoot.Instance.ShowMenu(InputAdapter.MousePosition, "Choose Option", items);
                };
                cell.gameObject.AddComponent<TooltipSource>().Text = () => SpellTooltip(sp);
                _spellIcons[s.Id] = cell;
                i++;
            }
        }

        private void CastFromBook(SpellDef sp)
        {
            if (sp.Type == SpellType.Attack) _hud.Session.Interaction.BeginSpell(sp.Id);
            else C.Send(ClientPackets.CastSpell(sp.Id, C.SelfId));
        }

        private string SpellTooltip(SpellDef sp)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(sp.Name).Append("</b>  (level ").Append(sp.Level).Append(")");
            if (sp.MaxHit > 0) sb.Append("\nMax hit ").Append(sp.MaxHit);
            if (sp.Heal > 0) sb.Append("\nHeals ").Append(sp.Heal);
            if (sp.Type == SpellType.Teleport && sp.Destination != null) sb.Append("\nTeleports to ").Append(sp.Destination);
            foreach (var r in sp.Runes)
            {
                int have = C.InventoryCount(r.Item);
                sb.Append("\n<color=").Append(have >= r.Qty ? "#80ff80" : "#ff6060").Append(">").Append(r.Qty).Append(" x ").Append(Db.Item(r.Item)?.Name ?? r.Item).Append(" (").Append(have).Append(")</color>");
            }
            return sb.ToString();
        }

        private void RefreshMagic()
        {
            int lvl = C.SkillLevel(Skill.Magic);
            foreach (var kv in _spellIcons)
            {
                var sp = Db.Spells[kv.Key];
                bool canCast = sp.Level <= lvl && sp.Runes.All(r => C.InventoryCount(r.Item) >= r.Qty);
                kv.Value.color = C.Autocast == sp.Id ? new Color(0.25f, 0.45f, 0.65f) : canCast ? UIKit.PanelInner : new Color(0.1f, 0.09f, 0.09f, 0.7f);
            }
        }

        // ------------------------------------------------------------------ party
        private void BuildParty()
        {
            var page = Page(Tab.Party);
            _partyText = UIKit.Text(page, "Members", "", 15, TextAnchor.UpperLeft);
            _partyText.rectTransform.Stretch(6, 6, 4, 130);
            _partyText.verticalOverflow = VerticalWrapMode.Overflow;
            var invite = UIKit.Button(page, "Invite", "Invite player", () => UIRoot.Instance.Prompt("Invite which player?", "", false, n => { if (!string.IsNullOrWhiteSpace(n)) C.Send(ClientPackets.PartyInvite(n.Trim())); }));
            ((RectTransform)invite.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 86), new Vector2(200, 32));
            var accept = UIKit.Button(page, "Accept", "Accept invite", () => C.Send(ClientPackets.PartyAccept()));
            ((RectTransform)accept.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 48), new Vector2(200, 32));
            var leave = UIKit.Button(page, "Leave", "Leave party", () => C.Send(ClientPackets.PartyLeave()));
            ((RectTransform)leave.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(200, 32));
        }

        private void RefreshParty()
        {
            if (C.Party.Count == 0) { _partyText.text = "<color=#c8c0b0>You are not in a party.\n\nParties share kill credit for quests and let you chat with /p.</color>"; return; }
            var sb = new System.Text.StringBuilder("<b>Party</b>\n");
            foreach (var m in C.Party)
                sb.Append(m.Leader ? "<color=#ffd040>* </color>" : "  ").Append(m.Name).Append("  (cb ").Append(m.CombatLevel).Append(")  ")
                  .Append("<color=#ff6060>").Append(m.Hp).Append("/").Append(m.MaxHp).Append("</color>\n");
            _partyText.text = sb.ToString();
        }

        // ------------------------------------------------------------------ settings
        private void BuildSettings()
        {
            var page = Page(Tab.Settings);
            float y = -4;
            void SliderRow(string label, float v, System.Action<float> set)
            {
                UIKit.Text(page, label, label, 14, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, y), new Vector2(100, 22));
                var s = UIKit.Slider(page, label + "Slider", v, set);
                ((RectTransform)s.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, y - 1), new Vector2(140, 20));
                y -= 26;
            }
            void ToggleRow(string label, bool v, System.Action<bool> set)
            {
                var t = UIKit.Toggle(page, label, label, v, set);
                ((RectTransform)t.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, y), new Vector2(240, 22));
                y -= 26;
            }
            SliderRow("Master", Settings.MasterVolume, v => Settings.MasterVolume = v);
            SliderRow("Music", Settings.MusicVolume, v => Settings.MusicVolume = v);
            SliderRow("Effects", Settings.SfxVolume, v => Settings.SfxVolume = v);
            SliderRow("Ambience", Settings.AmbientVolume, v => Settings.AmbientVolume = v);
            SliderRow("UI scale", (Settings.UiScale - 0.75f) / 0.75f, v => Settings.UiScale = 0.75f + v * 0.75f);
            SliderRow("Camera", Settings.CameraSensitivity / 2f, v => Settings.CameraSensitivity = Mathf.Max(0.2f, v * 2f));
            ToggleRow("Shadows", Settings.Shadows, v => Settings.Shadows = v);
            ToggleRow("V-Sync", Settings.VSync, v => Settings.VSync = v);
            ToggleRow("XP drops", Settings.ShowXpDrops, v => Settings.ShowXpDrops = v);
            ToggleRow("Name tags", Settings.ShowNames, v => Settings.ShowNames = v);
            ToggleRow("Hide roofs when inside", Settings.ShowRoofs, v => Settings.ShowRoofs = v);
            var fps = UIKit.Button(page, "Fps", "Frame cap: " + Settings.TargetFps, null, 14);
            ((RectTransform)fps.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y - 4), new Vector2(200, 28));
            fps.onClick.AddListener(() =>
            {
                int[] opts = { 30, 60, 120, 144, -1 };
                int idx = System.Array.IndexOf(opts, Settings.TargetFps);
                Settings.TargetFps = opts[(idx + 1) % opts.Length];
                fps.SetLabel("Frame cap: " + (Settings.TargetFps < 0 ? "none" : Settings.TargetFps.ToString()));
            });
            var logout = UIKit.Button(page, "Logout", "Log out", () => _hud.Session.Logout(), 16);
            ((RectTransform)logout.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(200, 34));
        }
    }

    /// <summary>Invokes an action on right click (uGUI buttons only handle left click).</summary>
    public sealed class RightClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public System.Action Action;
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) Action?.Invoke();
        }
    }
}
