using System;
using UnityEngine;

namespace CsAim.Data
{
    /// <summary>Per-firing-mode stats. Mode 0 = primary, mode 1 = alt (scoped / silencer on / burst).</summary>
    [Serializable]
    public struct WeaponModeStats
    {
        // Values are stored exactly as items_game.txt writes them (inaccuracy and spread are
        // x1000, i.e. "6.41" means 0.00641 radians-tangent). AccuracyModel does the /1000.
        public float spread;
        public float inaccuracyCrouch;
        public float inaccuracyStand;
        public float inaccuracyJump;
        public float inaccuracyLand;
        public float inaccuracyLadder;
        public float inaccuracyFire;
        public float inaccuracyMove;

        public float recoilAngle;
        public float recoilAngleVariance;
        public float recoilMagnitude;
        public float recoilMagnitudeVariance;

        public float maxPlayerSpeed;
        public float cycleTime;
    }

    /// <summary>
    /// One weapon, described with the same attribute names CS2 uses in items_game.txt.
    /// Create these from Tools > CS Aim > Import items_game.txt, or let WeaponLibrary
    /// build the bundled fallbacks at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "CS Aim/Weapon Definition", fileName = "weapon_")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string weaponId = "weapon_ak47";
        public string displayName = "AK-47";
        [Tooltip("Where the numbers came from: 'items_game.txt <path>' or 'builtin fallback'.")]
        public string source = "builtin fallback";

        [Header("Damage")]
        public float damage = 36f;
        public float armorRatio = 1.55f;
        public float penetration = 2f;
        public float range = 8192f;
        public float rangeModifier = 0.98f;
        public float headshotMultiplier = 4f;
        public int bullets = 1;

        [Header("Firing")]
        public bool isFullAuto = true;
        public int clipSize = 30;
        public float reloadTime = 2.43f;
        public bool hasAltMode;          // scope / silencer
        public bool altIsScope;
        [Tooltip("Zoom FOVs for scoped weapons (first, second level).")]
        public Vector2 zoomFov = new Vector2(40f, 10f);

        [Header("Modes")]
        public WeaponModeStats primary;
        public WeaponModeStats secondary;

        [Header("Accuracy recovery")]
        public float recoveryTimeCrouch = 0.305f;
        public float recoveryTimeStand = 0.368f;
        public float recoveryTimeCrouchFinal = 0.305f;
        public float recoveryTimeStandFinal = 0.368f;
        public int recoveryTransitionStartBullet;
        public int recoveryTransitionEndBullet;

        [Header("Recoil")]
        public int recoilSeed;
        [Tooltip("Optional measured spray: one line per shot, 'pitch yaw' in degrees of cumulative view offset. Overrides the seeded table when present.")]
        [TextArea(3, 12)]
        public string measuredPatternCsv = string.Empty;

        public WeaponModeStats Mode(bool alt) => alt && hasAltMode ? secondary : primary;
    }
}
