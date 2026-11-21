using UnityEngine;

namespace Embervale.Client
{
    /// <summary>Player preferences stored with PlayerPrefs (per PC, never on the server).</summary>
    public static class Settings
    {
        public static float MasterVolume { get => PlayerPrefs.GetFloat("ev.vol.master", 0.8f); set { PlayerPrefs.SetFloat("ev.vol.master", value); Changed(); } }
        public static float MusicVolume { get => PlayerPrefs.GetFloat("ev.vol.music", 0.5f); set { PlayerPrefs.SetFloat("ev.vol.music", value); Changed(); } }
        public static float SfxVolume { get => PlayerPrefs.GetFloat("ev.vol.sfx", 0.8f); set { PlayerPrefs.SetFloat("ev.vol.sfx", value); Changed(); } }
        public static float AmbientVolume { get => PlayerPrefs.GetFloat("ev.vol.ambient", 0.5f); set { PlayerPrefs.SetFloat("ev.vol.ambient", value); Changed(); } }
        public static int TargetFps { get => PlayerPrefs.GetInt("ev.fps", 60); set { PlayerPrefs.SetInt("ev.fps", value); Application.targetFrameRate = value; Changed(); } }
        public static bool VSync { get => PlayerPrefs.GetInt("ev.vsync", 0) == 1; set { PlayerPrefs.SetInt("ev.vsync", value ? 1 : 0); QualitySettings.vSyncCount = value ? 1 : 0; Changed(); } }
        public static bool Shadows { get => PlayerPrefs.GetInt("ev.shadows", 1) == 1; set { PlayerPrefs.SetInt("ev.shadows", value ? 1 : 0); Changed(); } }
        public static float UiScale { get => PlayerPrefs.GetFloat("ev.uiscale", 1f); set { PlayerPrefs.SetFloat("ev.uiscale", Mathf.Clamp(value, 0.75f, 1.5f)); Changed(); } }
        public static bool ShowRoofs { get => PlayerPrefs.GetInt("ev.roofs", 1) == 1; set { PlayerPrefs.SetInt("ev.roofs", value ? 1 : 0); Changed(); } }
        public static bool ShowXpDrops { get => PlayerPrefs.GetInt("ev.xpdrops", 1) == 1; set { PlayerPrefs.SetInt("ev.xpdrops", value ? 1 : 0); Changed(); } }
        public static bool ShowNames { get => PlayerPrefs.GetInt("ev.names", 1) == 1; set { PlayerPrefs.SetInt("ev.names", value ? 1 : 0); Changed(); } }
        public static float CameraSensitivity { get => PlayerPrefs.GetFloat("ev.camsens", 1f); set { PlayerPrefs.SetFloat("ev.camsens", value); Changed(); } }
        public static string LastServer { get => PlayerPrefs.GetString("ev.server", "127.0.0.1:7777"); set { PlayerPrefs.SetString("ev.server", value); } }
        public static string LastUser { get => PlayerPrefs.GetString("ev.user", ""); set { PlayerPrefs.SetString("ev.user", value); } }

        public static event System.Action OnChanged;
        private static void Changed() { PlayerPrefs.Save(); OnChanged?.Invoke(); }
    }
}
