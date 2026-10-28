using System.Collections.Generic;
using CsAim.Core;
using CsAim.Data;
using CsAim.Player;
using CsAim.Weapons;
using UnityEngine;

namespace CsAim.Training
{
    /// <summary>
    /// Scores spray control the way the guide frames it: a bullet's offset from where the spray
    /// started is (your mouse movement + recoil) + spread. Recoil is deterministic and is yours to
    /// cancel; spread is random and is not. So we report the two separately:
    ///
    ///   recoil error  = | mouse delta + recoil |   (0 = perfect compensation)
    ///   spread        = | spread |                 (luck, shown for context only)
    /// </summary>
    public sealed class SprayAnalyzer
    {
        public struct ShotSample
        {
            public Vector2 mouseDelta;   // eye angles relative to the first shot
            public Vector2 recoil;
            public Vector2 spread;
            public Vector2 Landing => mouseDelta + recoil + spread;
            public Vector2 RecoilResidual => mouseDelta + recoil;
        }

        public sealed class Spray
        {
            public string weapon;
            public readonly List<ShotSample> shots = new List<ShotSample>();
            public float MeanRecoilError;
            public float MeanRecoil;
            public float MeanSpread;
            public float CompensationPercent;   // 100 = recoil fully cancelled

            public void Finish()
            {
                if (shots.Count == 0) return;
                float err = 0f, rec = 0f, spr = 0f;
                foreach (var s in shots)
                {
                    err += s.RecoilResidual.magnitude;
                    rec += s.recoil.magnitude;
                    spr += s.spread.magnitude;
                }
                MeanRecoilError = err / shots.Count;
                MeanRecoil = rec / shots.Count;
                MeanSpread = spr / shots.Count;
                CompensationPercent = MeanRecoil > 0.01f
                    ? Mathf.Clamp(100f * (1f - MeanRecoilError / MeanRecoil), 0f, 100f)
                    : 100f;
            }
        }

        public Spray Current { get; private set; }
        public Spray Last { get; private set; }
        public float BestCompensation { get; private set; }
        public int MinShotsToScore = 10;

        Vector2 _startEye;

        public void OnShot(in ShotEvent e, string weaponName)
        {
            if (e.shot.shotIndex == 0 || Current == null)
            {
                FinishCurrent();
                Current = new Spray { weapon = weaponName };
                _startEye = e.eyeAngles;
            }

            Vector2 delta = e.eyeAngles - _startEye;
            delta.y = Mathf.DeltaAngle(0f, delta.y);
            Current.shots.Add(new ShotSample { mouseDelta = delta, recoil = e.shot.recoilDeg, spread = e.shot.spreadDeg });
        }

        /// <summary>Call when the trigger is released.</summary>
        public void FinishCurrent()
        {
            if (Current == null) return;
            Current.Finish();
            if (Current.shots.Count >= MinShotsToScore)
            {
                Last = Current;
                BestCompensation = Mathf.Max(BestCompensation, Current.CompensationPercent);
            }
            Current = null;
        }

        /// <summary>
        /// The weapon's pure recoil pattern: fire a full magazine standing still without touching
        /// the mouse. Negating it gives the ideal mouse path - the "drawing" the guide teaches.
        /// </summary>
        public static List<Vector2> PreviewPattern(WeaponDefinition def, int shots = 0)
        {
            var result = new List<Vector2>();
            var w = new WeaponController(def, spreadSeed: 1) { InfiniteAmmo = false };
            w.Fired += info => result.Add(info.recoilDeg);

            if (shots <= 0) shots = def.clipSize;
            float t = 0f, dt = CsConstants.TickInterval;
            var still = new OwnerState { onGround = true };
            for (int i = 0; i < 2000 && result.Count < shots; i++)
            {
                t += dt;
                // Semi-autos are "clicked" every tick, which fires them at their cycle time.
                w.Tick(t, dt, still, attackHeld: true, attackPressed: true);
            }
            return result;
        }
    }
}
