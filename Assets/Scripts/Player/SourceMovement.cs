using CsAim.Core;
using UnityEngine;

namespace CsAim.Player
{
    /// <summary>
    /// Source-engine ground/air movement (CGameMovement): friction, accelerate, air-accelerate
    /// with the 30 u/s wish cap, gravity and jump impulse, using CS cvar defaults.
    ///
    /// This matters for an aim trainer because accuracy depends on velocity: counter-strafing
    /// only works if deceleration matches the game, i.e. friction 5.2 / stopspeed 80.
    /// Unity axes: +Y is up (Source's +Z).
    /// </summary>
    public sealed class SourceMovement
    {
        public Vector3 Velocity;
        public float DuckAmount;     // 0 = standing, 1 = fully crouched

        bool _airborneGravity;

        /// <summary>Velocity update for one tick. Move the body by Velocity * dt, then call FinishGravity.</summary>
        public void Tick(float dt, Vector3 wishDir, float wishSpeed, bool onGround, bool jump, bool duckHeld)
        {
            _airborneGravity = false;
            // Crouch transition takes DuckTime seconds in either direction.
            DuckAmount = Mathf.MoveTowards(DuckAmount, duckHeld ? 1f : 0f, dt / CsConstants.DuckTime);

            if (onGround)
            {
                if (jump)
                {
                    // Impulse minus StartGravity's half; FinishGravity applies the other half after
                    // the move, so position integrates exactly to the 57-unit apex.
                    Velocity.y = CsConstants.JumpImpulse - CsConstants.SvGravity * 0.5f * dt;
                    _airborneGravity = true;
                    // No friction on the tick you jump (bunnyhop rule).
                    AirAccelerate(wishDir, wishSpeed, dt);
                }
                else
                {
                    Velocity.y = 0f;
                    Friction(dt);
                    Accelerate(wishDir, wishSpeed, CsConstants.SvAccelerate, dt);
                }
            }
            else
            {
                // StartGravity: half the tick's gravity before the move, the rest in FinishGravity.
                Velocity.y -= CsConstants.SvGravity * 0.5f * dt;
                _airborneGravity = true;
                AirAccelerate(wishDir, wishSpeed, dt);
            }

            // sv_maxspeed clamp on the horizontal plane.
            var flat = new Vector2(Velocity.x, Velocity.z);
            if (flat.magnitude > CsConstants.SvMaxSpeed)
            {
                flat = flat.normalized * CsConstants.SvMaxSpeed;
                Velocity.x = flat.x;
                Velocity.z = flat.y;
            }
        }

        /// <summary>Second half of the tick's gravity; call after the position has been moved.</summary>
        public void FinishGravity(float dt)
        {
            if (_airborneGravity) Velocity.y -= CsConstants.SvGravity * 0.5f * dt;
            _airborneGravity = false;
        }

        void Friction(float dt)
        {
            float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
            if (speed < 0.1f)
            {
                Velocity.x = Velocity.z = 0f;
                return;
            }

            float control = speed < CsConstants.SvStopSpeed ? CsConstants.SvStopSpeed : speed;
            float drop = control * CsConstants.SvFriction * dt;
            float newSpeed = Mathf.Max(speed - drop, 0f) / speed;
            Velocity.x *= newSpeed;
            Velocity.z *= newSpeed;
        }

        void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            float current = Velocity.x * wishDir.x + Velocity.z * wishDir.z;
            float add = wishSpeed - current;
            if (add <= 0f) return;

            float accelSpeed = Mathf.Min(accel * dt * wishSpeed, add);
            Velocity.x += accelSpeed * wishDir.x;
            Velocity.z += accelSpeed * wishDir.z;
        }

        void AirAccelerate(Vector3 wishDir, float wishSpeed, float dt)
        {
            float capped = Mathf.Min(wishSpeed, CsConstants.SvAirMaxWishSpeed);
            float current = Velocity.x * wishDir.x + Velocity.z * wishDir.z;
            float add = capped - current;
            if (add <= 0f) return;

            float accelSpeed = Mathf.Min(CsConstants.SvAirAccelerate * wishSpeed * dt, add);
            Velocity.x += accelSpeed * wishDir.x;
            Velocity.z += accelSpeed * wishDir.z;
        }
    }
}
