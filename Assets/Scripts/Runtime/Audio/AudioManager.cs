using System.Collections.Generic;
using UnityEngine;

namespace Embervale.Client
{
    /// <summary>
    /// Music (crossfaded per region), ambient loops and pooled one-shot sound effects. Clips are looked up
    /// by name in the AssetRegistry (generated WAVs in Assets/Audio). Missing clips are silently skipped
    /// after one warning so a missing sound never breaks gameplay.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private AudioSource _musicA, _musicB, _ambient, _ui;
        private bool _aActive = true;
        private string _music, _ambientName;
        private float _fade = 1f;
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly HashSet<string> _missing = new HashSet<string>();
        private GameSession _session;

        private void Awake()
        {
            _session = GetComponent<GameSession>();
            _musicA = NewSource("MusicA", true);
            _musicB = NewSource("MusicB", true);
            _ambient = NewSource("Ambient", true);
            _ui = NewSource("UI", false);
            for (int i = 0; i < 16; i++) _pool.Add(NewSource("Sfx" + i, false, true));
            Settings.OnChanged += ApplyVolumes;
            ApplyVolumes();
        }

        private void OnDestroy() => Settings.OnChanged -= ApplyVolumes;

        private AudioSource NewSource(string name, bool loop, bool spatial = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 4f;
            s.maxDistance = 40f;
            s.dopplerLevel = 0;
            return s;
        }

        private void ApplyVolumes()
        {
            AudioListener.volume = Settings.MasterVolume;
            _ambient.volume = Settings.AmbientVolume;
            _ui.volume = Settings.SfxVolume;
            foreach (var s in _pool) s.volume = Settings.SfxVolume;
        }

        private AudioClip Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var c = AssetRegistry.Instance.Clip(name);
            if (c == null && _missing.Add(name)) Debug.Log("[Audio] no clip named '" + name + "'");
            return c;
        }

        public void PlayMusic(string name)
        {
            if (name == _music) return;
            var clip = Get(name);
            _music = name;
            if (clip == null) return;
            var next = _aActive ? _musicB : _musicA;
            next.clip = clip;
            next.volume = 0;
            next.Play();
            _aActive = !_aActive;
            _fade = 0f;
        }

        public void PlayAmbient(string name)
        {
            if (name == _ambientName) return;
            _ambientName = name;
            var clip = Get(name);
            if (clip == null) { _ambient.Stop(); return; }
            _ambient.clip = clip;
            _ambient.Play();
        }

        public void PlayUi(string name)
        {
            var c = Get(name);
            if (c != null) _ui.PlayOneShot(c);
        }

        public void PlayAt(string name, Vector3 pos, float pitchJitter = 0.06f)
        {
            var c = Get(name);
            if (c == null) return;
            AudioSource free = null;
            foreach (var s in _pool) if (!s.isPlaying) { free = s; break; }
            if (free == null) free = _pool[0];
            free.transform.position = pos;
            free.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            free.clip = c;
            free.Play();
        }

        private void Update()
        {
            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / 2.5f);
                var cur = _aActive ? _musicA : _musicB;
                var old = _aActive ? _musicB : _musicA;
                cur.volume = _fade * Settings.MusicVolume;
                old.volume = (1f - _fade) * Settings.MusicVolume;
                if (_fade >= 1f) old.Stop();
            }
            else
            {
                (_aActive ? _musicA : _musicB).volume = Settings.MusicVolume;
            }
            // Pool sources follow the listener when not spatial-relevant.
            var cam = Camera.main;
            if (cam != null) transform.position = cam.transform.position;
        }

        /// <summary>Hooks the GameClient's sound/combat events. Called by the UI when entering the world.</summary>
        public void Bind(Embervale.Shared.Net.GameClient c)
        {
            c.OnSound += (id, name) =>
            {
                var v = _session.Entities?.Get(id);
                if (v != null) PlayAt(name, v.transform.position); else PlayUi(name);
            };
            c.OnHitSplat += (victim, attacker, dmg, crit, kind) =>
            {
                var v = _session.Entities?.Get(victim);
                if (v == null) return;
                PlayAt(kind == "miss" ? "miss" : dmg > 0 ? (crit ? "hit_crit" : "hit") : "block", v.transform.position);
            };
            c.OnLevelUp += (s, l) => PlayUi("level_up");
            c.OnDeath += (id, killer) =>
            {
                var v = _session.Entities?.Get(id);
                if (v != null) PlayAt("death", v.transform.position);
            };
            c.OnAnimation += (id, anim) =>
            {
                var v = _session.Entities?.Get(id);
                if (v == null) return;
                switch (anim)
                {
                    case Embervale.Shared.Simulation.ActionAnim.Eat: PlayAt("eat", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.Drink: PlayAt("drink", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.AttackMelee: PlayAt("swing", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.Mine: PlayAt("mine", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.Chop: PlayAt("chop", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.Fish: PlayAt("splash", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.Smith: PlayAt("anvil", v.transform.position); break;
                    case Embervale.Shared.Simulation.ActionAnim.Pray: PlayAt("pray", v.transform.position); break;
                }
            };
        }
    }
}
