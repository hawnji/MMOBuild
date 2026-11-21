using System.Collections.Generic;
using Embervale.Shared.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Embervale.Client
{
    /// <summary>
    /// Login, character selection and character creation screens, shown over a small 3D stage (campfire,
    /// trees, the selected character idling) rendered by a dedicated front-end camera.
    /// </summary>
    public sealed class FrontEnd
    {
        private readonly UIRoot _ui;
        private readonly GameSession _session;
        private RectTransform _screen;
        private Text _status;
        private PreviewStage _stage;

        // character creation state
        private readonly Appearance _look = new Appearance { Gender = 0, SkinTone = 1, HairStyle = 0, HairColor = 1, BodyColor = 2, LegColor = 9 };
        private int _selected;

        public FrontEnd(UIRoot ui, GameSession session)
        {
            _ui = ui;
            _session = session;
        }

        public void SetStatus(string text)
        {
            if (_status != null) _status.text = text ?? "";
        }

        public void Show(ClientState s)
        {
            if (_screen != null) Object.Destroy(_screen.gameObject);
            _screen = null;
            _status = null;
            if (s == ClientState.InWorld)
            {
                if (_stage != null) { _stage.Dispose(); _stage = null; }
                return;
            }
            if (_stage == null) _stage = new PreviewStage();
            _screen = UIKit.Node("Screen_" + s, _ui.HudLayer).Stretch();
            switch (s)
            {
                case ClientState.Login:
                case ClientState.Connecting: BuildLogin(s == ClientState.Connecting); _stage.SetCharacter(null); break;
                case ClientState.CharacterSelect: BuildSelect(); break;
                case ClientState.CharacterCreate: BuildCreate(); break;
                case ClientState.EnteringWorld: BuildLoading(); break;
            }
            SetStatus(_session.StatusText);
        }

        private RectTransform Card(Vector2 anchor, Vector2 size, string title)
        {
            var card = UIKit.Panel(_screen, "Card", new Color(0.1f, 0.09f, 0.08f, 0.9f));
            card.rectTransform.Place(anchor, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var t = UIKit.Text(card.transform, "Title", title, 26, TextAnchor.MiddleCenter, UIKit.TextGold);
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(size.x - 20, 40));
            return card.rectTransform;
        }

        private Text StatusLine(RectTransform card)
        {
            var t = UIKit.Text(card, "Status", "", 15, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.6f));
            t.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(card.sizeDelta.x - 20, 44));
            return t;
        }

        // ------------------------------------------------------------------ login
        private void BuildLogin(bool busy)
        {
            var logo = UIKit.Text(_screen, "Logo", "EMBERVALE", 72, TextAnchor.MiddleCenter, UIKit.TextGold);
            logo.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(900, 100));
            logo.fontStyle = FontStyle.Bold;
            var sub = UIKit.Text(_screen, "Tagline", "A persistent low-poly fantasy world", 20, TextAnchor.MiddleCenter, UIKit.TextDim);
            sub.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(900, 30));

            var card = Card(new Vector2(0.5f, 0.42f), new Vector2(420, 400), "Welcome");
            float y = -62;
            UIKit.Text(card, "UserLabel", "Username", 15, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(360, 20));
            var user = UIKit.Input(card, "User", "3-20 letters, digits or _");
            ((RectTransform)user.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y - 22), new Vector2(360, 34));
            user.text = Settings.LastUser;
            y -= 66;
            UIKit.Text(card, "PassLabel", "Password", 15, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(360, 20));
            var pass = UIKit.Input(card, "Pass", "at least 6 characters", true);
            ((RectTransform)pass.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y - 22), new Vector2(360, 34));
            y -= 66;
            UIKit.Text(card, "ServerLabel", "Server (host:port) - only for online play", 15, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(360, 20));
            var server = UIKit.Input(card, "Server", "127.0.0.1:7777");
            ((RectTransform)server.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y - 22), new Vector2(360, 34));
            server.text = Settings.LastServer;
            y -= 74;

            void Go(bool online, bool register)
            {
                Settings.LastUser = user.text;
                Settings.LastServer = server.text;
                if (online)
                {
                    var parts = server.text.Split(':');
                    int port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : Shared.Net.Protocol.DefaultPort;
                    _session.PlayOnline(parts[0].Trim(), port, user.text.Trim(), pass.text, register);
                }
                else _session.PlayOffline(user.text.Trim(), pass.text, register);
            }

            var b1 = UIKit.Button(card, "Offline", "Play offline", () => Go(false, false));
            ((RectTransform)b1.transform).Place(new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(-4, y), new Vector2(176, 36));
            var b2 = UIKit.Button(card, "OfflineNew", "New offline account", () => Go(false, true), 14);
            ((RectTransform)b2.transform).Place(new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(4, y), new Vector2(176, 36));
            y -= 44;
            var b3 = UIKit.Button(card, "Online", "Log in to server", () => Go(true, false));
            ((RectTransform)b3.transform).Place(new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(-4, y), new Vector2(176, 36));
            var b4 = UIKit.Button(card, "Register", "Create server account", () => Go(true, true), 14);
            ((RectTransform)b4.transform).Place(new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(4, y), new Vector2(176, 36));
            foreach (var b in new[] { b1, b2, b3, b4 }) b.interactable = !busy && _session.Db != null;
            _status = StatusLine(card);
            if (_session.Db == null) _status.text = "Game data is missing from StreamingAssets/GameData.";

            var help = UIKit.Text(_screen, "Help", "Offline play runs the full server on this PC. Online play needs Embervale.Server running (see README).", 14, TextAnchor.MiddleCenter, UIKit.TextDim);
            help.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(1000, 24));
        }

        // ------------------------------------------------------------------ select
        private void BuildSelect()
        {
            var chars = _session.Client.Characters;
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, chars.Count - 1));
            var card = Card(new Vector2(0.2f, 0.5f), new Vector2(380, 520), "Your Characters");
            float y = -64;
            for (int i = 0; i < chars.Count; i++)
            {
                int idx = i;
                var c = chars[i];
                var b = UIKit.Button(card, "Char" + i, c.Name + "\n<size=13><color=#c8b898>Combat " + c.CombatLevel + "  -  Total level " + c.TotalLevel + "</color></size>", () => { _selected = idx; Show(ClientState.CharacterSelect); }, 18);
                ((RectTransform)b.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(330, 54));
                if (i == _selected) b.GetComponent<Image>().color = new Color(0.55f, 0.42f, 0.2f);
                y -= 62;
            }
            if (chars.Count > 0) _stage.SetCharacter(chars[_selected].Appearance);
            var play = UIKit.Button(card, "Play", "Enter world", () => { if (chars.Count > 0) _session.SelectCharacter(chars[_selected].Id); }, 20);
            ((RectTransform)play.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(330, 44));
            var create = UIKit.Button(card, "Create", "Create new character", () => _session.ShowCreate());
            ((RectTransform)create.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(330, 36));
            var back = UIKit.Button(card, "Back", "Log out", () => _session.ReturnToLogin());
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(160, 32));
            _status = StatusLine(card);
            _status.rectTransform.anchoredPosition = new Vector2(0, -44);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = new Vector2(0.5f, 0);
        }

        // ------------------------------------------------------------------ create
        private void BuildCreate()
        {
            var card = Card(new Vector2(0.2f, 0.5f), new Vector2(400, 600), "Create Character");
            float y = -64;
            UIKit.Text(card, "NameLabel", "Name", 15, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(340, 20));
            var name = UIKit.Input(card, "Name", "3-12 letters, digits or spaces");
            ((RectTransform)name.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y - 22), new Vector2(340, 34));
            name.characterLimit = 12;
            y -= 74;

            Text Row(string label, System.Func<string> value, System.Action<int> step)
            {
                UIKit.Text(card, label, label, 16, TextAnchor.MiddleLeft, UIKit.TextDim).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-50, y), new Vector2(240, 32));
                var val = UIKit.Text(card, label + "Value", value(), 16, TextAnchor.MiddleCenter);
                val.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(90, y), new Vector2(110, 32));
                var l = UIKit.Button(card, label + "Prev", "<", () => { step(-1); val.text = value(); _stage.SetCharacter(_look); });
                ((RectTransform)l.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(20, y), new Vector2(32, 30));
                var r = UIKit.Button(card, label + "Next", ">", () => { step(1); val.text = value(); _stage.SetCharacter(_look); });
                ((RectTransform)r.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(160, y), new Vector2(32, 30));
                y -= 44;
                return val;
            }

            byte Wrap(int v, int n) => (byte)(((v % n) + n) % n);
            Row("Body type", () => _look.Gender == 0 ? "A" : "B", d => _look.Gender = Wrap(_look.Gender + d, 2));
            Row("Skin tone", () => (_look.SkinTone + 1) + " / 6", d => _look.SkinTone = Wrap(_look.SkinTone + d, 6));
            Row("Hair style", () => Palette.HairStyles[_look.HairStyle % Palette.HairStyles.Length], d => _look.HairStyle = Wrap(_look.HairStyle + d, 6));
            Row("Hair colour", () => (_look.HairColor + 1) + " / 8", d => _look.HairColor = Wrap(_look.HairColor + d, 8));
            Row("Top colour", () => (_look.BodyColor + 1) + " / 10", d => _look.BodyColor = Wrap(_look.BodyColor + d, 10));
            Row("Legs colour", () => (_look.LegColor + 1) + " / 10", d => _look.LegColor = Wrap(_look.LegColor + d, 10));
            var rnd = UIKit.Button(card, "Random", "Randomise", () =>
            {
                _look.Gender = (byte)Random.Range(0, 2); _look.SkinTone = (byte)Random.Range(0, 6); _look.HairStyle = (byte)Random.Range(0, 6);
                _look.HairColor = (byte)Random.Range(0, 8); _look.BodyColor = (byte)Random.Range(0, 10); _look.LegColor = (byte)Random.Range(0, 10);
                Show(ClientState.CharacterCreate);
            });
            ((RectTransform)rnd.transform).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(160, 30));

            var create = UIKit.Button(card, "Create", "Create", () => _session.CreateCharacter(name.text.Trim(), _look.Clone()), 20);
            ((RectTransform)create.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(330, 42));
            var back = UIKit.Button(card, "Back", "Back", () => _session.ShowSelect());
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(160, 32));
            _status = StatusLine(card);
            _status.rectTransform.anchoredPosition = new Vector2(0, -44);
            _stage.SetCharacter(_look);
        }

        private void BuildLoading()
        {
            var t = UIKit.Text(_screen, "Loading", "Entering Embervale...", 30, TextAnchor.MiddleCenter, UIKit.TextGold);
            t.rectTransform.Place(new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 50));
            _status = UIKit.Text(_screen, "Status", "", 16, TextAnchor.MiddleCenter, UIKit.TextDim);
            _status.rectTransform.Place(new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 30));
        }
    }

    /// <summary>The little 3D scene behind the front-end screens.</summary>
    public sealed class PreviewStage
    {
        private readonly GameObject _root;
        private readonly Transform _slot;
        private CharacterRig _rig;
        private string _key;

        public PreviewStage()
        {
            _root = new GameObject("FrontEndStage");
            _root.transform.position = new Vector3(0, -500, 0);
            var assets = AssetRegistry.Instance;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Ground";
            ground.transform.SetParent(_root.transform, false);
            ground.transform.localScale = new Vector3(14, 0.1f, 14);
            ground.transform.localPosition = new Vector3(0, -0.1f, 0);
            var gm = assets.TerrainMaterial;
            if (gm != null) ground.GetComponent<Renderer>().sharedMaterial = gm;
            Object.Destroy(ground.GetComponent<Collider>());
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(0.36f, 0.5f, 0.27f));
            mpb.SetColor("_Color", new Color(0.36f, 0.5f, 0.27f));
            ground.GetComponent<Renderer>().SetPropertyBlock(mpb);

            void Put(string model, Vector3 p, float rot, float scale = 1f)
            {
                if (!assets.HasModel(model)) return;
                var go = assets.Spawn(model, _root.transform);
                go.transform.localPosition = p;
                go.transform.localRotation = Quaternion.Euler(0, rot, 0);
                go.transform.localScale = Vector3.one * scale;
                Tintable.On(go);
            }
            Put("tree_oak", new Vector3(-3.5f, 0, 3.5f), 20);
            Put("tree_pine", new Vector3(3.8f, 0, 4.2f), 70);
            Put("tree_willow", new Vector3(-5.5f, 0, -1f), 140);
            Put("bush", new Vector3(2.4f, 0, 1.8f), 10);
            Put("flowers", new Vector3(-1.6f, 0, 1.4f), 0);
            Put("rock_large", new Vector3(4.5f, 0, -0.5f), 30);
            Put("log_fallen", new Vector3(1.6f, 0, -1.6f), 75);
            Put("fire", new Vector3(1.6f, 0, -0.2f), 0);
            var fl = new GameObject("FireLight").AddComponent<Light>();
            fl.transform.SetParent(_root.transform, false);
            fl.transform.localPosition = new Vector3(1.6f, 0.8f, -0.2f);
            fl.color = new Color(1f, 0.6f, 0.25f);
            fl.range = 7;
            fl.intensity = 2.2f;
            fl.gameObject.AddComponent<Flicker>();

            _slot = new GameObject("CharacterSlot").transform;
            _slot.SetParent(_root.transform, false);
            _slot.localRotation = Quaternion.Euler(0, 20, 0);   // models face -Z; turn them toward the stage camera

            var camGo = new GameObject("FrontEndCamera");
            camGo.transform.SetParent(_root.transform, false);
            camGo.transform.localPosition = new Vector3(-1.6f, 1.6f, -5.2f);
            camGo.transform.localRotation = Quaternion.Euler(8, 12, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.depth = 5;
            cam.fieldOfView = 40;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.1f, 0.16f);
            cam.farClipPlane = 60;

            var sun = new GameObject("StageSun").AddComponent<Light>();
            sun.transform.SetParent(_root.transform, false);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(35, 150, 0);
            sun.intensity = 0.55f;
            sun.color = new Color(0.6f, 0.7f, 1f);
        }

        public void SetCharacter(Appearance a)
        {
            string key = a == null ? "" : a.Gender + "," + a.SkinTone + "," + a.HairStyle + "," + a.HairColor + "," + a.BodyColor + "," + a.LegColor;
            if (key == _key) return;
            _key = key;
            if (_rig != null) Object.Destroy(_rig.gameObject);
            _rig = null;
            if (a == null) return;
            _rig = CharacterRig.Create(HumanoidLook.FromAppearance(a), _slot);
            if (_rig.Anim != null) _rig.Anim.SetLoop("Idle");
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
