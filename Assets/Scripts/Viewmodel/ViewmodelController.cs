using System.Collections.Generic;
using CsAim.Core;
using CsAim.Player;
using CsAim.Weapons;
using UnityEngine;

namespace CsAim.Viewmodel
{
    /// <summary>
    /// First-person weapon and arms, drawn by a second camera on top of the world (like CS, so the
    /// gun never clips into walls). Animation is driven by the real simulation: recoil kicks on the
    /// weapon's actual shots, bob from actual velocity, reload from the real reload timer.
    /// Settings mirror CS2: viewmodel_fov, viewmodel_offset_x/y/z and switchhands.
    /// </summary>
    public sealed class ViewmodelController : MonoBehaviour
    {
        // CS2 defaults
        public float viewmodelFov = 68f;
        public Vector3 offset = new Vector3(2.5f, 0f, -1.5f);
        public bool leftHanded;
        public bool visible = true;

        /// <summary>
        /// Base placement (camera space, units) so CS2's default offsets look right at viewmodel_fov 68:
        /// rifles sit in the lower right with the grip hand at the corner and the muzzle just below
        /// and right of the crosshair; pistols closer to the centre.
        /// </summary>
        static Vector3 BasePosition(WeaponKind kind) =>
            kind == WeaponKind.Pistol ? new Vector3(4.5f, -4.6f, 17f) : new Vector3(7f, -6f, 22f);
        const float InwardYaw = -3f;

        PlayerController _player;
        Camera _vmCamera;
        Transform _rigRoot;     // offset + handedness
        Transform _pivot;       // sway, bob, recoil, draw, reload, inspect
        ViewmodelRig _rig;
        Transform _flash;
        float _flashUntil;

        // springs (position in units, rotation in degrees)
        Vector3 _kickPos, _kickPosVel, _kickRot, _kickRotVel;
        Vector2 _sway, _swayVel;
        float _bobPhase, _bobAmount;
        float _drawStart = -10f;
        float _inspectStart = -10f;
        bool _wasReloading;

        // Player-imported model (from their own CS2 install) replacing the procedural gun.
        GameObject _customModel;
        Bounds _proceduralBounds;
        int _loadToken;
        public string CustomModelStatus { get; private set; } = string.Empty;
        public bool HasCustomModel => _customModel != null;
        string CurrentWeaponId => _player != null && _player.Weapon != null ? _player.Weapon.Def.weaponId : string.Empty;

        struct ShellState { public Transform t; public Vector3 vel; public Vector3 spin; public float dieAt; }
        readonly List<ShellState> _shells = new List<ShellState>();
        readonly Stack<Transform> _shellPool = new Stack<Transform>();

        public void Init(PlayerController player, Camera worldCamera)
        {
            _player = player;

            worldCamera.cullingMask &= ~(1 << ViewmodelBuilder.Layer);

            var camGo = new GameObject("ViewmodelCamera");
            camGo.transform.SetParent(worldCamera.transform, false);
            _vmCamera = camGo.AddComponent<Camera>();
            _vmCamera.clearFlags = CameraClearFlags.Depth;
            _vmCamera.cullingMask = 1 << ViewmodelBuilder.Layer;
            _vmCamera.depth = worldCamera.depth + 1;
            _vmCamera.nearClipPlane = 0.5f;
            _vmCamera.farClipPlane = 300f;
            camGo.AddComponent<ViewmodelCullingFlip>().controller = this;

            _rigRoot = new GameObject("ViewmodelRoot").transform;
            _rigRoot.SetParent(camGo.transform, false);

            player.WeaponChanged += OnWeaponChanged;
            player.Shot += OnShot;
            CustomWeaponModels.Changed += id => { if (id == CurrentWeaponId) OnWeaponChanged(_player.Weapon); };
            if (player.Weapon != null) OnWeaponChanged(player.Weapon);
        }

        public bool LeftHandedRender => leftHanded;

        void OnWeaponChanged(WeaponController w)
        {
            if (_pivot != null) Destroy(_pivot.gameObject);
            foreach (var s in _shells) if (s.t != null) Destroy(s.t.gameObject);
            _shells.Clear();
            _shellPool.Clear();

            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(_rigRoot, false);
            _rig = ViewmodelBuilder.Build(w.Def, _pivot);
            _flash = ViewmodelBuilder.MuzzleFlash(_rig.muzzle);
            _proceduralBounds = CustomModelLoader.BoundsIn(_rig.weapon, _rig.weapon, r => !IsHandPart(r.transform));
            _customModel = null;
            TryLoadCustomModel(w.Def.weaponId);
            _drawStart = Time.time;
            _inspectStart = -10f;
            _kickPos = _kickPosVel = _kickRot = _kickRotVel = Vector3.zero;
        }

