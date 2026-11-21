using System.Collections.Generic;
using Embervale.Shared.Data;
using Embervale.Shared.Net;
using Embervale.Shared.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>
    /// In-game interface: status orbs, minimap, chat, side panel tabs, XP drops, overhead names / health bars /
    /// hit splats, level-up banner, and the interface windows the server opens (bank, shop, trade, dialogue,
    /// crafting). Everything reads from the GameClient mirror; nothing here changes game state directly.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public UIRoot UI { get; private set; }
        public GameSession Session { get; private set; }
        public GameClient Client => Session.Client;
        public GameDatabase Db => Session.Db;

        private Image _hpFill, _prayerFill, _runFill;
        private Text _hpText, _prayerText, _runText;
        private RawImage _minimap;
        private RectTransform _minimapDots, _compass;
        private readonly List<Image> _dotPool = new List<Image>();
        private Text _regionText, _pingText;

        private ChatBox _chat;
        private SidePanel _side;
        private RectTransform _xpDrops;
        private Text _levelBanner;
        private float _levelBannerTimer;

        private readonly Dictionary<int, Overhead> _overheads = new Dictionary<int, Overhead>();
        private readonly List<Splat> _splats = new List<Splat>();
        private readonly List<GameWindow> _windows = new List<GameWindow>();

        public void Init(UIRoot ui, GameSession session)
        {
            UI = ui;
            Session = session;
            BuildOrbsAndMap();
            _chat = new ChatBox(this);
            _side = new SidePanel(this);
            _xpDrops = UIKit.Node("XpDrops", UI.HudLayer).Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-270, -60), new Vector2(160, 300));
            _levelBanner = UIKit.Text(UI.HudLayer, "LevelBanner", "", 30, TextAnchor.MiddleCenter, UIKit.TextGold);
            _levelBanner.rectTransform.Place(new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 90));
            _levelBanner.verticalOverflow = VerticalWrapMode.Overflow;

            var c = Client;
            c.OnStatsChanged += RefreshOrbs;
            c.OnHitSplat += OnHitSplat;
            c.OnXpDrop += OnXpDrop;
            c.OnLevelUp += OnLevelUp;
            c.OnChat += OnChatBubble;
            c.OnDialogue += d => Open<DialogueWindow>().Show(d);
            c.OnDialogueClosed += () => Close<DialogueWindow>();
            c.OnShop += s => Open<ShopWindow>().Refresh();
            c.OnShopClosed += () => Close<ShopWindow>();
            c.OnBankOpened += () => { Open<BankWindow>().Refresh(); _side.Show(SidePanel.Tab.Inventory); };
            c.OnBankClosed += () => Close<BankWindow>();
            c.OnBankChanged += () => Find<BankWindow>()?.Refresh();
            c.OnInventoryChanged += () => { Find<MarketWindow>()?.Refresh(); Find<BankWindow>()?.Refresh(); Find<ShopWindow>()?.Refresh(); Find<TradeWindow>()?.Refresh(); };
            c.OnCraftMenu += (station, type) => Open<CraftWindow>().Show(station, type);
            c.OnMarketOpened += () => { Open<MarketWindow>().Refresh(); _side.Show(SidePanel.Tab.Inventory); };
            c.OnMarketClosed += () => Close<MarketWindow>();
            c.OnMarketOrders += () => Find<MarketWindow>()?.Refresh();
            c.OnMarketQuote += q => Find<MarketWindow>()?.ShowQuote(q);
            c.OnTradeRequest += (id, name) => _chat.Add("<color=#ff80ff>" + name + " wishes to trade with you. Right-click them and choose Trade to accept.</color>");
            c.OnTrade += t => Open<TradeWindow>().Refresh();
            c.OnTradeClosed += reason => { Close<TradeWindow>(); if (!string.IsNullOrEmpty(reason)) _chat.Add(reason); };
            RefreshOrbs();
        }

        public void Teardown()
        {
            foreach (var w in _windows) w.Destroy();
            _windows.Clear();
            foreach (var o in _overheads.Values) if (o.Root != null) Destroy(o.Root.gameObject);
            _overheads.Clear();
            _chat?.Destroy();
            _side?.Destroy();
        }

        // ------------------------------------------------------------------ windows
        public T Find<T>() where T : GameWindow
        {
            foreach (var w in _windows) if (w is T t && w.Root != null) return t;
            return null;
        }

        public T Open<T>() where T : GameWindow, new()
        {
            var w = Find<T>();
            if (w == null)
            {
                w = new T();
                w.Build(this);
                _windows.Add(w);
            }
            w.Root.SetAsLastSibling();
            Session.Audio.PlayUi("ui_open");
            return w;
        }

        public void Close<T>() where T : GameWindow
        {
            var w = Find<T>();
            if (w == null) return;
            _windows.Remove(w);
            w.Destroy();
        }

        public void CloseWindow(GameWindow w)
        {
            _windows.Remove(w);
            w.Destroy();
        }

        /// <summary>Escape: closes the most recent window (and tells the server for server-side interfaces).</summary>
        public void CloseTopWindow()
        {
            if (_windows.Count == 0) { _side.Show(SidePanel.Tab.Settings); return; }
            var w = _windows[_windows.Count - 1];
            w.OnCloseRequested();
        }

        public void ToggleWorldMap()
        {
            if (Find<WorldMapWindow>() != null) Close<WorldMapWindow>();
            else Open<WorldMapWindow>();
        }

        public void Message(string text) => _chat.Add(text);

        // ------------------------------------------------------------------ orbs + minimap
        private Image Orb(string name, Vector2 pos, Color fill, out Text label, System.Action onClick)
        {
            var bg = UIKit.Image(UI.HudLayer, name, UIKit.Circle, new Color(0.08f, 0.07f, 0.06f, 0.95f));
            bg.rectTransform.Place(new Vector2(1, 1), new Vector2(0.5f, 0.5f), pos, new Vector2(52, 52));
            var ring = bg.gameObject.AddComponent<Outline>();
            ring.effectColor = UIKit.Trim;
            ring.effectDistance = new Vector2(2, -2);
            var f = UIKit.Image(bg.transform, "Fill", UIKit.Circle, fill);
            f.rectTransform.Stretch(4, 4, 4, 4);
            f.type = Image.Type.Filled;
            f.fillMethod = Image.FillMethod.Vertical;
            f.fillOrigin = 0;
            f.preserveAspect = false;
            label = UIKit.Text(bg.transform, "Value", "", 16, TextAnchor.MiddleCenter, Color.white);
            label.rectTransform.Stretch();
            label.fontStyle = FontStyle.Bold;
            if (onClick != null)
            {
                bg.raycastTarget = true;
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() => onClick());
            }
            return f;
        }

        private void BuildOrbsAndMap()
        {
            var frame = UIKit.Panel(UI.HudLayer, "MinimapFrame", new Color(0.1f, 0.09f, 0.08f, 0.95f));
            frame.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -10), new Vector2(214, 214));
            var maskGo = UIKit.Image(frame.transform, "Mask", UIKit.Circle, Color.white);
            maskGo.rectTransform.Stretch(7, 7, 7, 7);
            maskGo.preserveAspect = false;
            maskGo.raycastTarget = true;
            var mask = maskGo.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            _minimap = UIKit.Node("Map", maskGo.transform).gameObject.AddComponent<RawImage>();
            _minimap.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(282, 282));
            _minimap.texture = Session.World.MapTexture;
            _minimap.raycastTarget = false;
            _minimapDots = UIKit.Node("Dots", maskGo.transform).Stretch();
            var self = UIKit.Image(maskGo.transform, "Self", UIKit.Circle, Color.white);
            self.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7, 7));
            _compass = UIKit.Node("Compass", frame.transform).Place(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(18, -18), new Vector2(30, 30));
            var cbg = UIKit.Image(_compass, "Bg", UIKit.Circle, new Color(0.15f, 0.12f, 0.1f));
            cbg.rectTransform.Stretch();
            var n = UIKit.Text(_compass, "N", "N", 15, TextAnchor.MiddleCenter, UIKit.Bad);
            n.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 2), new Vector2(20, 18));

            // Clicking the minimap walks there.
            var click = maskGo.gameObject.AddComponent<MinimapClick>();
            click.Hud = this;

            var worldMapBtn = UIKit.Button(UI.HudLayer, "WorldMapButton", "Map", ToggleWorldMap, 13);
            ((RectTransform)worldMapBtn.transform).Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -228), new Vector2(54, 24));
            _regionText = UIKit.Text(UI.HudLayer, "Region", "", 14, TextAnchor.MiddleRight, UIKit.TextGold);
            _regionText.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-72, -228), new Vector2(200, 24));
            _pingText = UIKit.Text(UI.HudLayer, "Ping", "", 12, TextAnchor.MiddleRight, UIKit.TextDim);
            _pingText.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -252), new Vector2(260, 18));

            _hpFill = Orb("HpOrb", new Vector2(-258, -40), new Color(0.75f, 0.12f, 0.1f), out _hpText, null);
            _prayerFill = Orb("PrayerOrb", new Vector2(-262, -98), new Color(0.3f, 0.75f, 0.95f), out _prayerText, () => _side.Show(SidePanel.Tab.Prayer));
            _runFill = Orb("RunOrb", new Vector2(-250, -156), new Color(0.85f, 0.75f, 0.3f), out _runText, () => Client.Send(ClientPackets.ToggleRun(!Client.Running)));
            foreach (var t in new[] { _hpFill, _prayerFill, _runFill }) t.transform.parent.gameObject.AddComponent<TooltipSource>();
            _hpFill.transform.parent.GetComponent<TooltipSource>().Text = () => "Hitpoints " + Client.Hp + " / " + Client.MaxHp + (Client.Poison > 0 ? "\n<color=#60ff60>Poisoned</color>" : "");
            _prayerFill.transform.parent.GetComponent<TooltipSource>().Text = () => "Prayer points " + Mathf.CeilToInt(Client.PrayerPoints) + " / " + Client.SkillLevel(Skill.Prayer);
            _runFill.transform.parent.GetComponent<TooltipSource>().Text = () => "Run energy " + Mathf.FloorToInt(Client.RunEnergy) + "%\nClick to " + (Client.Running ? "walk" : "run");
            if (Session.Environment != null) Session.Environment.OnRegionChanged += r =>
            {
                _regionText.text = r.Name;
                ShowBanner("<size=22>Entering</size>\n" + r.Name, 3f);
            };
        }

        private void RefreshOrbs()
        {
            var c = Client;
            if (c == null) return;
            _hpFill.fillAmount = c.MaxHp > 0 ? Mathf.Clamp01(c.Hp / (float)c.MaxHp) : 0;
            _hpFill.color = c.Poison > 0 ? new Color(0.2f, 0.65f, 0.15f) : new Color(0.75f, 0.12f, 0.1f);
            _hpText.text = c.Hp.ToString();
            int maxPrayer = Mathf.Max(1, c.SkillLevel(Skill.Prayer));
            _prayerFill.fillAmount = Mathf.Clamp01(c.PrayerPoints / maxPrayer);
            _prayerText.text = Mathf.CeilToInt(c.PrayerPoints).ToString();
            _prayerFill.color = c.ActivePrayers.Count > 0 ? new Color(0.55f, 0.95f, 1f) : new Color(0.3f, 0.75f, 0.95f);
            _runFill.fillAmount = Mathf.Clamp01(c.RunEnergy / 100f);
            _runText.text = Mathf.FloorToInt(c.RunEnergy).ToString();
            _runFill.color = c.Running ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.5f, 0.35f);
        }

        public void MinimapClicked(Vector2 local, Vector2 size)
        {
            var self = Session.SelfView;
            if (self == null) return;
            // Map is drawn rotated by the camera yaw; undo that to get the world offset.
            const float tilesAcross = 70f;
            var v = new Vector2(local.x / size.x, local.y / size.y) * tilesAcross;
            float yaw = Session.Camera != null ? Session.Camera.Yaw : 0;
            var rot = Quaternion.Euler(0, 0, -yaw) * new Vector3(v.x, v.y, 0);
            var p = self.transform.position + new Vector3(rot.x, 0, rot.y);
            Session.Interaction?.WalkTo(p);
        }

        private void UpdateMinimap()
        {
            var self = Session.SelfView;
            if (self == null || _minimap.texture == null) return;
            var pos = self.transform.position;
            float w = Session.World.Terrain.Width, h = Session.World.Terrain.Height;
            const float tilesAcross = 70f;
            float yaw = Session.Camera != null ? Session.Camera.Yaw : 0;
            // RawImage is larger than the mask so the rotated square always covers the circle.
            float span = tilesAcross * (282f / 200f);
            _minimap.uvRect = new Rect((pos.x - span / 2) / w, (pos.z - span / 2) / h, span / w, span / h);
            _minimap.rectTransform.localRotation = Quaternion.Euler(0, 0, yaw);
            _compass.localRotation = Quaternion.Euler(0, 0, yaw);

            int used = 0;
            float scale = 200f / tilesAcross;
            var rot = Quaternion.Euler(0, 0, yaw);
            foreach (var v in Session.Entities.Views.Values)
            {
                if (v == null || v.IsLocalPlayer || v.Snapshot == null) continue;
                var s = v.Snapshot;
                Color col;
                switch (s.Kind)
                {
                    case EntityKind.Player: col = Color.white; break;
                    case EntityKind.Monster: if (s.Has(EntitySnapshot.FlagDead)) continue; col = new Color(1f, 0.9f, 0.1f); break;
                    case EntityKind.Npc: col = new Color(1f, 1f, 0.3f); break;
                    case EntityKind.GroundItem: col = new Color(1f, 0.2f, 0.15f); break;
                    default: continue;
                }
                var d = new Vector3(s.X - pos.x, s.Z - pos.z, 0);
                if (d.sqrMagnitude > (tilesAcross / 2) * (tilesAcross / 2)) continue;
                var p = rot * d * scale;
                Image dot;
                if (used < _dotPool.Count) dot = _dotPool[used];
                else
                {
                    dot = UIKit.Image(_minimapDots, "Dot", UIKit.Circle, col);
                    dot.rectTransform.sizeDelta = new Vector2(5, 5);
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    _dotPool.Add(dot);
                }
                dot.gameObject.SetActive(true);
                dot.color = col;
                dot.rectTransform.anchoredPosition = new Vector2(p.x, p.y);
                used++;
            }
            for (int i = used; i < _dotPool.Count; i++) _dotPool[i].gameObject.SetActive(false);
            _pingText.text = Session.Offline ? "Offline world" : "Ping " + Client.LastPingMs + " ms";
        }

        // ------------------------------------------------------------------ overheads
        private sealed class Overhead
        {
            public RectTransform Root;
            public Text Name;
            public Image HpBg, HpFill;
            public Text Bubble;
            public float BubbleTimer, HpTimer;
        }

        private sealed class Splat
        {
            public int Entity;
            public RectTransform Root;
            public float Age;
            public Vector2 Offset;
        }

        private Overhead GetOverhead(EntityView v)
        {
            int id = v.Snapshot.Id;
            if (_overheads.TryGetValue(id, out var o) && o.Root != null) return o;
            o = new Overhead();
            o.Root = UIKit.Node("Overhead" + id, UI.OverheadLayer);
            o.Root.anchorMin = o.Root.anchorMax = Vector2.zero;
            o.Root.sizeDelta = new Vector2(200, 60);
            o.Name = UIKit.Text(o.Root, "Name", "", 14, TextAnchor.LowerCenter);
            o.Name.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(240, 20));
            o.HpBg = UIKit.Image(o.Root, "HpBg", null, new Color(0.7f, 0.05f, 0.05f, 0.9f));
            o.HpBg.preserveAspect = false;
            o.HpBg.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(46, 6));
            o.HpFill = UIKit.Image(o.HpBg.transform, "Fill", null, new Color(0.1f, 0.85f, 0.15f));
            o.HpFill.preserveAspect = false;
            o.HpFill.rectTransform.anchorMin = Vector2.zero;
            o.HpFill.rectTransform.anchorMax = new Vector2(1, 1);
            o.HpFill.rectTransform.pivot = new Vector2(0, 0.5f);
            o.HpFill.rectTransform.offsetMin = o.HpFill.rectTransform.offsetMax = Vector2.zero;
            o.Bubble = UIKit.Text(o.Root, "Bubble", "", 15, TextAnchor.LowerCenter, new Color(1f, 1f, 0.3f));
            o.Bubble.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(320, 44));
            _overheads[id] = o;
            return o;
        }

        private void UpdateOverheads()
        {
            var cam = Session.Camera != null ? Session.Camera.Cam : null;
            if (cam == null) return;
            var canvasScale = UI.Canvas.scaleFactor;
            var seen = new HashSet<int>();
            var selfPos = Session.SelfView != null ? Session.SelfView.transform.position : Vector3.zero;
            foreach (var v in Session.Entities.Views.Values)
            {
                if (v == null || v.Snapshot == null || !v.IsActor) continue;
                var s = v.Snapshot;
                bool inCombat = s.Has(EntitySnapshot.FlagInCombat) && s.MaxHp > 0;
                bool showName = Settings.ShowNames && (s.Kind != EntityKind.Monster || inCombat) && !v.IsLocalPlayer;
                _overheads.TryGetValue(s.Id, out var existing);
                bool bubble = existing != null && existing.BubbleTimer > 0;
                bool hpRecent = existing != null && existing.HpTimer > 0;
                if (!showName && !inCombat && !bubble && !hpRecent) { if (existing != null) existing.Root.gameObject.SetActive(false); continue; }
                if ((v.transform.position - selfPos).sqrMagnitude > 45 * 45) { if (existing != null) existing.Root.gameObject.SetActive(false); continue; }
                var sp = cam.WorldToScreenPoint(v.transform.position + Vector3.up * v.OverheadHeight);
                if (sp.z < 0) { if (existing != null) existing.Root.gameObject.SetActive(false); continue; }
                var o = GetOverhead(v);
                seen.Add(s.Id);
                o.Root.gameObject.SetActive(true);
                o.Root.anchoredPosition = new Vector2(sp.x, sp.y) / canvasScale - new Vector2(0, 0);
                o.Name.text = showName ? (s.Kind == EntityKind.Player ? s.Name + " <color=#c0c0c0>(" + s.Level + ")</color>" : s.Name) : "";
                o.Name.color = s.Kind == EntityKind.Npc ? new Color(1f, 0.9f, 0.4f) : Color.white;
                bool showHp = (inCombat || hpRecent) && s.MaxHp > 0 && !s.Has(EntitySnapshot.FlagDead);
                o.HpBg.gameObject.SetActive(showHp);
                if (showHp) o.HpFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(s.Hp / (float)s.MaxHp), 1);
                if (o.BubbleTimer > 0) { o.BubbleTimer -= Time.deltaTime; if (o.BubbleTimer <= 0) o.Bubble.text = ""; }
                if (o.HpTimer > 0) o.HpTimer -= Time.deltaTime;
            }
            var remove = new List<int>();
            foreach (var kv in _overheads)
                if (!Session.Entities.Views.ContainsKey(kv.Key)) { if (kv.Value.Root != null) Destroy(kv.Value.Root.gameObject); remove.Add(kv.Key); }
            foreach (var id in remove) _overheads.Remove(id);

            for (int i = _splats.Count - 1; i >= 0; i--)
            {
                var sp = _splats[i];
                sp.Age += Time.deltaTime;
                var v = Session.Entities.Get(sp.Entity);
                if (sp.Age > 1.2f || v == null || sp.Root == null)
                {
                    if (sp.Root != null) Destroy(sp.Root.gameObject);
                    _splats.RemoveAt(i);
                    continue;
                }
                var p = cam.WorldToScreenPoint(v.transform.position + Vector3.up * (v.OverheadHeight * 0.6f));
                sp.Root.gameObject.SetActive(p.z > 0);
                sp.Root.anchoredPosition = new Vector2(p.x, p.y) / canvasScale + sp.Offset + new Vector2(0, sp.Age * 18f);
                float a = sp.Age < 0.9f ? 1f : 1f - (sp.Age - 0.9f) / 0.3f;
                foreach (var g in sp.Root.GetComponentsInChildren<Graphic>()) { var c = g.color; c.a = a; g.color = c; }
            }
        }

        private void OnHitSplat(int victim, int attacker, int damage, bool crit, string kind)
        {
            var v = Session.Entities.Get(victim);
            if (v == null) return;
            var o = GetOverhead(v);
            o.HpTimer = 6f;
            var root = UIKit.Node("Splat", UI.OverheadLayer);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.sizeDelta = new Vector2(34, 34);
            Color col = kind == "miss" ? new Color(0.2f, 0.4f, 0.85f) : kind == "poison" ? new Color(0.2f, 0.65f, 0.15f) : crit ? new Color(1f, 0.55f, 0f) : new Color(0.8f, 0.1f, 0.08f);
            var bg = UIKit.Image(root, "Bg", UIKit.Circle, col);
            bg.rectTransform.Stretch();
            var t = UIKit.Text(root, "Value", kind == "miss" ? "0" : damage.ToString(), crit ? 19 : 16, TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.Stretch(-10, -10, 0, 0);
            t.fontStyle = FontStyle.Bold;
            int n = 0;
            foreach (var s in _splats) if (s.Entity == victim) n++;
            _splats.Add(new Splat { Entity = victim, Root = root, Offset = new Vector2((n % 3 - 1) * 26, (n / 3) * 20) });
            if (victim == Client.SelfId || attacker == Client.SelfId) Session.Camera?.SendMessage("OnHit", SendMessageOptions.DontRequireReceiver);
        }

        private void OnChatBubble(ChatChannel ch, int id, string name, string text)
        {
            if (ch != ChatChannel.Local) return;
            var v = Session.Entities.Get(id);
            if (v == null) return;
            var o = GetOverhead(v);
            o.Bubble.text = text;
            o.BubbleTimer = 5f;
        }

        // ------------------------------------------------------------------ xp + levels
        private void OnXpDrop(Skill skill, int amount)
        {
            if (!Settings.ShowXpDrops || amount <= 0) return;
            var row = UIKit.Node("Drop", _xpDrops);
            row.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(160, 26));
            var icon = UIKit.Image(row, "Icon", AssetRegistry.Instance.Icon("skill_" + skill.ToString().ToLowerInvariant()));
            icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(22, 22));
            var t = UIKit.Text(row, "Amount", "+" + amount + " xp", 16, TextAnchor.MiddleLeft, Color.white);
            t.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(66, 0), new Vector2(100, 24));
            row.gameObject.AddComponent<FloatUp>().Speed = 45f;
        }

        private void OnLevelUp(Skill skill, int level)
        {
            ShowBanner("<color=#ffffff>Congratulations!</color>\nYour " + skill + " level is now " + level + ".", 4f);
            _chat.Add("<color=#ffd040>Congratulations, you just advanced a " + skill + " level. You are now level " + level + ".</color>");
            _side.RefreshAll();
        }

        public void ShowBanner(string text, float seconds)
        {
            _levelBanner.text = text;
            _levelBannerTimer = seconds;
            _levelBanner.color = UIKit.TextGold;
        }

        // ------------------------------------------------------------------ loop
        private void LateUpdate()
        {
            if (Session.Entities == null) return;
            UpdateMinimap();
            UpdateOverheads();
            if (_levelBannerTimer > 0)
            {
                _levelBannerTimer -= Time.deltaTime;
                var c = _levelBanner.color;
                c.a = Mathf.Clamp01(_levelBannerTimer);
                _levelBanner.color = c;
                if (_levelBannerTimer <= 0) _levelBanner.text = "";
            }
            _chat.Update();
            _side.Update();
            foreach (var w in _windows.ToArray()) w.Update();

            if (!UIRoot.IsTyping)
            {
                if (InputAdapter.Down(InputAdapter.Key.M)) ToggleWorldMap();
                if (InputAdapter.Down(InputAdapter.Key.F1)) _side.Show(SidePanel.Tab.Inventory);
                if (InputAdapter.Down(InputAdapter.Key.F2)) _side.Show(SidePanel.Tab.Equipment);
                if (InputAdapter.Down(InputAdapter.Key.F3)) _side.Show(SidePanel.Tab.Skills);
                if (InputAdapter.Down(InputAdapter.Key.F4)) _side.Show(SidePanel.Tab.Prayer);
                if (InputAdapter.Down(InputAdapter.Key.F5)) _side.Show(SidePanel.Tab.Magic);
                if (InputAdapter.Down(InputAdapter.Key.Enter)) _chat.Focus();
            }
        }
    }

    public sealed class FloatUp : MonoBehaviour
    {
        public float Speed = 40f, Life = 2.2f;
        private float _t;
        private Graphic[] _g;
        private void Update()
        {
            _t += Time.deltaTime;
            ((RectTransform)transform).anchoredPosition += new Vector2(0, Speed * Time.deltaTime);
            if (_g == null) _g = GetComponentsInChildren<Graphic>();
            float a = _t < Life - 0.5f ? 1f : (Life - _t) / 0.5f;
            foreach (var g in _g) { var c = g.color; c.a = Mathf.Clamp01(a); g.color = c; }
            if (_t >= Life) Destroy(gameObject);
        }
    }

    public sealed class MinimapClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public Hud Hud;
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left) return;
            var rt = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local))
                Hud.MinimapClicked(local, rt.rect.size);
        }
    }

    /// <summary>Chat log with channel filters and an input line. "/" commands are sent to the server.</summary>
    public sealed class ChatBox
    {
        private readonly Hud _hud;
        private readonly RectTransform _root;
        private readonly Text _log;
        private readonly InputField _input;
        private readonly ScrollRect _scroll;
        private readonly List<(int filter, string text)> _lines = new List<(int, string)>();
        private int _filter; // 0 all, 1 game, 2 public, 3 party/private
        private bool _dirty;
        private ChatChannel _sendChannel = ChatChannel.Local;

        public ChatBox(Hud hud)
        {
            _hud = hud;
            var panel = UIKit.Panel(hud.UI.HudLayer, "Chat", new Color(0.1f, 0.09f, 0.08f, 0.78f));
            _root = panel.rectTransform.Place(Vector2.zero, Vector2.zero, new Vector2(8, 8), new Vector2(560, 210));
            var content = UIKit.Scroll(_root, "Log", out _scroll);
            ((RectTransform)_scroll.transform).Stretch(4, 4, 28, 38);
            _scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            _log = UIKit.Text(content, "Text", "", 15, TextAnchor.LowerLeft);
            _log.verticalOverflow = VerticalWrapMode.Overflow;
            _log.rectTransform.anchorMin = new Vector2(0, 1);
            _log.rectTransform.anchorMax = new Vector2(1, 1);
            _log.rectTransform.pivot = new Vector2(0.5f, 1);
            _log.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.gameObject.AddComponent<VerticalLayoutGroup>().childControlHeight = true;

            string[] tabs = { "All", "Game", "Public", "Party/PM" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(_root, "Tab" + i, tabs[i], () => { _filter = idx; _dirty = true; }, 13);
                ((RectTransform)b.transform).Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(4 + i * 82, -3), new Vector2(78, 22));
            }
            var chan = UIKit.Button(_root, "Channel", "Say", null, 13);
            ((RectTransform)chan.transform).Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 4), new Vector2(64, 30));
            chan.onClick.AddListener(() =>
            {
                _sendChannel = _sendChannel == ChatChannel.Local ? ChatChannel.Party : _sendChannel == ChatChannel.Party ? ChatChannel.Trade : ChatChannel.Local;
                chan.SetLabel(_sendChannel == ChatChannel.Local ? "Say" : _sendChannel == ChatChannel.Party ? "Party" : "Trade");
            });
            _input = UIKit.Input(_root, "Input", "Press Enter to chat. /w name msg, /party invite name, /pos", false, 15);
            ((RectTransform)_input.transform).Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(72, 4), new Vector2(480, 30));
            _input.characterLimit = 200;
            _input.onEndEdit.AddListener(Submit);

            var c = hud.Client;
            c.OnGameMessage += m => Add(m, 1);
            c.OnChat += (ch, id, name, text) =>
            {
                string col = ch == ChatChannel.Party ? "#60c8ff" : ch == ChatChannel.Private ? "#ff80ff" : ch == ChatChannel.Trade ? "#ffb060" : ch == ChatChannel.System ? "#ffd040" : "#ffffff";
                string prefix = ch == ChatChannel.Party ? "[Party] " : ch == ChatChannel.Private ? "[PM] " : ch == ChatChannel.Trade ? "[Trade] " : "";
                Add("<color=" + col + ">" + prefix + "<b>" + Escape(name) + ":</b> " + Escape(text) + "</color>", ch == ChatChannel.Local || ch == ChatChannel.Trade ? 2 : ch == ChatChannel.System ? 1 : 3);
            };
            Add("<color=#ffd040>Welcome to Embervale.</color> Left-click to walk or act, right-click for options. Q/E rotate, wheel zooms, M opens the map.", 1);
            if (c.Motd != null) Add(c.Motd, 1);
        }

        private static string Escape(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        public void Add(string text, int filter = 1)
        {
            _lines.Add((filter, text));
            if (_lines.Count > 200) _lines.RemoveAt(0);
            _dirty = true;
        }

        public void Focus()
        {
            _input.Select();
            _input.ActivateInputField();
        }

        private void Submit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { _input.text = ""; return; }
            if (!InputAdapter.Down(InputAdapter.Key.Enter) && !InputAdapter.Held(InputAdapter.Key.Enter)) return;
            _hud.Client.Send(ClientPackets.Chat(_sendChannel, text.Trim()));
            _input.text = "";
            _input.DeactivateInputField();
        }

        public void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            var sb = new System.Text.StringBuilder();
            int start = Mathf.Max(0, _lines.Count - 80);
            for (int i = start; i < _lines.Count; i++)
            {
                if (_filter != 0 && _lines[i].filter != _filter) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(_lines[i].text);
            }
            _log.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0;
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
