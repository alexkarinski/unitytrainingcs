using System;
using System.Collections.Generic;
using CsAim.Combat;
using CsAim.Core;
using CsAim.Data;
using CsAim.Weapons;
using UnityEngine;

namespace CsAim.Player
{
    /// <summary>A fired bullet after hit registration, for scoring and spray analysis.</summary>
    public struct ShotEvent
    {
        public ShotInfo shot;
        public Vector2 eyeAngles;       // pitch up, yaw right - where the player was aiming, no recoil
        public Vector3 origin;
        public Vector3 direction;
        public bool hitSomething;
        public RaycastHit hit;
        public Hitbox hitbox;           // null for world hits
        public HitResult damage;
    }

    /// <summary>
    /// First-person player: CS2 mouse input (counts * sensitivity * 0.022), Source movement on a
    /// fixed 64 Hz tick, and hitscan bullets that leave the eye at eye + aimpunch + spread.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        // ---- Settings (saved to PlayerPrefs by the HUD)
        public float sensitivity = 1.25f;          // CS2 "sensitivity"
        public float zoomSensitivityRatio = 1.0f;  // CS2 "zoom_sensitivity_ratio"
        public bool invertY;
        public float fov4by3 = 90f;                // CS2 fov is fixed at 90 (4:3 horizontal)
        public float dpi = 800f;                   // mouse DPI - only for eDPI / cm per 360 readouts

        /// <summary>eDPI = sensitivity x DPI, the usual way to compare settings between players.</summary>
        public float EDpi => sensitivity * dpi;
        /// <summary>Mouse travel for a full 360 turn: 360 / (sens * 0.022) counts, / DPI inches, * 2.54 cm.</summary>
        public float CmPer360 => sensitivity > 0f && dpi > 0f ? 360f / (sensitivity * CsConstants.MYaw) / dpi * 2.54f : 0f;

        public Camera Camera { get; private set; }
        public Vector2 EyeAngles => _eyeAngles;
        public Vector3 Velocity => _move.Velocity;
        public float Speed2D => new Vector2(_move.Velocity.x, _move.Velocity.z).magnitude;
        public bool OnGround { get; private set; } = true;
        public bool InputEnabled { get; set; } = true;
        public bool Walking { get; private set; }
        public float DuckAmount => _move.DuckAmount;
        public WeaponController Weapon { get; private set; }
        public IReadOnlyList<WeaponDefinition> Weapons => _weapons;
        public int WeaponIndex { get; private set; }

        /// <summary>CS2-style key bindings (defaults = stock CS2, can be loaded from your cfg).</summary>
        public InputBindings Bindings { get; } = new InputBindings();
        public bool AttackHeld => InputEnabled && Bindings.Held(InputAction.Attack);

        public event Action<ShotEvent> Shot;
        public event Action<WeaponController> WeaponChanged;

        readonly SourceMovement _move = new SourceMovement();
        readonly List<WeaponDefinition> _weapons = new List<WeaponDefinition>();
        CharacterController _cc;
        Vector2 _eyeAngles;
        float _tickAccumulator;
        float _simTime;
        bool _attackPressedLatch;
        bool _jumpLatch;
        int _previousWeapon;
        bool _blockFire;

        public void Init(Camera cam, List<WeaponDefinition> weapons)
        {
            Camera = cam;
            _weapons.Clear();
            _weapons.AddRange(weapons);

            _cc = GetComponent<CharacterController>();
            _cc.radius = CsConstants.HullRadius;
            _cc.height = CsConstants.StandHullHeight;
            _cc.center = new Vector3(0f, CsConstants.StandHullHeight * 0.5f, 0f);
            _cc.stepOffset = 18f;
            _cc.slopeLimit = 45.57f;
            _cc.skinWidth = 0.5f;
            _cc.minMoveDistance = 0f;

            // Re-initialising (after an import) must replace the current weapon even at index 0.
            if (Weapon != null) Weapon.Fired -= OnWeaponFired;
            Weapon = null;
            WeaponIndex = 0;
            SelectWeapon(0);
        }

        public void SelectWeapon(int index)
        {
            if (_weapons.Count == 0) return;
            index = Mathf.Clamp(index, 0, _weapons.Count - 1);
            if (Weapon != null && index == WeaponIndex) return;

            _previousWeapon = WeaponIndex;
            WeaponIndex = index;
            if (Weapon != null) Weapon.Fired -= OnWeaponFired;
            Weapon = new WeaponController(_weapons[index]);
            Weapon.Fired += OnWeaponFired;
            WeaponChanged?.Invoke(Weapon);
        }