        bool IsHandPart(Transform t) =>
            _rig != null && (t.IsChildOf(_rig.leftHand) || t.IsChildOf(_rig.rightHand));

        async void TryLoadCustomModel(string weaponId)
        {
            int token = ++_loadToken;
            CustomModelStatus = string.Empty;
            if (!CustomWeaponModels.TryGetSource(weaponId, out var glb, out var gltfPath)) return;

            CustomModelStatus = "загружается…";
            GameObject model = null;
            try
            {
                model = await CustomModelLoader.LoadAsync(glb, gltfPath, _rig.weapon);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CS Aim] custom model for {weaponId} failed: {e.Message}");
            }

            // The weapon may have changed while loading.
            if (token != _loadToken || _rig == null || model == null || model.transform.parent != _rig.weapon)
            {
                if (model != null) Destroy(model);
                if (token == _loadToken) CustomModelStatus = "не удалось загрузить файл";
                return;
            }

            _customModel = model;
            RefitCustomModel();

            // Hide the procedural gun; hands and anchors stay.
            foreach (var r in _rig.weapon.GetComponentsInChildren<Renderer>(true))
                if (!IsHandPart(r.transform) && !r.transform.IsChildOf(model.transform)) r.enabled = false;
            CustomModelStatus = "своя модель";
        }

        /// <summary>Re-applies the automatic fit plus the saved manual corrections.</summary>
        public void RefitCustomModel()
        {
            if (_customModel == null) return;
            CustomModelLoader.Fit(_customModel, _rig.weapon, _proceduralBounds, CustomWeaponModels.GetFit(CurrentWeaponId));
        }

        void OnShot(ShotEvent e)
        {
            if (_rig == null) return;
            // Recoil animation: an impulse into a spring, slightly randomised like the game's.
            _kickPosVel += new Vector3(0f, 0.15f, -_rig.kickBack) * 60f;
            _kickRotVel += new Vector3(-_rig.kickPitch, Random.Range(-0.8f, 0.8f), Random.Range(-1.2f, 1.2f)) * 60f;
            _inspectStart = -10f;

            bool silenced = _rig.suppressor != null && _player.Weapon.AltMode;
            if (!silenced)
            {
                _flashUntil = Time.time + 0.035f;
                _flash.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            }
            if (_rig.kind != WeaponKind.Sniper) SpawnShell();
        }

        void Update()
        {
            if (_player == null || _rig == null) return;
            var w = _player.Weapon;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            if (_player.InputEnabled)
            {
                if (_player.Bindings.Pressed(InputAction.Inspect) && !w.Reloading) _inspectStart = Time.time;
                if (_player.Bindings.Pressed(InputAction.SwitchHands)) leftHanded = !leftHanded;
            }

            // Scoped: CS hides the viewmodel and shows the scope overlay instead.
            bool scoped = w.Def.altIsScope && w.ZoomLevel > 0;
            _rigRoot.gameObject.SetActive(visible && !scoped);
            if (_rig.suppressor != null) _rig.suppressor.gameObject.SetActive(w.AltMode);

            _vmCamera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(viewmodelFov * 0.5f * Mathf.Deg2Rad) * 0.75f) * Mathf.Rad2Deg;
            _rigRoot.localPosition = BasePosition(_rig.kind) + new Vector3(offset.x - 2.5f, offset.y, offset.z + 1.5f);
            _rigRoot.localRotation = Quaternion.Euler(0f, InwardYaw, 0f);

            // --- springs: recoil kick
            Spring(ref _kickPos, ref _kickPosVel, 260f, 26f, dt);
            Spring(ref _kickRot, ref _kickRotVel, 260f, 24f, dt);

            // --- sway: the gun lags behind mouse movement
            Vector2 look = _player.LookDeltaThisFrame;
            Vector2 swayTarget = new Vector2(Mathf.Clamp(-look.x * 0.6f, -2.5f, 2.5f), Mathf.Clamp(-look.y * 0.6f, -2.5f, 2.5f));
            _sway = Vector2.SmoothDamp(_sway, swayTarget, ref _swayVel, 0.08f, Mathf.Infinity, dt);

