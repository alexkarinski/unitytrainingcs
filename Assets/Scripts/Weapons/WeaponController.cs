using System;
using CsAim.Core;
using CsAim.Data;
using UnityEngine;

namespace CsAim.Weapons
{
    /// <summary>Everything about one fired bullet, split the way the guide teaches it:
    /// recoil (learnable, compensate with the mouse) vs spread (random, cannot be compensated).</summary>
    public struct ShotInfo
    {
        public int shotIndex;             // 0-based index in the current spray
        public float time;
        public Vector2 recoilDeg;         // pitch up / yaw right added by recoil
        public Vector2 spreadDeg;         // random deviation from inaccuracy + spread
        public float inaccuracy;          // tangent, after /1000
        public float spread;              // tangent, after /1000
        public bool firstShotAccurate;    // fired at stand/crouch base inaccuracy
    }

    /// <summary>
    /// Fire loop, accuracy penalty and aim punch for a single weapon. Plain C# so the logic is
    /// independent of frame rate: the player feeds it fixed 64 Hz ticks.
    /// </summary>
    public sealed class WeaponController
    {
        public WeaponDefinition Def { get; }
        public RecoilTable Recoil { get; }

        /// <summary>Raw aim punch (pitch up, yaw right). Bullets go to eye + punch * weapon_recoil_scale.</summary>
        public Vector2 AimPunchRaw { get; private set; }
        public Vector2 BulletRecoilDeg => AimPunchRaw * CsConstants.WeaponRecoilScale;
        /// <summary>How far the camera itself is kicked (view_recoil_tracking of the bullet recoil).</summary>
        public Vector2 ViewRecoilDeg => BulletRecoilDeg * CsConstants.ViewRecoilTracking;

        public float AccuracyPenalty { get; private set; }
        public float RecoilIndex { get; private set; }
        public int ShotsFired { get; private set; }
        public int Ammo { get; private set; }
        public bool Reloading => _reloadEnd > 0f;
        public float ReloadProgress => Reloading ? 1f - Mathf.Clamp01((_reloadEnd - _now) / Def.reloadTime) : 1f;
        public bool InfiniteAmmo = true;   // sv_infinite_ammo 2: reload still happens, magazine refills
        public int ZoomLevel { get; private set; }
        public bool AltMode => Def.hasAltMode && (Def.altIsScope ? ZoomLevel > 0 : _silencerOn);
        public float CurrentInaccuracy { get; private set; }
        public float CurrentSpread { get; private set; }

        public event Action<ShotInfo> Fired;

        Vector2 _punchVel;
        float _nextAttack;
        float _lastShotTime = -100f;
        float _reloadEnd;
        float _now;
        bool _silencerOn = true;
        bool _interrupted;
        bool _wasOnGround = true;
        float _lastVerticalSpeed;
        readonly ValveUniformRandomStream _rng;

        public WeaponController(WeaponDefinition def, int spreadSeed = 0)
        {
            Def = def;
            Recoil = new RecoilTable(def);
            Ammo = def.clipSize;
            _rng = new ValveUniformRandomStream(spreadSeed != 0 ? spreadSeed : Environment.TickCount);
        }

        public WeaponModeStats Mode => Def.Mode(AltMode);

        public void ToggleAlt()
        {
            if (!Def.hasAltMode) return;
            if (Def.altIsScope) ZoomLevel = (ZoomLevel + 1) % 3;
            else _silencerOn = !_silencerOn;
        }

        public void StartReload()
        {
            if (Reloading || Ammo == Def.clipSize) return;
            _reloadEnd = _now + Def.reloadTime;
            ZoomLevel = 0;
        }

        /// <summary>
        /// Aborts the spray immediately (even mid-tick), resets recoil and refills the magazine.
        /// Used by the path trainer to restart an attempt the moment you leave the trajectory.
        /// </summary>
        public void Interrupt()
        {
            ResetSpray();
            Ammo = Def.clipSize;
            _reloadEnd = 0f;
            _interrupted = true;
        }

        public void ResetSpray()
        {
            AimPunchRaw = Vector2.zero;
            _punchVel = Vector2.zero;
            AccuracyPenalty = 0f;
            RecoilIndex = 0f;
            ShotsFired = 0;
        }

