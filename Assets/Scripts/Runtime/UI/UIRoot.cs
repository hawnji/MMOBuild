using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>
    /// Owns the screen-space canvas, the front-end screens (login / characters) and the in-game HUD, plus the
    /// shared widgets every screen uses: hover text, tooltips, the right-click menu and status messages.
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }

        public struct MenuItem
        {
            public string Label;
            public Action Action;
            public MenuItem(string label, Action action) { Label = label; Action = action; }
        }

        public Canvas Canvas { get; private set; }
        public RectTransform OverheadLayer { get; private set; }
        public RectTransform HudLayer { get; private set; }
        public RectTransform WindowLayer { get; private set; }
        public RectTransform TopLayer { get; private set; }
        public Hud Hud { get; private set; }

        private GameSession _session;
        private CanvasScaler _scaler;
        private FrontEnd _front;
        private Text _hover, _cursorHint;
        private RectTransform _tooltip;
        private Text _tooltipText;
        private RectTransform _menu;

        public static bool PointerOverUI
        {
            get
            {
                var es = EventSystem.current;
                return es != null && es.IsPointerOverGameObject();
            }
        }

        public static bool IsTyping
        {
            get
            {
                var es = EventSystem.current;
                if (es == null || es.currentSelectedGameObject == null) return false;
                var f = es.currentSelectedGameObject.GetComponent<InputField>();
                return f != null && f.isFocused;
            }
        }

        public static UIRoot Create(GameSession session)
        {
            var go = new GameObject("UI", typeof(RectTransform));
            go.transform.SetParent(session.transform, false);
            var ui = go.AddComponent<UIRoot>();
            ui.Build(session);
            return ui;
        }

        private void Build(GameSession session)
        {
            Instance = this;
            _session = session;
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1600, 900);
            _scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            ApplyScale();
            Settings.OnChanged += ApplyScale;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.transform.SetParent(session.transform, false);
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                // Created at runtime, so it has no actions asset yet: give it the package's default UI actions,
                // otherwise it receives no pointer input and nothing on the canvas is clickable.
                var module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                module.AssignDefaultActions();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            OverheadLayer = UIKit.Node("Overhead", transform).Stretch();
            HudLayer = UIKit.Node("Hud", transform).Stretch();
            WindowLayer = UIKit.Node("Windows", transform).Stretch();
            TopLayer = UIKit.Node("Top", transform).Stretch();

            _hover = UIKit.Text(TopLayer, "HoverText", "", 17, TextAnchor.UpperLeft);
            _hover.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -8), new Vector2(900, 26));
            _cursorHint = UIKit.Text(TopLayer, "CursorHint", "", 14, TextAnchor.LowerLeft, UIKit.TextGold);
            _cursorHint.rectTransform.sizeDelta = new Vector2(400, 22);
            _cursorHint.rectTransform.pivot = new Vector2(0, 0);

            var tip = UIKit.Panel(TopLayer, "Tooltip", new Color(0.08f, 0.07f, 0.06f, 0.96f));
            _tooltip = tip.rectTransform;
            _tooltip.pivot = new Vector2(0, 1);
            tip.raycastTarget = false;
            _tooltipText = UIKit.Text(tip.transform, "Text", "", 14, TextAnchor.UpperLeft);
            _tooltipText.rectTransform.Stretch(8, 8, 6, 6);
            _tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            tip.gameObject.SetActive(false);

            _front = new FrontEnd(this, session);
            session.OnStateChanged += OnState;
        }

        private void OnDestroy() => Settings.OnChanged -= ApplyScale;

        private void ApplyScale()
        {
            if (_scaler != null) _scaler.referenceResolution = new Vector2(1600, 900) / Settings.UiScale;
        }

        private void OnState(ClientState s)
        {
            CloseMenu();
            HideTooltip();
            _front.Show(s);
        }

        public void SetStatus(string text) => _front?.SetStatus(text);

        public void EnterWorld()
        {
            _front.Show(ClientState.InWorld);
            Hud = new GameObject("HudController").AddComponent<Hud>();
            Hud.transform.SetParent(transform, false);
            Hud.Init(this, _session);
            _session.Audio.Bind(_session.Client);
        }

        public void LeaveWorld()
        {
            if (Hud != null) { Hud.Teardown(); Destroy(Hud.gameObject); }
            Hud = null;
            foreach (Transform t in WindowLayer) Destroy(t.gameObject);
            foreach (Transform t in OverheadLayer) Destroy(t.gameObject);
            SetHover(null);
            SetCursorHint(null);
        }

        // ------------------------------------------------------------------ hover / tooltip
        public void SetHover(string text)
        {
            if (_hover == null) return;
            _hover.text = text ?? "";
        }

        public void SetCursorHint(string text)
        {
            if (_cursorHint != null) _cursorHint.text = text ?? "";
        }

        public void ShowTooltip(string text)
        {
            _tooltipText.text = text;
            var size = new Vector2(Mathf.Min(320, _tooltipText.preferredWidth + 18), 0);
            _tooltip.sizeDelta = new Vector2(size.x, 10);
            Canvas.ForceUpdateCanvases();
            _tooltip.sizeDelta = new Vector2(size.x, _tooltipText.preferredHeight + 14);
            _tooltip.gameObject.SetActive(true);
            _tooltip.SetAsLastSibling();
        }

        public void HideTooltip()
        {
            if (_tooltip != null) _tooltip.gameObject.SetActive(false);
        }

        private Vector2 ToCanvas(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out var local);
            var rect = ((RectTransform)transform).rect;
            return local - rect.min;
        }

        private void Update()
        {
            var mouse = InputAdapter.MousePosition;
            var pos = ToCanvas(mouse);
            var size = ((RectTransform)transform).rect.size;
            if (_tooltip.gameObject.activeSelf)
            {
                var p = pos + new Vector2(16, -16);
                p.x = Mathf.Min(p.x, size.x - _tooltip.sizeDelta.x - 4);
                p.y = Mathf.Max(p.y, _tooltip.sizeDelta.y + 4);
                _tooltip.anchorMin = _tooltip.anchorMax = Vector2.zero;
                _tooltip.anchoredPosition = p;
            }
            if (!string.IsNullOrEmpty(_cursorHint.text))
            {
                _cursorHint.rectTransform.anchorMin = _cursorHint.rectTransform.anchorMax = Vector2.zero;
                _cursorHint.rectTransform.anchoredPosition = pos + new Vector2(18, 8);
            }
            if (_menu != null && (InputAdapter.MouseDown(0) || InputAdapter.MouseDown(1)))
            {
                // Clicking outside the menu closes it (clicks on menu buttons are handled first by uGUI).
                if (!RectTransformUtility.RectangleContainsScreenPoint(_menu, mouse, null)) CloseMenu();
            }
            if (InputAdapter.Down(InputAdapter.Key.Escape))
            {
                if (_menu != null) CloseMenu();
                else Hud?.CloseTopWindow();
            }
        }

        // ------------------------------------------------------------------ context menu
        public void ShowMenu(Vector2 screenPos, string title, List<MenuItem> items)
        {
            CloseMenu();
            if (items == null || items.Count == 0) return;
            var panel = UIKit.Panel(TopLayer, "ContextMenu", new Color(0.1f, 0.09f, 0.08f, 0.97f));
            _menu = panel.rectTransform;
            _menu.anchorMin = _menu.anchorMax = Vector2.zero;
            _menu.pivot = new Vector2(0, 1);
            const float rowH = 22f;
            float width = 160;
            var header = UIKit.Text(_menu, "Title", title, 15, TextAnchor.MiddleLeft, UIKit.TextGold);
            header.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -2), new Vector2(300, rowH));
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = UIKit.Node("Item" + i, _menu);
                row.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(2, -(i + 1) * rowH - 2), new Vector2(0, rowH));
                var img = row.gameObject.AddComponent<Image>();
                img.color = new Color(1, 1, 1, 0);
                var b = row.gameObject.AddComponent<Button>();
                var cb = b.colors;
                cb.highlightedColor = new Color(1, 0.85f, 0.4f, 0.25f);
                cb.normalColor = new Color(1, 1, 1, 0);
                b.colors = cb;
                b.targetGraphic = img;
                b.onClick.AddListener(() => { CloseMenu(); it.Action?.Invoke(); });
                var t = UIKit.Text(row, "Label", it.Label, 15);
                t.rectTransform.Stretch(6, 0, 0, 0);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                width = Mathf.Max(width, t.preferredWidth + 20);
            }
            foreach (RectTransform r in _menu) if (r.name.StartsWith("Item")) r.sizeDelta = new Vector2(width - 4, rowH);
            var size = new Vector2(width, (items.Count + 1) * rowH + 6);
            _menu.sizeDelta = size;
            var pos = ToCanvas(screenPos) + new Vector2(-width * 0.4f, 6);
            var canvasSize = ((RectTransform)transform).rect.size;
            pos.x = Mathf.Clamp(pos.x, 2, canvasSize.x - size.x - 2);
            pos.y = Mathf.Clamp(pos.y, size.y + 2, canvasSize.y - 2);
            _menu.anchoredPosition = pos;
        }

        private int _menuClosedFrame = -1;

        /// <summary>True while a context menu is open or was closed this frame (that click belongs to the menu).</summary>
        public bool MenuBlocksClick => _menu != null || _menuClosedFrame == Time.frameCount;

        public void CloseMenu()
        {
            if (_menu != null) { Destroy(_menu.gameObject); _menuClosedFrame = Time.frameCount; }
            _menu = null;
        }

        /// <summary>Simple modal prompt for a number or text (e.g. "Withdraw X", "Enter amount").</summary>
        public void Prompt(string title, string initial, bool numeric, Action<string> onOk)
        {
            var body = UIKit.Window(TopLayer, title, new Vector2(320, 130), null, out var root);
            var field = UIKit.Input(body, "Field", "", false, 18);
            ((RectTransform)field.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(290, 34));
            if (numeric) field.contentType = InputField.ContentType.IntegerNumber;
            field.text = initial ?? "";
            void Ok() { var v = field.text; Destroy(root); onOk(v); }
            var ok = UIKit.Button(body, "Ok", "OK", Ok);
            ((RectTransform)ok.transform).Place(new Vector2(0.3f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(110, 30));
            var cancel = UIKit.Button(body, "Cancel", "Cancel", () => Destroy(root));
            ((RectTransform)cancel.transform).Place(new Vector2(0.7f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(110, 30));
            field.onEndEdit.AddListener(v => { if (InputAdapter.Down(InputAdapter.Key.Enter) && root != null) Ok(); });
            field.Select();
            field.ActivateInputField();
        }
    }
}
