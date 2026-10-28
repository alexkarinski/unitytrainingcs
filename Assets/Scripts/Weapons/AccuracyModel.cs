using CsAim.Core;
using CsAim.Data;
using UnityEngine;

namespace CsAim.Weapons
{
    /// <summary>What the weapon needs to know about its owner each tick.</summary>
    public struct OwnerState
    {
        public Vector3 velocity;     // source units / s
        public bool onGround;
        public bool ducking;         // fully or partially crouched
        public bool walking;         // shift held
        public bool onLadder;
        public float duckAmount;     // 0..1
    }

    /// <summary>
    /// Inaccuracy and spread, following CS:GO/CS2's CWeaponCSBase::GetInaccuracy / UpdateAccuracyPenalty.
    ///
    ///   inaccuracy = stance base (crouch/stand, blended by duck amount)
    ///              + movement term  inaccuracy_move * curve(speed)
    ///              + air term       inaccuracy_jump * curve(|vz|)     (better at the jump apex)
    ///              + accumulated penalty (inaccuracy_fire per shot, inaccuracy_land on landing)
    ///
    /// Every term is in items_game units (x1000) until the final /1000.
    /// </summary>
    public static class AccuracyModel
    {
        /// <summary>weapon_accuracy_movement curve exponent applied to running (not walking) speed.</summary>
        public const float MovementPower = 0.25f;
        /// <summary>Exponent shaping the jump term over vertical speed.</summary>
        public const float JumpPower = 0.25f;

        public static float StateInaccuracy(WeaponModeStats m, in OwnerState s)
        {
            float inaccuracy = Mathf.Lerp(m.inaccuracyStand, m.inaccuracyCrouch, s.duckAmount);

            if (s.onLadder)
            {
                inaccuracy += m.inaccuracyLadder;
            }
            else
            {
                float maxSpeed = Mathf.Max(1f, m.maxPlayerSpeed);
                float speed = new Vector2(s.velocity.x, s.velocity.z).magnitude;

                // Below 34% of max speed you're fully accurate - the "counter-strafe" window.
                float moveScale = RemapClamped(speed,
                    maxSpeed * CsConstants.AccurateSpeedFraction, maxSpeed * 0.95f, 0f, 1f);
                if (moveScale > 0f)
                {
                    if (!s.walking) moveScale = Mathf.Pow(moveScale, MovementPower);
                    inaccuracy += moveScale * m.inaccuracyMove;
                }

                if (!s.onGround)
                {
                    float vz = Mathf.Abs(s.velocity.y);
                    float airScale = Mathf.Pow(Mathf.Clamp01(vz / CsConstants.JumpImpulse), JumpPower);
                    // Even at the apex you are never ground-accurate.
                    inaccuracy += Mathf.Lerp(m.inaccuracyJump * 0.2f, m.inaccuracyJump, airScale);
                }
            }

            return inaccuracy;
        }

        /// <summary>Recovery time for the current stance, transitioning to the "final" value over a spray.</summary>
        public static float RecoveryTime(WeaponDefinition w, float duckAmount, float recoilIndex)
        {
            float t = 0f;
            if (w.recoveryTransitionEndBullet > w.recoveryTransitionStartBullet)
                t = RemapClamped(recoilIndex, w.recoveryTransitionStartBullet, w.recoveryTransitionEndBullet, 0f, 1f);

            float stand = Mathf.Lerp(w.recoveryTimeStand, w.recoveryTimeStandFinal, t);
            float crouch = Mathf.Lerp(w.recoveryTimeCrouch, w.recoveryTimeCrouchFinal, t);
            return Mathf.Max(0.001f, Mathf.Lerp(stand, crouch, duckAmount));
        }

        /// <summary>
        /// Exponential recovery: after recovery_time seconds 90% of the penalty is gone
        /// (Valve: Lerp(exp(dt * ln(0.1) / recoveryTime), 0, penalty)).
        /// </summary>
        public static float Decay(float value, float dt, float recoveryTime)
        {
            return value * Mathf.Exp(dt * Mathf.Log(0.1f) / recoveryTime);
        }

        /// <summary>
        /// Bullet deviation in tangent space, the "two cone" model: an inaccuracy cone and a
        /// spread cone, each with its own random angle and radius, summed.
        /// Uses the same per-shot stream the trainer seeds, so a spray is reproducible for replay.
        /// </summary>
        public static Vector2 SampleDeviation(ValveUniformRandomStream rng, float inaccuracy, float spread)
        {
            float r1 = rng.RandomFloat(0f, 1f);
            float a1 = rng.RandomFloat(0f, 2f * Mathf.PI);
            float r2 = rng.RandomFloat(0f, 1f);
            float a2 = rng.RandomFloat(0f, 2f * Mathf.PI);

            float i = r1 * inaccuracy;
            float s = r2 * spread;

            return new Vector2(
                Mathf.Cos(a1) * i + Mathf.Cos(a2) * s,
                Mathf.Sin(a1) * i + Mathf.Sin(a2) * s);
        }

        public static float RemapClamped(float v, float a, float b, float c, float d)
        {
            if (Mathf.Approximately(a, b)) return v >= b ? d : c;
            float t = Mathf.Clamp01((v - a) / (b - a));
            return c + (d - c) * t;
        }
    }
}
