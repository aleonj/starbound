using UnityEngine;

namespace StarBound.Core
{
    // Persisted player-facing settings. Plain static class rather than a
    // MonoBehaviour/DontDestroyOnLoad singleton — the game never loads a
    // scene at runtime (see DemoBootstrap), so a static field already
    // survives for the whole process lifetime with no extra machinery.
    // Only Master Volume exists so far: no audio, haptics, or other
    // settings-worthy systems exist in the codebase yet (see the
    // "[UI] Settings screen" story) — add their backing fields here,
    // following the same PlayerPrefs-backed property shape, once those
    // systems actually exist to control.
    public static class GameSettings
    {
        private const string MasterVolumeKey = "Settings.MasterVolume";
        private static float masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);

        public static float MasterVolume
        {
            get => masterVolume;
            set
            {
                masterVolume = Mathf.Clamp01(value);
                AudioListener.volume = masterVolume;
                PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
            }
        }

        // The static initializer above only sets the backing field, not
        // AudioListener.volume itself — call this once at startup so the
        // persisted value actually takes effect before any audio plays.
        public static void Apply() => AudioListener.volume = masterVolume;
    }
}