        public void Teleport(Vector3 feet, float yaw, float pitch = 0f)
        {
            _cc.enabled = false;
            transform.position = feet;
            _cc.enabled = true;
            _move.Velocity = Vector3.zero;
            _eyeAngles = new Vector2(pitch, yaw);
        }

        public void SetEyeAngles(Vector2 pitchYaw) => _eyeAngles = pitchYaw;

        /// <summary>Ignore the trigger until the player lets go of it (restart after a failed attempt).</summary>
        public void BlockFireUntilRelease() => _blockFire = true;

        public Vector3 EyePosition =>
            transform.position + Vector3.up * Mathf.Lerp(CsConstants.StandEyeHeight, CsConstants.DuckEyeHeight, _move.DuckAmount);

        void Update()
        {
            if (Camera == null) return;

            if (InputEnabled)
            {
                ReadMouse();
                ReadButtons();
            }

            // Fixed 64 Hz simulation, like the game server.
            _tickAccumulator += Mathf.Min(Time.deltaTime, 0.25f);
            while (_tickAccumulator >= CsConstants.TickInterval)
            {
                _tickAccumulator -= CsConstants.TickInterval;
                _simTime += CsConstants.TickInterval;
                SimulateTick(CsConstants.TickInterval);
            }

            UpdateCamera();
        }

        float CurrentFov4by3()
        {
            var w = Weapon;
            if (w == null || !w.Def.altIsScope || w.ZoomLevel == 0) return fov4by3;
            return w.ZoomLevel == 1 ? w.Def.zoomFov.x : w.Def.zoomFov.y;
        }

        void ReadMouse()
        {
            float dx = Input.GetAxisRaw("Mouse X");
            float dy = Input.GetAxisRaw("Mouse Y");

            // Zoomed sensitivity: CS2 scales by (zoom fov / default fov) * zoom_sensitivity_ratio.
            float fov = CurrentFov4by3();
            float zoomScale = fov < fov4by3 ? fov / fov4by3 * zoomSensitivityRatio : 1f;
            float s = sensitivity * zoomScale;

            _eyeAngles.y += dx * s * CsConstants.MYaw;
            _eyeAngles.x += (invertY ? -dy : dy) * s * CsConstants.MPitch;
            _eyeAngles.x = Mathf.Clamp(_eyeAngles.x, -CsConstants.MaxPitch, CsConstants.MaxPitch);
            _eyeAngles.y = Mathf.Repeat(_eyeAngles.y + 180f, 360f) - 180f;
        }

        void ReadButtons()
        {
            var b = Bindings;
            if (b.Pressed(InputAction.Attack)) _attackPressedLatch = true;
            if (b.Pressed(InputAction.Jump)) _jumpLatch = true;
            if (b.Pressed(InputAction.Attack2)) Weapon?.ToggleAlt();
            if (b.Pressed(InputAction.Reload)) Weapon?.StartReload();
            if (b.Pressed(InputAction.LastInv)) SelectWeapon(_previousWeapon);
            if (_weapons.Count > 0)
            {
                if (b.Pressed(InputAction.InvNext)) SelectWeapon((WeaponIndex + 1) % _weapons.Count);
                if (b.Pressed(InputAction.InvPrev)) SelectWeapon((WeaponIndex + _weapons.Count - 1) % _weapons.Count);
            }

            for (int i = 0; i < 9; i++)
                if (b.Pressed(InputAction.Slot1 + i)) SelectWeapon(i);
        }

