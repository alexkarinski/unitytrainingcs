namespace CsAim.Core
{
    /// <summary>
    /// Engine constants and cvar defaults. The whole simulation runs in Source units
    /// (1 unit = 1 inch, player is 72 units tall), so these numbers are used verbatim -
    /// no metric conversion that could drift from the game.
    /// </summary>
    public static class CsConstants
    {
        // ---- Simulation
        public const float TickRate = 64f;              // CS2 sub-tick still evaluates movement on a 64 Hz grid
        public const float TickInterval = 1f / TickRate;

        // ---- Mouse (m_yaw / m_pitch are locked to 0.022 in CS2)
        public const float MYaw = 0.022f;
        public const float MPitch = 0.022f;
        public const float MaxPitch = 89f;

        // ---- Player hull / eyes
        public const float StandEyeHeight = 64.093811f;
        public const float DuckEyeHeight = 46.076218f;
        public const float StandHullHeight = 72f;
        public const float DuckHullHeight = 54f;
        public const float HullRadius = 16f;

        // ---- Movement cvars
        public const float SvGravity = 800f;
        public const float SvFriction = 5.2f;
        public const float SvStopSpeed = 80f;
        public const float SvAccelerate = 5.5f;
        public const float SvAirAccelerate = 12f;
        public const float SvAirMaxWishSpeed = 30f;
        public const float SvMaxSpeed = 320f;
        public const float JumpImpulse = 301.993377f;   // sqrt(2 * 800 * 57)
        public const float WalkSpeedModifier = 0.52f;   // shift-walk
        public const float DuckSpeedModifier = 0.34f;
        public const float DuckTime = 0.2f;              // seconds to fully crouch

        // ---- Accuracy: CS considers you "accurate" below 34% of the weapon's max speed
        public const float AccurateSpeedFraction = 0.34f;

        // ---- Recoil cvars (CS:GO/CS2 defaults)
        public const float WeaponRecoilScale = 2.0f;         // weapon_recoil_scale
        public const float RecoilDecay2Exp = 8.0f;           // weapon_recoil_decay2_exp
        public const float RecoilDecay2Lin = 18.0f;          // weapon_recoil_decay2_lin
        public const float RecoilVelDecay = 4.5f;            // weapon_recoil_vel_decay
        // Calibration: scales recoil_magnitude into aim-punch velocity for seeded tables. Chosen so
        // an AK-47 (magnitude 30, 600 RPM) climbs ~6 degrees by the 10th bullet, matching the game.
        public const float SeededKickScale = 0.65f;
        public const float ViewRecoilTracking = 0.45f;       // view_recoil_tracking (how much of aimpunch the camera shows)
        public const int RecoilTableSize = 64;

        // ---- Damage
        public const float RangeModifierDistance = 500f;     // damage *= range_modifier ^ (dist / 500)
        public const float HeadMultiplier = 4.0f;
        public const float StomachMultiplier = 1.25f;
        public const float ChestMultiplier = 1.0f;
        public const float LegMultiplier = 0.75f;
        public const float ArmorBonus = 0.5f;
    }
}
