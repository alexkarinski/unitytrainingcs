using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CsAim.Data
{
    /// <summary>
    /// Supplies the weapon list. Priority:
    ///   1. WeaponDefinition assets the user imported in the editor (Resources/Weapons)
    ///   2. The path saved in PlayerPrefs, StreamingAssets/items_game.txt, or the default CS2
    ///      pak01_dir.vpk (CS2 keeps items_game.txt inside the VPK)
    ///   3. Bundled fallbacks below
    ///
    /// The fallbacks are approximations written from public CS:GO-era data so the trainer runs
    /// out of the box. They are NOT authoritative - import your own items_game.txt for exact
    /// numbers (the HUD shows which source is active).
    /// </summary>
    public static class WeaponLibrary
    {
        public const string PathPrefKey = "csaim.items_game_path";

        public static List<WeaponDefinition> Load()
        {
            var list = new List<WeaponDefinition>(Resources.LoadAll<WeaponDefinition>("Weapons"));
            if (list.Count > 0) return Order(list);

            foreach (var path in CandidatePaths())
            {
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                try
                {
                    var imported = ItemsGameImporter.ImportFile(path);
                    if (imported.Count > 0) return Order(imported);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CS Aim] Failed to import {path}: {e.Message}");
                }
            }

            return Order(Fallbacks());
        }

        static IEnumerable<string> CandidatePaths()
        {
            string saved = PlayerPrefs.GetString(PathPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(saved)) yield return saved;
            yield return Path.Combine(Application.streamingAssetsPath, "items_game.txt");
            yield return ItemsGameImporter.DefaultItemsGamePath;
        }

        static readonly string[] PreferredOrder =
        {
            "weapon_ak47", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_awp", "weapon_deagle",
            "weapon_usp_silencer", "weapon_glock", "weapon_galilar", "weapon_famas", "weapon_mp9",
        };

        static List<WeaponDefinition> Order(List<WeaponDefinition> list)
        {
            list.Sort((a, b) =>
            {
                int ia = System.Array.IndexOf(PreferredOrder, a.weaponId);
                int ib = System.Array.IndexOf(PreferredOrder, b.weaponId);
                if (ia < 0) ia = 1000;
                if (ib < 0) ib = 1000;
                return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.weaponId, b.weaponId);
            });
            return list;
        }

        // ------------------------------------------------------------------ fallbacks

        static WeaponDefinition Make(string id, string display)
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinition>();
            w.weaponId = id;
            w.name = id;
            w.displayName = display;
            w.source = "builtin fallback (approximate)";
            return w;
        }

        static WeaponModeStats Mode(float cycle, float speed, float spread, float crouch, float stand,
                                    float jump, float land, float fire, float move,
                                    float rAngle, float rAngleVar, float rMag, float rMagVar)
        {
            return new WeaponModeStats
            {
                cycleTime = cycle, maxPlayerSpeed = speed, spread = spread,
                inaccuracyCrouch = crouch, inaccuracyStand = stand, inaccuracyJump = jump,
                inaccuracyLand = land, inaccuracyLadder = jump, inaccuracyFire = fire, inaccuracyMove = move,
                recoilAngle = rAngle, recoilAngleVariance = rAngleVar,
                recoilMagnitude = rMag, recoilMagnitudeVariance = rMagVar,
            };
        }

        public static List<WeaponDefinition> Fallbacks()
        {
            var list = new List<WeaponDefinition>();

            var ak = Make("weapon_ak47", "AK-47");
            ak.damage = 36; ak.armorRatio = 1.55f; ak.penetration = 2; ak.rangeModifier = 0.98f;
            ak.clipSize = 30; ak.reloadTime = 2.43f; ak.isFullAuto = true; ak.recoilSeed = 223;
            ak.primary = Mode(0.1f, 215, 0.6f, 4.81f, 6.41f, 140f, 242f, 7.8f, 175.06f, 0, 70, 30, 0);
            ak.recoveryTimeCrouch = 0.305f; ak.recoveryTimeStand = 0.368f;
            ak.recoveryTimeCrouchFinal = 0.305f; ak.recoveryTimeStandFinal = 0.368f;
            ak.measuredPatternCsv = AkPattern;
            list.Add(ak);

            var m4 = Make("weapon_m4a1", "M4A4");
            m4.damage = 33; m4.armorRatio = 1.4f; m4.penetration = 2; m4.rangeModifier = 0.97f;
            m4.clipSize = 30; m4.reloadTime = 3.07f; m4.isFullAuto = true; m4.recoilSeed = 38965;
            m4.primary = Mode(0.09f, 225, 0.6f, 3.68f, 4.9f, 140f, 140f, 7f, 137.29f, 0, 70, 23, 0);
            m4.recoveryTimeCrouch = 0.3f; m4.recoveryTimeStand = 0.42f;
            m4.recoveryTimeCrouchFinal = 0.3f; m4.recoveryTimeStandFinal = 0.42f;
            list.Add(m4);

            var m4s = Make("weapon_m4a1_silencer", "M4A1-S");
            m4s.damage = 38; m4s.armorRatio = 1.4f; m4s.penetration = 2; m4s.rangeModifier = 0.99f;
            m4s.clipSize = 20; m4s.reloadTime = 3.07f; m4s.isFullAuto = true; m4s.recoilSeed = 39165;
            m4s.primary = Mode(0.1f, 225, 0.6f, 3.68f, 4.9f, 140f, 140f, 7f, 137.29f, 0, 60, 21, 0);
            m4s.recoveryTimeCrouch = 0.3f; m4s.recoveryTimeStand = 0.42f;
            m4s.recoveryTimeCrouchFinal = 0.3f; m4s.recoveryTimeStandFinal = 0.42f;
            list.Add(m4s);

            var awp = Make("weapon_awp", "AWP");
            awp.damage = 115; awp.armorRatio = 1.95f; awp.penetration = 2.5f; awp.rangeModifier = 0.99f;
            awp.clipSize = 5; awp.reloadTime = 3.67f; awp.isFullAuto = false; awp.recoilSeed = 5605;
            awp.hasAltMode = true; awp.altIsScope = true; awp.zoomFov = new Vector2(40, 10);
            awp.primary = Mode(1.455f, 200, 0.2f, 60f, 80f, 500f, 500f, 110f, 175f, 0, 20, 78, 15);
            awp.secondary = Mode(1.455f, 100, 0.2f, 1.6f, 2f, 500f, 500f, 110f, 175f, 0, 20, 78, 15);
            awp.recoveryTimeCrouch = 0.246f; awp.recoveryTimeStand = 0.345f;
            awp.recoveryTimeCrouchFinal = 0.246f; awp.recoveryTimeStandFinal = 0.345f;
            list.Add(awp);

            var deagle = Make("weapon_deagle", "Desert Eagle");
            deagle.damage = 53; deagle.armorRatio = 1.864f; deagle.penetration = 2; deagle.rangeModifier = 0.85f;
            deagle.clipSize = 7; deagle.reloadTime = 2.2f; deagle.isFullAuto = false; deagle.recoilSeed = 26474;
            deagle.primary = Mode(0.225f, 230, 2f, 3.41f, 5.12f, 340f, 270f, 55f, 25f, 0, 20, 40, 10);
            deagle.recoveryTimeCrouch = 0.594f; deagle.recoveryTimeStand = 0.811f;
            deagle.recoveryTimeCrouchFinal = 0.594f; deagle.recoveryTimeStandFinal = 0.811f;
            list.Add(deagle);

            var usp = Make("weapon_usp_silencer", "USP-S");
            usp.damage = 35; usp.armorRatio = 1.01f; usp.penetration = 1; usp.rangeModifier = 0.91f;
            usp.clipSize = 12; usp.reloadTime = 2.2f; usp.isFullAuto = false; usp.recoilSeed = 18413;
            usp.primary = Mode(0.17f, 240, 2f, 3.68f, 4.9f, 100f, 100f, 52f, 13.87f, 0, 0, 23, 2);
            usp.recoveryTimeCrouch = 0.27f; usp.recoveryTimeStand = 0.32f;
            usp.recoveryTimeCrouchFinal = 0.27f; usp.recoveryTimeStandFinal = 0.32f;
            list.Add(usp);

            var glock = Make("weapon_glock", "Glock-18");
            glock.damage = 30; glock.armorRatio = 0.94f; glock.penetration = 1; glock.rangeModifier = 0.85f;
            glock.clipSize = 20; glock.reloadTime = 2.27f; glock.isFullAuto = false; glock.recoilSeed = 41284;
            glock.primary = Mode(0.15f, 240, 2f, 4.2f, 5.6f, 100f, 100f, 56f, 13.87f, 0, 20, 18, 0);
            glock.recoveryTimeCrouch = 0.29f; glock.recoveryTimeStand = 0.35f;
            glock.recoveryTimeCrouchFinal = 0.29f; glock.recoveryTimeStandFinal = 0.35f;
            list.Add(glock);

            return list;
        }

        /// <summary>
        /// AK-47 spray shape: cumulative view offset per shot in degrees (pitch up, yaw right).
        /// Shape follows the well-known pattern (≈9 shots up, sweep left, sweep right, wobble).
        /// Approximate - replace with a pattern you measured in CS2 (see README, "Calibration").
        /// </summary>
        const string AkPattern =
@"0.00 0.00
0.25 0.02
0.70 0.04
1.30 0.02
2.00 -0.06
2.75 0.02
3.45 0.18
4.05 0.30
4.55 0.30
4.90 0.10
5.10 -0.35
5.25 -0.85
5.40 -1.35
5.50 -1.80
5.55 -2.10
5.60 -1.95
5.65 -1.50
5.75 -0.90
5.85 -0.30
5.90 0.25
5.95 0.85
6.00 1.30
6.05 1.55
6.10 1.45
6.15 1.05
6.20 0.60
6.20 0.25
6.25 -0.10
6.30 -0.25
6.30 -0.15";
    }
}