            // --- bob: from real horizontal speed, only on the ground
            float speed = _player.Speed2D;
            float targetBob = _player.OnGround ? Mathf.Clamp01(speed / 250f) : 0f;
            _bobAmount = Mathf.MoveTowards(_bobAmount, targetBob, dt * 4f);
            _bobPhase += dt * Mathf.Lerp(6f, 11f, Mathf.Clamp01(speed / 250f));
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.45f, -Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.4f, 0f) * _bobAmount;

            // --- jump / crouch
            float air = _player.OnGround ? 0f : Mathf.Clamp(-_player.Velocity.y / 300f, -1f, 1f);
            Vector3 stance = new Vector3(0f, air * 0.9f - _player.DuckAmount * 0.6f, 0f);

            // --- deploy: rise from below when switching weapons
            float draw = Mathf.Clamp01((Time.time - _drawStart) / 0.45f);
            float drawEase = 1f - (1f - draw) * (1f - draw) * (1f - draw);
            Vector3 drawPos = new Vector3(0f, -9f, -3f) * (1f - drawEase);
            Vector3 drawRot = new Vector3(45f, 0f, -15f) * (1f - drawEase);

            // --- reload choreography from the real reload timer
            Vector3 relPos = Vector3.zero, relRot = Vector3.zero;
            Vector3 leftTarget = _rig.leftHome;
            Vector3 magPos = _rig.magazineHome;
            bool magVisible = true;
            if (w.Reloading)
            {
                if (!_wasReloading) _inspectStart = -10f;
                AnimateReload(w.ReloadProgress, ref relPos, ref relRot, ref leftTarget, ref magPos, ref magVisible);
            }
            _wasReloading = w.Reloading;

            // --- inspect
            Vector3 inspRot = Vector3.zero, inspPos = Vector3.zero;
            float it = (Time.time - _inspectStart) / 3.0f;
            if (it >= 0f && it < 1f)
            {
                float turn = Bump(it, 0.08f, 0.3f, 0.75f, 0.95f);
                float flip = Bump(it, 0.35f, 0.45f, 0.6f, 0.7f);
                inspRot = new Vector3(-10f * turn, -55f * turn, 35f * turn + 25f * flip);
                inspPos = new Vector3(-3.5f * turn, 1.5f * turn, -1f * turn);
            }

            _pivot.localPosition = _kickPos + new Vector3(_sway.y * 0.6f, _sway.x * 0.6f, 0f) + bob + stance + drawPos + relPos + inspPos;
            _pivot.localRotation = Quaternion.Euler(_kickRot + new Vector3(-_sway.x * 1.2f, _sway.y * 1.6f, -_sway.y * 1.2f) + drawRot + relRot + inspRot);

            // Hands, magazine, bolt
            _rig.leftHand.localPosition = Vector3.Lerp(_rig.leftHand.localPosition, leftTarget, 1f - Mathf.Exp(-25f * dt));
            _rig.magazine.localPosition = magPos;
            _rig.magazine.gameObject.SetActive(magVisible);
            if (_rig.bolt != null)
            {
                // Bolt / slide cycles back for a moment on each shot, and on the charge at reload end.
                float back = Mathf.Clamp01(-_kickPos.z / Mathf.Max(0.1f, _rig.kickBack)) * (_rig.kind == WeaponKind.Pistol ? 1.6f : 1.2f);
                _rig.bolt.localPosition = _rig.boltHome + new Vector3(0, 0, -back);
            }

            UpdateLimbs();

            _flash.gameObject.SetActive(Time.time < _flashUntil);
            UpdateShells(dt);
        }

        /// <summary>
        /// Gun tilts, the left hand pulls the magazine down and away, comes back with a fresh one,
        /// seats it, then returns to the handguard. Timeline is a fraction of reload_time.
        /// </summary>
        void AnimateReload(float p, ref Vector3 pos, ref Vector3 rot, ref Vector3 leftTarget, ref Vector3 magPos, ref bool magVisible)
        {
            float tilt = Bump(p, 0.02f, 0.15f, 0.8f, 0.95f);
            pos = new Vector3(-1.0f, -1.8f, -1.0f) * tilt;
            rot = new Vector3(8f, 0f, 28f) * tilt;

            Vector3 magHand = _rig.magazineHome + new Vector3(0f, _rig.kind == WeaponKind.Pistol ? -2.4f : -3.5f, 0.3f);
            Vector3 pouch = magHand + new Vector3(-3f, -12f, -4f);

            if (p < 0.12f) leftTarget = _rig.leftHome;
            else if (p < 0.25f) leftTarget = magHand;                                     // grab
            else if (p < 0.45f) leftTarget = Vector3.Lerp(magHand, pouch, Smooth(p, 0.25f, 0.4f));   // pull out, away
            else if (p < 0.7f) leftTarget = Vector3.Lerp(pouch, magHand, Smooth(p, 0.45f, 0.65f));   // fresh mag in
            else if (p < 0.82f) leftTarget = magHand;                                     // seat
            else leftTarget = _rig.leftHome;

            if (p >= 0.25f && p < 0.7f)
            {
                // The magazine travels with the hand: out with the old one, back with the new one.
                Vector3 handOffset = leftTarget - magHand;
                magPos = _rig.magazineHome + handOffset;
                magVisible = !(p > 0.4f && p < 0.47f);   // swap happens off-screen
            }
            else if (p >= 0.7f && p < 0.76f)
            {
                magPos = _rig.magazineHome + new Vector3(0f, -0.6f * (1f - Smooth(p, 0.7f, 0.76f)), 0f);
            }

            // Charging handle / slide at the end.
            if (_rig.bolt != null && p > 0.84f && p < 0.94f) _kickPos += new Vector3(0, 0, -0.02f);
        }

        void UpdateLimbs()
        {
            // Wrist points in rig (pivot parent) space follow the hands; elbows stay put.
            Vector3 rw = _rigRoot.InverseTransformPoint(_rig.rightHand.TransformPoint(new Vector3(0.9f, -0.4f, -1.4f)));
            Vector3 lw = _rigRoot.InverseTransformPoint(_rig.leftHand.TransformPoint(new Vector3(-0.9f, -0.6f, -1.4f)));
            ViewmodelBuilder.PlaceLimb(_rig.rightForearm, rw, _rig.rightElbow, 0f, 0.55f, 2.5f);
            ViewmodelBuilder.PlaceLimb(_rig.rightSleeve, rw, _rig.rightElbow, 0.5f, 1.1f, 3.3f);
            ViewmodelBuilder.PlaceLimb(_rig.leftForearm, lw, _rig.leftElbow, 0f, 0.55f, 2.5f);
            ViewmodelBuilder.PlaceLimb(_rig.leftSleeve, lw, _rig.leftElbow, 0.5f, 1.1f, 3.3f);
        }

        void SpawnShell()
        {
            Transform t = _shellPool.Count > 0 ? _shellPool.Pop() : ViewmodelBuilder.Shell(_rigRoot);
            t.gameObject.SetActive(true);
            t.position = _rig.ejectPort.position;
            t.localRotation = Quaternion.Euler(Random.Range(0, 360f), 0, 90f);
            _shells.Add(new ShellState
            {
                t = t,
                vel = new Vector3(Random.Range(28f, 40f), Random.Range(22f, 32f), Random.Range(-8f, 2f)),
                spin = new Vector3(Random.Range(500f, 900f), Random.Range(-200f, 200f), 0f),
                dieAt = Time.time + 0.6f,
            });
        }

        void UpdateShells(float dt)
        {
            for (int i = _shells.Count - 1; i >= 0; i--)
            {
                var s = _shells[i];
                if (s.t == null) { _shells.RemoveAt(i); continue; }
                if (Time.time >= s.dieAt)
                {
                    s.t.gameObject.SetActive(false);
                    _shellPool.Push(s.t);
                    _shells.RemoveAt(i);
                    continue;
                }
                s.vel += new Vector3(0f, -260f, 0f) * dt;
                s.t.localPosition += s.vel * dt;
                s.t.localRotation *= Quaternion.Euler(s.spin * dt);
                _shells[i] = s;
            }
        }

        // ------------------------------------------------------------------ helpers

        static void Spring(ref Vector3 x, ref Vector3 v, float k, float c, float dt)
        {
            v += (-k * x - c * v) * dt;
            x += v * dt;
        }

        static float Smooth(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        /// <summary>0 before a, rises to 1 by b, holds, falls back to 0 between c and d.</summary>
        static float Bump(float t, float a, float b, float c, float d) =>
            t < a || t > d ? 0f : t < b ? Smooth(t, a, b) : t < c ? 1f : 1f - Smooth(t, c, d);
    }

    /// <summary>Mirrors the viewmodel camera for left-handed mode (flips culling so faces stay correct).</summary>
    public sealed class ViewmodelCullingFlip : MonoBehaviour
    {
        public ViewmodelController controller;
        Camera _cam;

        void Awake() => _cam = GetComponent<Camera>();

        void OnPreCull()
        {
            _cam.ResetProjectionMatrix();
            if (controller != null && controller.LeftHandedRender)
                _cam.projectionMatrix = _cam.projectionMatrix * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
        }

        void OnPreRender() { if (controller != null && controller.LeftHandedRender) GL.invertCulling = true; }
        void OnPostRender() { GL.invertCulling = false; }
    }
}
