using System;
using System.Collections.Generic;
using System.IO;
using Embervale.Shared.Data;
using Embervale.Shared.Simulation;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Plays the legacy Animation clips imported from the Blender pipeline. Gameplay ActionAnim values map to
    /// clip names; one-shot actions (attacks, eating, hurt) play on top and fall back to the looping state.
    /// </summary>
    public sealed class AnimationDriver : MonoBehaviour
    {
        private Animation _anim;
        private string _loop = "Idle";
        private string _oneShot;
        private float _oneShotEnd;

        public static string ClipFor(ActionAnim a)
        {
            switch (a)
            {
                case ActionAnim.Walk: return "Walk";
                case ActionAnim.Run: return "Run";
                case ActionAnim.AttackMelee: return "AttackMelee";
                case ActionAnim.AttackRanged: return "AttackRanged";
                case ActionAnim.CastSpell: return "Cast";
                case ActionAnim.Mine: return "Mine";
                case ActionAnim.Chop: return "Chop";
                case ActionAnim.Fish: return "Fish";
                case ActionAnim.Smith: return "Smith";
                case ActionAnim.Cook: return "Cook";
                case ActionAnim.Craft: return "Craft";
                case ActionAnim.Eat: return "Eat";
                case ActionAnim.Drink: return "Drink";
                case ActionAnim.Hurt: return "Hurt";
                case ActionAnim.Death: return "Death";
                case ActionAnim.Pickpocket: return "Pickpocket";
                case ActionAnim.Climb: return "Climb";
                case ActionAnim.Farm: return "Farm";
                case ActionAnim.Pray: return "Pray";
                case ActionAnim.Firemaking: return "Firemaking";
                default: return "Idle";
            }
        }

        public static bool IsLooping(string clip)
        {
            switch (clip)
            {
                case "Idle": case "Walk": case "Run": case "Mine": case "Chop": case "Fish": case "Smith": case "Cook":
                case "Craft": case "Farm": case "Climb": case "Firemaking":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Adds clips (re-targeted by bone path) to the model's Animation component.</summary>
        public void Init(IEnumerable<AnimationClip> clips)
        {
            _anim = GetComponent<Animation>();
            if (_anim == null) _anim = gameObject.AddComponent<Animation>();
            _anim.playAutomatically = false;
            _anim.cullingType = AnimationCullingType.BasedOnRenderers;
            if (clips != null)
                foreach (var c in clips)
                {
                    if (c == null) continue;
                    var name = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                    if (_anim.GetClip(name) == null) _anim.AddClip(c, name);
                }
            foreach (AnimationState s in _anim)
                s.wrapMode = IsLooping(s.name) ? WrapMode.Loop : (s.name == "Death" ? WrapMode.ClampForever : WrapMode.Once);
            Play("Idle", true);
        }

        public bool Has(string clip) => _anim != null && _anim[clip] != null;

        /// <summary>Sets the looping base state (Idle/Walk/Run/skilling).</summary>
        public void SetLoop(string clip)
        {
            if (!Has(clip)) clip = clip == "Run" && Has("Walk") ? "Walk" : "Idle";
            if (_loop == clip) return;
            _loop = clip;
            if (_oneShot == null) Play(clip, true);
        }

        public void PlayOnce(string clip, float speed = 1f)
        {
            if (!Has(clip)) return;
            if (IsLooping(clip)) { SetLoop(clip); return; }
            _oneShot = clip;
            var st = _anim[clip];
            st.speed = speed;
            _oneShotEnd = clip == "Death" ? float.MaxValue : Time.time + st.length / Mathf.Max(0.1f, speed) - 0.1f;
            _anim.CrossFade(clip, 0.08f, PlayMode.StopAll);
        }

        public void ResetDeath()
        {
            _oneShot = null;
            Play(_loop, true);
        }

        private void Play(string clip, bool loop)
        {
            if (_anim == null || !Has(clip)) return;
            _anim.CrossFade(clip, 0.2f, PlayMode.StopAll);
        }

        private void Update()
        {
            if (_oneShot != null && Time.time >= _oneShotEnd)
            {
                _oneShot = null;
                Play(_loop, true);
            }
        }
    }

    /// <summary>How a held item is mounted to a hand socket (from StreamingAssets/Art/equipment_attach.json).</summary>
    public static class AttachRules
    {
        public sealed class Rule { public string Socket = "RightHandSocket"; public string Up = "forward"; public string Face = "out"; }

        private static Dictionary<string, Rule> _rules;
        private static Rule _default = new Rule();

        public static Rule For(string model)
        {
            if (_rules == null)
            {
                _rules = new Dictionary<string, Rule>();
                try
                {
                    var root = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Art", "equipment_attach.json")));
                    _default = root["default"].ToObject<Rule>();
                    foreach (var p in (JObject)root["models"]) _rules[p.Key] = p.Value.ToObject<Rule>();
                }
                catch (Exception e) { Debug.LogWarning("[Attach] " + e.Message); }
            }
            return model != null && _rules.TryGetValue(model, out var r) ? r : _default;
        }
    }

    /// <summary>Visual description of a humanoid (players from Appearance, NPCs from presets).</summary>
    public sealed class HumanoidLook
    {
        public bool Female;
        public Color Skin = Palette.SkinTones[1];
        public Color Hair = Palette.HairColors[1];
        public Color Cloth = Palette.ClothColors[0];
        public Color Cloth2 = Palette.ClothColors[9];
        public string HairStyle = "short";
        public string Beard = "none";
        public bool Robed;
        /// <summary>Visual equipment: model name -> tint key.</summary>
        public readonly List<KeyValuePair<string, string>> Wear = new List<KeyValuePair<string, string>>();

        public static HumanoidLook FromAppearance(Appearance a)
        {
            a = a ?? new Appearance();
            return new HumanoidLook
            {
                Female = a.Gender == 1,
                Skin = Palette.SkinTones[a.SkinTone % Palette.SkinTones.Length],
                Hair = Palette.HairColors[a.HairColor % Palette.HairColors.Length],
                Cloth = Palette.ClothColors[a.BodyColor % Palette.ClothColors.Length],
                Cloth2 = Palette.ClothColors[a.LegColor % Palette.ClothColors.Length],
                HairStyle = Palette.HairStyles[a.HairStyle % Palette.HairStyles.Length],
            };
        }

        private static HumanoidLook P(bool f, int skin, int hair, string style, int cloth, int cloth2, string beard = "none", bool robed = false,
                                      params string[] wear)
        {
            var l = new HumanoidLook
            {
                Female = f, Skin = Palette.SkinTones[skin], Hair = Palette.HairColors[hair], HairStyle = style,
                Cloth = Palette.ClothColors[cloth], Cloth2 = Palette.ClothColors[cloth2], Beard = beard, Robed = robed
            };
            for (int i = 0; i + 1 < wear.Length; i += 2) l.Wear.Add(new KeyValuePair<string, string>(wear[i], wear[i + 1]));
            return l;
        }

        /// <summary>Outfits for the npc "model" names used in npcs.json.</summary>
        public static HumanoidLook ForNpc(string model, int id)
        {
            switch (model)
            {
                case "npc_elder": return P(false, 1, 5, "short", 5, 5, "full", true);
                case "npc_blacksmith": return P(false, 2, 0, "short", 6, 9, "short", false, "a_leather_body", null, "a_gloves", "iron");
                case "npc_merchant": return P(true, 1, 3, "bun", 7, 0, "none", true);
                case "npc_banker": return P(false, 0, 6, "short", 2, 6, "moustache", false, "a_chest", "gold");
                case "npc_guard": return P(false, 3, 1, "short", 1, 6, "none", false, "a_helm", "steel", "a_chest", "steel", "a_legs", "steel", "w_sword", "steel", "a_shield", "steel");
                case "npc_villager_m": return P(false, id % 6, id % 8, Palette.HairStyles[id % 3], id % 10, (id + 3) % 10, id % 2 == 0 ? "short" : "none");
                case "npc_villager_f": return P(true, id % 6, (id + 2) % 8, Palette.HairStyles[1 + id % 5], (id + 1) % 10, (id + 4) % 10);
                case "npc_innkeeper": return P(false, 1, 4, "short", 4, 0, "full");
                case "npc_seeker": return P(true, 4, 0, "braids", 3, 0, "none", false, "a_leather_body", null, "a_leather_boots", null);
                case "npc_fisher": return P(false, 2, 2, "short", 8, 9, "goatee", false, "a_hat", "fire");
                case "npc_mage": return P(true, 0, 5, "long", 2, 2, "none", false, "a_robe", "blue", "a_hat", "blue", "w_staff", "ghostwood");
                default: return P(id % 2 == 1, id % 6, id % 8, "short", id % 10, 9);
            }
        }
    }

    /// <summary>
    /// Assembles humanoid characters from the shared skeleton FBX: body, hair, beard, skinned armour
    /// (re-bound to the character's bones by name) and held items on hand sockets. Also used to swap
    /// equipment when a player's equipped items change.
    /// </summary>
    public sealed class CharacterRig : MonoBehaviour
    {
        public Transform Armature;
        public Tintable Tint;
        public AnimationDriver Anim;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _restRootSpace = new Dictionary<string, Quaternion>();
        private readonly List<GameObject> _worn = new List<GameObject>();
        private string _wornKey = "";
        private bool _female;

        public static CharacterRig Create(HumanoidLook look, Transform parent)
        {
            var assets = AssetRegistry.Instance;
            var go = assets.Spawn(look.Female ? "Humanoid_Female" : "Humanoid_Male", parent);
            go.name = "Character";
            var rig = go.AddComponent<CharacterRig>();
            rig._female = look.Female;
            rig.Armature = go.transform.Find("Armature") ?? go.transform;
            foreach (var t in rig.Armature.GetComponentsInChildren<Transform>(true))
            {
                rig._bones[t.name] = t;
                rig._restRootSpace[t.name] = Quaternion.Inverse(go.transform.rotation) * t.rotation;
            }
            // body variant / hair / beard
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var n = smr.name;
                if (n == "Body") smr.gameObject.SetActive(!look.Robed);
                else if (n == "Body_Robed") smr.gameObject.SetActive(look.Robed);
                else if (n.StartsWith("Hair_")) smr.gameObject.SetActive(n == "Hair_" + look.HairStyle);
                else if (n.StartsWith("Beard_")) smr.gameObject.SetActive(n == "Beard_" + look.Beard);
            }
            rig.Tint = Tintable.On(go);
            rig.Tint.SetTint("EV_Skin", look.Skin);
            rig.Tint.SetTint("EV_Hair", look.Hair);
            rig.Tint.SetTint("EV_Cloth", look.Cloth);
            rig.Tint.SetTint("EV_Cloth2", look.Cloth2);
            rig.Anim = go.AddComponent<AnimationDriver>();
            rig.Anim.Init(assets.Clips(look.Female ? "Humanoid_Female_Animations" : "Humanoid_Animations"));
            rig.SetWorn(look.Wear);
            return rig;
        }

        public Transform Bone(string name) => _bones.TryGetValue(name, out var t) ? t : null;

        /// <summary>Equipment visuals from a player's equipped item ids (indexed by EquipSlot).</summary>
        public void SetEquipped(string[] equipped, GameDatabase db)
        {
            var wear = new List<KeyValuePair<string, string>>();
            bool hideHair = false;
            if (equipped != null)
                for (int i = 0; i < equipped.Length; i++)
                {
                    var id = equipped[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    var def = db.Item(id);
                    if (def == null || string.IsNullOrEmpty(def.Model)) continue;
                    if ((EquipSlot)i == EquipSlot.Ring || (EquipSlot)i == EquipSlot.Amulet || (EquipSlot)i == EquipSlot.Ammo) continue;
                    wear.Add(new KeyValuePair<string, string>(def.Model, def.Tint));
                    if (def.Model == "a_helm" || def.Model == "a_leather_cowl") hideHair = true;
                }
            SetWorn(wear);
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name.StartsWith("Hair_"))
                    smr.enabled = !hideHair;
        }

        public void SetWorn(List<KeyValuePair<string, string>> wear)
        {
            var key = string.Join(";", wear.ConvertAll(k => k.Key + ":" + k.Value));
            if (key == _wornKey) return;
            _wornKey = key;
            foreach (var g in _worn) if (g != null) Destroy(g);
            _worn.Clear();
            foreach (var kv in wear)
            {
                var model = kv.Key;
                Color tint;
                bool hasTint = Palette.TryTint(kv.Value, out tint);
                GameObject piece = model.StartsWith("a_") && model != "a_shield" ? AddArmour(model) : AddHeld(model);
                if (piece == null) continue;
                foreach (var info in piece.GetComponentsInChildren<TintInfo>(true))
                {
                    if (!hasTint) continue;
                    info.SetLocal("EV_Metal", tint);
                    info.SetLocal("EV_Wood", tint);
                    if (model == "a_robe" || model == "a_hat") info.SetLocal("EV_Cloth", tint);
                }
                _worn.Add(piece);
            }
            Tint.Refresh();
        }

        private GameObject AddArmour(string model)
        {
            var src = AssetRegistry.Instance.Model(model + (_female ? "_female" : "_male"));
            if (src == null) return null;
            var tmp = Instantiate(src);
            GameObject result = null;
            foreach (var smr in tmp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var go = new GameObject(model);
                go.transform.SetParent(transform, false);
                var dst = go.AddComponent<SkinnedMeshRenderer>();
                dst.sharedMesh = smr.sharedMesh;
                dst.sharedMaterials = smr.sharedMaterials;
                var bones = new Transform[smr.bones.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = smr.bones[i] != null ? Bone(smr.bones[i].name) : null;
                dst.bones = bones;
                dst.rootBone = smr.rootBone != null ? Bone(smr.rootBone.name) : Bone("Hips");
                dst.localBounds = smr.localBounds;
                dst.updateWhenOffscreen = false;
                var srcInfo = smr.GetComponent<TintInfo>();
                if (srcInfo != null)
                {
                    var ti = go.AddComponent<TintInfo>();
                    ti.SlotKeys = srcInfo.SlotKeys;
                    ti.BakedColors = srcInfo.BakedColors;
                }
                result = go;
            }
            Destroy(tmp);
            return result;
        }

        private GameObject AddHeld(string model)
        {
            var rule = AttachRules.For(model);
            var socket = Bone(rule.Socket);
            if (socket == null) return null;
            var go = AssetRegistry.Instance.Spawn(model, socket);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Inverse(_restRootSpace[rule.Socket]) * RestRotation(rule);
            return go;
        }

        /// <summary>
        /// Desired rotation of a held model in character root space while the skeleton is in its T-pose rest
        /// (the model's local +Y is the "business end", local +Z an edge, local -X the shield face).
        /// </summary>
        private Quaternion RestRotation(AttachRules.Rule rule)
        {
            bool left = rule.Socket.StartsWith("Left");
            // hand direction in the rest pose (character faces +Z, so its left is -X)
            var hand = left ? Vector3.left : Vector3.right;
            var socketDir = (new Vector3(left ? 0.57f : -0.57f, 0f, 0.82f)).normalized; // matches the Blender socket tilt
            switch (rule.Up)
            {
                case "hand_reverse":
                {
                    var up = -hand;
                    if (rule.Face == "out") return Quaternion.LookRotation(Vector3.Cross(Vector3.up, up), up);
                    return Quaternion.LookRotation(hand, Vector3.up);
                }
                default:
                    return Quaternion.LookRotation(Vector3.up, socketDir);
            }
        }
    }
}