        void SimulateTick(float dt)
        {
            bool input = InputEnabled;
            var b = Bindings;
            Walking = input && b.Held(InputAction.Walk);
            bool duck = input && b.Held(InputAction.Duck);

            // Wish direction in the yaw plane (pitch never affects ground movement).
            Vector3 wish = Vector3.zero;
            if (input)
            {
                float fwd = (b.Held(InputAction.Forward) ? 1f : 0f) - (b.Held(InputAction.Back) ? 1f : 0f);
                float side = (b.Held(InputAction.Right) ? 1f : 0f) - (b.Held(InputAction.Left) ? 1f : 0f);
                var yawRot = Quaternion.Euler(0f, _eyeAngles.y, 0f);
                wish = yawRot * new Vector3(side, 0f, fwd);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
            }

            float maxSpeed = Weapon != null ? Weapon.Mode.maxPlayerSpeed : 250f;
            if (maxSpeed <= 0f) maxSpeed = 250f;
            float wishSpeed = wish.sqrMagnitude > 0f ? maxSpeed : 0f;
            if (_move.DuckAmount > 0.5f) wishSpeed *= CsConstants.DuckSpeedModifier;
            else if (Walking) wishSpeed *= CsConstants.WalkSpeedModifier;

            bool jump = _jumpLatch && OnGround && _move.DuckAmount < 0.5f;
            _jumpLatch = false;

            _move.Tick(dt, wish.normalized, wishSpeed, OnGround, jump, duck);
            ApplyHull();

            // StayOnGround: push down a little while grounded so the controller keeps reporting
            // ground contact instead of flickering airborne every other tick.
            Vector3 delta = _move.Velocity * dt;
            if (OnGround && !jump) delta.y -= 2f;

            Vector3 before = transform.position;
            var flags = _cc.Move(delta);
            Vector3 actual = (transform.position - before) / dt;
            _move.FinishGravity(dt);

            OnGround = (flags & CollisionFlags.Below) != 0 || (_cc.isGrounded && _move.Velocity.y <= 0f);
            if ((flags & CollisionFlags.Sides) != 0)
            {
                _move.Velocity.x = actual.x;
                _move.Velocity.z = actual.z;
            }
            if ((flags & CollisionFlags.Above) != 0 && _move.Velocity.y > 0f) _move.Velocity.y = 0f;
            if (OnGround && _move.Velocity.y < 0f) _move.Velocity.y = 0f;

            if (Weapon != null)
            {
                var owner = new OwnerState
                {
                    velocity = _move.Velocity,
                    onGround = OnGround,
                    ducking = _move.DuckAmount > 0f,
                    walking = Walking,
                    duckAmount = _move.DuckAmount,
                };
                bool rawHeld = input && b.Held(InputAction.Attack);
                if (!rawHeld) _blockFire = false;
                bool held = rawHeld && !_blockFire;
                Weapon.Tick(_simTime, dt, owner, held, _attackPressedLatch && !_blockFire);
            }
            _attackPressedLatch = false;
        }

        void ApplyHull()
        {
            float h = Mathf.Lerp(CsConstants.StandHullHeight, CsConstants.DuckHullHeight, _move.DuckAmount);
            _cc.height = h;
            _cc.center = new Vector3(0f, h * 0.5f, 0f);
        }

        void UpdateCamera()
        {
            Vector2 view = _eyeAngles + (Weapon != null ? Weapon.ViewRecoilDeg : Vector2.zero);
            Camera.transform.SetPositionAndRotation(EyePosition, Quaternion.Euler(-view.x, view.y, 0f));

            // CS2's fov is horizontal at 4:3 (Hor+), so the vertical fov is aspect-independent.
            float h = CurrentFov4by3() * Mathf.Deg2Rad;
            Camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(h * 0.5f) * 0.75f) * Mathf.Rad2Deg;
        }

        void OnWeaponFired(ShotInfo info)
        {
            Vector2 a = _eyeAngles + info.recoilDeg + info.spreadDeg;
            Vector3 dir = Quaternion.Euler(-a.x, a.y, 0f) * Vector3.forward;
            Vector3 origin = EyePosition;

            var e = new ShotEvent { shot = info, eyeAngles = _eyeAngles, origin = origin, direction = dir };

            if (Physics.Raycast(origin, dir, out var hit, Weapon.Def.range, ~0, QueryTriggerInteraction.Ignore))
            {
                e.hitSomething = true;
                e.hit = hit;
                var hb = hit.collider.GetComponent<Hitbox>();
                if (hb != null && hb.owner != null && hb.owner.Alive)
                {
                    e.hitbox = hb;
                    var res = new HitResult
                    {
                        group = hb.group,
                        distance = hit.distance,
                        point = hit.point,
                        normal = hit.normal,
                        shotIndex = info.shotIndex,
                        damage = DamageModel.Compute(Weapon.Def, hb.group, hit.distance, hb.owner.HasArmor, hb.owner.HasHelmet),
                    };
                    hb.owner.ApplyHit(res);
                    res.killed = !hb.owner.Alive;
                    e.damage = res;
                }
            }

            Shot?.Invoke(e);
        }
    }
}
