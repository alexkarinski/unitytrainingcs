using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CsAim.Viewmodel
{
    /// <summary>Manual corrections on top of the automatic fit, saved per weapon.</summary>
    [Serializable]
    public struct ModelFit
    {
        public Vector3 euler;     // extra rotation after auto-alignment, degrees
        public Vector3 offset;    // units, in weapon space
        public float scale;       // multiplier on the auto scale

        public static ModelFit Default => new ModelFit { scale = 1f };
    }

    /// <summary>
    /// Weapon models the player imported from their own CS2 install (exported to .glb with
    /// Source 2 Viewer). They are kept only on this computer - in the game's persistent data
    /// folder, which in a browser is the site's IndexedDB - and are never uploaded anywhere.
    /// </summary>
    public static class CustomWeaponModels
    {
        public static event Action<string> Changed;

        static string Folder => Path.Combine(Application.persistentDataPath, "weapon_models");
        static string GlbPath(string weaponId) => Path.Combine(Folder, weaponId + ".glb");
        static string PathKey(string weaponId) => "csaim.model_path." + weaponId;
        static string FitKey(string weaponId) => "csaim.model_fit." + weaponId;

        public static bool Has(string weaponId) =>
            File.Exists(GlbPath(weaponId)) || File.Exists(PlayerPrefs.GetString(PathKey(weaponId), "\u0000"));

        /// <summary>Stored .glb bytes, or the path of a .gltf that references external files.</summary>
        public static bool TryGetSource(string weaponId, out byte[] glb, out string gltfPath)
        {
            glb = null;
            gltfPath = null;
            if (File.Exists(GlbPath(weaponId))) { glb = File.ReadAllBytes(GlbPath(weaponId)); return true; }
            string p = PlayerPrefs.GetString(PathKey(weaponId), string.Empty);
            if (p.Length > 0 && File.Exists(p)) { gltfPath = p; return true; }
            return false;
        }

        /// <summary>Stores a self-contained .glb (works on desktop and in the browser).</summary>
        public static void SaveGlb(string weaponId, byte[] data)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(GlbPath(weaponId), data);
            PlayerPrefs.DeleteKey(PathKey(weaponId));
            SetFit(weaponId, ModelFit.Default);
            SyncFs();
            Changed?.Invoke(weaponId);
        }

        /// <summary>Desktop: import from a path. .glb is copied; .gltf is referenced in place (it needs its buffers/textures next to it).</summary>
        public static void ImportFile(string weaponId, string path)
        {
            if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                SaveGlb(weaponId, File.ReadAllBytes(path));
                return;
            }
            if (File.Exists(GlbPath(weaponId))) File.Delete(GlbPath(weaponId));
            PlayerPrefs.SetString(PathKey(weaponId), path);
            SetFit(weaponId, ModelFit.Default);
            PlayerPrefs.Save();
            Changed?.Invoke(weaponId);
        }

        public static void Remove(string weaponId)
        {
            if (File.Exists(GlbPath(weaponId))) File.Delete(GlbPath(weaponId));
            PlayerPrefs.DeleteKey(PathKey(weaponId));
            PlayerPrefs.DeleteKey(FitKey(weaponId));
            PlayerPrefs.Save();
            SyncFs();
            Changed?.Invoke(weaponId);
        }

        public static ModelFit GetFit(string weaponId)
        {
            string json = PlayerPrefs.GetString(FitKey(weaponId), string.Empty);
            if (json.Length == 0) return ModelFit.Default;
            var f = JsonUtility.FromJson<ModelFit>(json);
            if (f.scale <= 0f) f.scale = 1f;
            return f;
        }

        public static void SetFit(string weaponId, ModelFit fit)
        {
            PlayerPrefs.SetString(FitKey(weaponId), JsonUtility.ToJson(fit));
            PlayerPrefs.Save();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CsAimSyncFs();
        static void SyncFs() => CsAimSyncFs();
#else
        static void SyncFs() { }
#endif
    }
}
