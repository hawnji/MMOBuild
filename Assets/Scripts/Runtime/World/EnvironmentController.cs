using Embervale.Shared.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Embervale.Client
{
    /// <summary>
    /// Sun, ambient light and per-region atmosphere. When the player crosses into another region the fog
    /// colour/density, music and ambient loop blend to that region's settings from world.json.
    /// </summary>
    public sealed class EnvironmentController : MonoBehaviour
    {
        private GameSession _session;
        private Light _sun;
        private RegionDef _region;
        private Color _fogTarget = new Color(0.78f, 0.85f, 0.9f);
        private float _densityTarget = 0.008f;
        public RegionDef Region => _region;
        public event System.Action<RegionDef> OnRegionChanged;

        public void Init(GameSession session)
        {
            _session = session;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.95f, 0.86f);
            _sun.intensity = 1.25f;
            _sun.shadows = Settings.Shadows ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 0.65f;
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.52f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.27f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = _densityTarget;
            RenderSettings.fogColor = _fogTarget;
            Settings.OnChanged += ApplySettings;
        }

        private void OnDestroy() => Settings.OnChanged -= ApplySettings;

        private void ApplySettings()
        {
            if (_sun != null) _sun.shadows = Settings.Shadows ? LightShadows.Soft : LightShadows.None;
        }

        private void Update()
        {
            var self = _session?.SelfView;
            if (self == null) return;
            var p = self.transform.position;
            var world = _session.Db.World;
            RegionDef found = null;
            foreach (var r in world.Regions) if (r.Contains(p.x, p.z)) { found = r; break; }
            if (found != _region && found != null)
            {
                _region = found;
                _fogTarget = Palette.Hex(found.Fog, _fogTarget);
                _densityTarget = found.Id == "ashen" ? 0.03f : found.Id == "ruins" ? 0.02f : found.Id == "ironridge" ? 0.012f : 0.008f;
                _session.Audio.PlayMusic("music_" + (found.Music ?? "meadow"));
                _session.Audio.PlayAmbient("amb_" + (found.Ambient ?? "meadow"));
                OnRegionChanged?.Invoke(found);
            }
            float k = 1f - Mathf.Exp(-1.5f * Time.deltaTime);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, _fogTarget, k);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, _densityTarget, k);
            var cam = _session.Camera != null ? _session.Camera.Cam : null;
            if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
        }
    }
}