        public void Tick(float now, float dt, in OwnerState owner, bool attackHeld, bool attackPressed)
        {
            _now = now;
            _interrupted = false;
            var mode = Mode;

            // Landing adds a one-off penalty scaled by how hard you hit the ground.
            if (owner.onGround && !_wasOnGround)
            {
                float fall = Mathf.Clamp01(Mathf.Abs(_lastVerticalSpeed) / CsConstants.JumpImpulse);
                AccuracyPenalty += mode.inaccuracyLand * fall;
            }
            _wasOnGround = owner.onGround;
            _lastVerticalSpeed = owner.velocity.y;

            if (Reloading && now >= _reloadEnd)
            {
                _reloadEnd = 0f;
                Ammo = Def.clipSize;
            }

            // Releasing the trigger ends the spray (CS resets m_iShotsFired on release).
            if (!attackHeld) ShotsFired = 0;

            bool spraying = attackHeld && now - _lastShotTime <= mode.cycleTime * 1.5f;

            // Accuracy penalty and recoil index recover exponentially once we stop shooting.
            float recovery = AccuracyModel.RecoveryTime(Def, owner.duckAmount, RecoilIndex);
            AccuracyPenalty = AccuracyModel.Decay(AccuracyPenalty, dt, recovery);
            // Only recover once a shot is overdue: on the tick the next round fires the gap is
            // already >= cycletime, and decaying there would shift the pattern by a bullet.
            if (now - _lastShotTime >= mode.cycleTime + dt)
                RecoilIndex = AccuracyModel.Decay(RecoilIndex, dt, recovery);
            if (RecoilIndex < 0.01f) RecoilIndex = 0f;

            DecayPunch(dt, spraying);

            float stateInaccuracy = AccuracyModel.StateInaccuracy(mode, owner);
            CurrentInaccuracy = (stateInaccuracy + AccuracyPenalty) / 1000f;
            CurrentSpread = mode.spread / 1000f;

            if (Ammo <= 0 && !Reloading)
            {
                if (InfiniteAmmo) StartReload();
                return;
            }

            bool wantsFire = Def.isFullAuto ? attackHeld : attackPressed;
            if (!wantsFire || Reloading) return;

            // Catch up on every shot that should have happened inside this tick (high RPM).
            if (_nextAttack < now - dt) _nextAttack = now - dt;
            while (_nextAttack <= now && Ammo > 0)
            {
                FireOne(_nextAttack, mode, stateInaccuracy);
                _nextAttack += mode.cycleTime;
                if (!Def.isFullAuto || _interrupted) break;
            }
        }

        void FireOne(float time, WeaponModeStats mode, float stateInaccuracy)
        {
            bool firstShot = ShotsFired == 0 && RecoilIndex < 0.5f && AccuracyPenalty < 0.5f;
            float inaccuracy = (stateInaccuracy + AccuracyPenalty) / 1000f;
            float spread = mode.spread / 1000f;

            Vector2 recoilBefore = ApplyRecoil(RecoilIndex);

            Vector2 tangent = AccuracyModel.SampleDeviation(_rng, inaccuracy, spread);
            var info = new ShotInfo
            {
                shotIndex = ShotsFired,
                time = time,
                recoilDeg = recoilBefore,
                spreadDeg = new Vector2(Mathf.Atan(tangent.y), Mathf.Atan(tangent.x)) * Mathf.Rad2Deg,
                inaccuracy = inaccuracy,
                spread = spread,
                firstShotAccurate = firstShot,
            };

            AccuracyPenalty += mode.inaccuracyFire;
            RecoilIndex += 1f;
            ShotsFired++;
            _lastShotTime = time;
            Ammo--;
            if (Def.altIsScope && Def.primary.cycleTime > 0.5f) ZoomLevel = 0; // bolt-action unscopes

            Fired?.Invoke(info);
        }

        /// <summary>Applies this shot's kick and returns the recoil offset the bullet itself uses.</summary>
        /// <remarks>The recoil index is fractional (it recovers continuously between taps), so like
        /// CS:GO we interpolate between neighbouring table entries.</remarks>
        Vector2 ApplyRecoil(float recoilIndex)
        {
            int i0 = Mathf.FloorToInt(recoilIndex);
            float frac = recoilIndex - i0;

            if (Recoil.IsMeasured)
            {
                // Measured patterns describe where the bullet lands, so place the punch exactly there.
                Vector2 m0 = Recoil.GetMeasured(i0), m1 = Recoil.GetMeasured(i0 + 1);
                AimPunchRaw = (m0 + (m1 - m0) * frac) / CsConstants.WeaponRecoilScale;
                _punchVel = Vector2.zero;
                return BulletRecoilDeg;
            }

            // The bullet uses the punch accumulated *before* this shot's kick, as in the engine.
            Vector2 before = BulletRecoilDeg;
            var k0 = Recoil.GetKick(AltMode, i0);
            var k1 = Recoil.GetKick(AltMode, i0 + 1);
            float a = Mathf.Lerp(k0.angleDeg, k1.angleDeg, frac) * Mathf.Deg2Rad;
            float mag = Mathf.Lerp(k0.magnitude, k1.magnitude, frac);
            _punchVel += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (mag * CsConstants.SeededKickScale);
            return before;
        }

        /// <summary>weapon_recoil_decay2: exponential + linear decay of the punch, then velocity integration.</summary>
        void DecayPunch(float dt, bool spraying)
        {
            // A measured pattern is a fixed shape while you hold the trigger; it only recovers after.
            if (Recoil.IsMeasured && spraying) return;

            var p = AimPunchRaw * Mathf.Exp(-CsConstants.RecoilDecay2Exp * dt);
            float len = p.magnitude;
            float lin = CsConstants.RecoilDecay2Lin * dt;
            p = len > lin ? p * (1f - lin / len) : Vector2.zero;

            p += _punchVel * (dt * 0.5f);
            _punchVel *= Mathf.Exp(-CsConstants.RecoilVelDecay * dt);
            p += _punchVel * (dt * 0.5f);

            AimPunchRaw = p;
        }
    }
}
