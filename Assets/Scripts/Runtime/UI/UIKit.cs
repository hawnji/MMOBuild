using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>
    /// Code-built uGUI helpers and the shared visual theme (dark stone panels with brass trim). All game UI is
    /// constructed from these so the look stays consistent and no prefab wiring is needed.
    /// </summary>
    public static class UIKit
    {
        public static readonly Color PanelBg = new Color(0.13f, 0.115f, 0.1f, 0.94f);
        public static readonly Color PanelInner = new Color(0.2f, 0.18f, 0.155f, 0.95f);
        public static readonly Color Trim = new Color(0.62f, 0.5f, 0.3f, 1f);
        public static readonly Color TextMain = new Color(1f, 0.93f, 0.78f);
        public static readonly Color TextDim = new Color(0.75f, 0.7f, 0.6f);
        public static readonly Color TextGold = new Color(1f, 0.82f, 0.3f);
        public static readonly Color ButtonColor = new Color(0.32f, 0.26f, 0.19f, 1f);
        public static readonly Color ButtonHover = new Color(0.45f, 0.36f, 0.24f, 1f);
        public static readonly Color SlotBg = new Color(0.09f, 0.08f, 0.07f, 0.85f);
        public static readonly Color Good = new Color(0.4f, 0.9f, 0.4f);
        public static readonly Color Bad = new Color(0.95f, 0.35f, 0.3f);

        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        private static Sprite _rounded, _circle, _slot;

        /// <summary>9-sliced rounded rectangle generated at runtime (no editor assets required).</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded == null) _rounded = MakeRounded(32, 8, 1.5f);
                return _rounded;
            }
        }

        public static Sprite SlotSprite
        {
            get
            {
                if (_slot == null) _slot = MakeRounded(24, 5, 1.2f);
                return _slot;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    const int n = 64;
                    var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d)));
                        }
                    t.Apply();
                    _circle = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
                }
                return _circle;
            }
        }

        private static Sprite MakeRounded(int size, int radius, float soft)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((radius - d) / soft)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        // ------------------------------------------------------------------ layout helpers
        public static RectTransform Rect(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            return rt != null ? rt : go.AddComponent<RectTransform>();
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors at a normalised point with a pixel size and offset.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color? color = null, bool trim = true)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = Rounded;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = color ?? PanelBg;
            if (trim)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Trim;
                o.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return img;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color? color = null)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = sprite;
            img.color = color ?? Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string name, string text, int size = 16, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null, bool shadow = true)
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? TextMain;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0, 0, 0, 0.85f);
                s.effectDistance = new Vector2(1, -1);
            }
            return t;
        }

        public static Button Button(Transform parent, string name, string label, UnityAction onClick, int size = 16)
        {
            var img = Panel(parent, name, ButtonColor);
            var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.3f, 1.25f, 1.15f);
            cb.pressedColor = new Color(0.8f, 0.75f, 0.7f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            cb.colorMultiplier = 1.4f;
            b.colors = cb;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => GameSession.Instance?.Audio?.PlayUi("click"));
            var t = Text(img.transform, "Label", label, size, TextAnchor.MiddleCenter);
            t.rectTransform.Stretch(4, 4, 2, 2);
            return b;
        }

        public static void SetLabel(this Button b, string s)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = s;
        }

        public static InputField Input(Transform parent, string name, string placeholder, bool password = false, int size = 16)
        {
            var bg = Panel(parent, name, SlotBg);
            var field = bg.gameObject.AddComponent<InputField>();
            var txt = Text(bg.transform, "Text", "", size, TextAnchor.MiddleLeft, Color.white, false);
            txt.rectTransform.Stretch(8, 8, 2, 2);
            txt.supportRichText = false;
            var ph = Text(bg.transform, "Placeholder", placeholder, size, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.35f), false);
            ph.rectTransform.Stretch(8, 8, 2, 2);
            ph.fontStyle = FontStyle.Italic;
            field.textComponent = txt;
            field.placeholder = ph;
            field.targetGraphic = bg;
            if (password) { field.contentType = InputField.ContentType.Password; field.asteriskChar = '*'; }
            return field;
        }

        public static Slider Slider(Transform parent, string name, float value, Action<float> onChange)
        {
            var root = Node(name, parent);
            var bg = Panel(root, "Background", SlotBg, false);
            bg.rectTransform.Stretch(0, 0, 7, 7);
            var fillArea = Node("Fill Area", root).Stretch(4, 12, 7, 7);
            var fill = Panel(fillArea, "Fill", Trim, false);
            fill.rectTransform.Stretch();
            var handleArea = Node("Handle Area", root).Stretch(8, 8, 0, 0);
            var handle = Image(handleArea, "Handle", Circle, TextGold);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(18, 0);
            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = 0; s.maxValue = 1;
            s.value = value;
            s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        public static Toggle Toggle(Transform parent, string name, string label, bool on, Action<bool> onChange)
        {
            var root = Node(name, parent);
            var box = Panel(root, "Box", SlotBg);
            box.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(20, 20));
            var check = Image(box.transform, "Check", Circle, TextGold);
            check.rectTransform.Stretch(4, 4, 4, 4);
            var t = Text(root, "Label", label, 15);
            t.rectTransform.Stretch(28, 0, 0, 0);
            var tg = root.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            tg.graphic = check;
            tg.targetGraphic = box;
            tg.isOn = on;
            tg.onValueChanged.AddListener(v => onChange(v));
            return tg;
        }

        /// <summary>Vertical scroll view; returns the content transform (use a layout group on it).</summary>
        public static RectTransform Scroll(Transform parent, string name, out ScrollRect scroll)
        {
            var bg = Panel(parent, name, PanelInner, false);
            scroll = bg.gameObject.AddComponent<ScrollRect>();
            var viewport = Node("Viewport", bg.transform).Stretch(4, 4, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            return content;
        }

        public static VerticalLayoutGroup VList(GameObject go, float spacing = 4, int pad = 4)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement Size(this Component c, float height = -1, float width = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (height >= 0) le.preferredHeight = height;
            if (width >= 0) le.preferredWidth = width;
            return le;
        }

        /// <summary>A movable window with title bar and close button. Returns the body area.</summary>
        public static RectTransform Window(Transform parent, string title, Vector2 size, Action onClose, out GameObject root)
        {
            var panel = Panel(parent, title + "Window");
            root = panel.gameObject;
            panel.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var bar = Panel(panel.transform, "TitleBar", new Color(0.25f, 0.2f, 0.14f, 1f), false);
            bar.rectTransform.anchorMin = new Vector2(0, 1);
            bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1);
            bar.rectTransform.offsetMin = new Vector2(3, -33);
            bar.rectTransform.offsetMax = new Vector2(-3, -3);
            bar.gameObject.AddComponent<DragWindow>().Target = panel.rectTransform;
            var t = Text(bar.transform, "Title", title, 18, TextAnchor.MiddleCenter, TextGold);
            t.rectTransform.Stretch(30, 30, 0, 0);
            if (onClose != null)
            {
                var close = Button(bar.transform, "Close", "X", () => onClose(), 15);
                ((RectTransform)close.transform).Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-3, 0), new Vector2(26, 24));
            }
            var body = Node("Body", panel.transform).Stretch(8, 8, 40, 8);
            return body;
        }

        public static string FormatQty(int q)
        {
            if (q >= 10_000_000) return "<color=#40ff80>" + (q / 1_000_000) + "M</color>";
            if (q >= 100_000) return "<color=#ffffff>" + (q / 1000) + "K</color>";
            return "<color=#ffff40>" + q + "</color>";
        }

        public static string Gp(long v) => v >= 1_000_000 ? (v / 1_000_000.0).ToString("0.#") + "M" : v >= 10_000 ? (v / 1000) + "K" : v.ToString("N0");
    }

    /// <summary>Lets a window be dragged by its title bar.</summary>
    public sealed class DragWindow : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        public RectTransform Target;
        public void OnBeginDrag(PointerEventData e) => Target.SetAsLastSibling();
        public void OnDrag(PointerEventData e)
        {
            var canvas = Target.GetComponentInParent<Canvas>();
            float s = canvas != null ? canvas.scaleFactor : 1f;
            Target.anchoredPosition += e.delta / s;
        }
    }

    /// <summary>Tooltip trigger for any UI element.</summary>
    public sealed class TooltipSource : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Text;
        public void OnPointerEnter(PointerEventData e) { var t = Text?.Invoke(); if (!string.IsNullOrEmpty(t)) UIRoot.Instance.ShowTooltip(t); }
        public void OnPointerExit(PointerEventData e) => UIRoot.Instance.HideTooltip();
        private void OnDisable() { if (UIRoot.Instance != null) UIRoot.Instance.HideTooltip(); }
    }

    /// <summary>
    /// An item slot (inventory, bank, equipment, shop, trade). Shows the icon and quantity, supports
    /// left click, right click (context menu), hover tooltips and drag-and-drop between slots.
    /// </summary>
    public sealed class ItemSlotView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public string Container;
        public int Index;
        public string ItemId;
        public int Quantity;
        public Image Bg, Icon;
        public Text Qty;
        public Sprite EmptySprite;
        public Action<ItemSlotView> OnLeft, OnRight;
        public Action<ItemSlotView, ItemSlotView> OnDropped; // (from, to)
        public Func<ItemSlotView, string> Tooltip;

        private static GameObject _ghost;
        private static ItemSlotView _dragFrom;

        public static ItemSlotView Create(Transform parent, string container, int index, float size)
        {
            var bg = UIKit.Panel(parent, container + "Slot" + index, UIKit.SlotBg, false);
            bg.sprite = UIKit.SlotSprite;
            bg.rectTransform.sizeDelta = new Vector2(size, size);
            var v = bg.gameObject.AddComponent<ItemSlotView>();
            v.Container = container;
            v.Index = index;
            v.Bg = bg;
            v.Icon = UIKit.Image(bg.transform, "Icon", null);
            v.Icon.rectTransform.Stretch(3, 3, 3, 3);
            v.Icon.enabled = false;
            v.Qty = UIKit.Text(bg.transform, "Qty", "", 13, TextAnchor.UpperLeft);
            v.Qty.rectTransform.Stretch(3, 0, 1, 0);
            return v;
        }

        public void Set(string itemId, int qty)
        {
            ItemId = string.IsNullOrEmpty(itemId) || qty <= 0 ? null : itemId;
            Quantity = ItemId == null ? 0 : qty;
            var db = GameSession.Instance.Db;
            var def = ItemId != null ? db.Item(ItemId) : null;
            if (def != null)
            {
                Icon.sprite = AssetRegistry.Instance.Icon(def.Icon);
                Icon.enabled = true;
                Icon.color = Icon.sprite != null ? Color.white : new Color(1, 0, 1, 0.6f);
                Qty.text = def.Stackable || qty > 1 ? UIKit.FormatQty(qty) : "";
            }
            else
            {
                Icon.sprite = EmptySprite;
                Icon.enabled = EmptySprite != null;
                Icon.color = new Color(1, 1, 1, 0.18f);
                Qty.text = "";
            }
        }

        public void SetHighlight(bool on) => Bg.color = on ? new Color(0.45f, 0.38f, 0.15f, 0.95f) : UIKit.SlotBg;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (e.button == PointerEventData.InputButton.Left) OnLeft?.Invoke(this);
            else if (e.button == PointerEventData.InputButton.Right) OnRight?.Invoke(this);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (ItemId == null || OnDropped == null || e.button != PointerEventData.InputButton.Left) return;
            _dragFrom = this;
            _ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _ghost.transform.SetParent(UIRoot.Instance.TopLayer, false);
            var img = _ghost.GetComponent<Image>();
            img.sprite = Icon.sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            _ghost.GetComponent<CanvasGroup>().blocksRaycasts = false;
            ((RectTransform)_ghost.transform).sizeDelta = Icon.rectTransform.rect.size;
            Icon.color = new Color(1, 1, 1, 0.35f);
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_ghost != null) _ghost.transform.position = e.position;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            if (_dragFrom == this) Icon.color = Color.white;
            _dragFrom = null;
        }

        public void OnDrop(PointerEventData e)
        {
            if (_dragFrom == null || _dragFrom == this) return;
            var from = _dragFrom;
            (OnDropped ?? from.OnDropped)?.Invoke(from, this);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            var t = Tooltip?.Invoke(this);
            if (!string.IsNullOrEmpty(t)) UIRoot.Instance.ShowTooltip(t);
        }

        public void OnPointerExit(PointerEventData e) => UIRoot.Instance.HideTooltip();
    }
}
