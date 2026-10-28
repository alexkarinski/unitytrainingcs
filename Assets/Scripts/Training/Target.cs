using System;
using CsAim.Combat;
using CsAim.Core;
using CsAim.Player;
using UnityEngine;

namespace CsAim.Training
{
    public enum TargetMotion { Static, Strafe, Peek }

    /// <summary>
    /// A humanoid target sized like a CS2 player model (72 units, head centre at ~65), with
    /// per-hitgroup colliders so headshots, stomach and leg multipliers behave like the game.
    /// Strafing targets use the same Source movement as the player, so their acceleration and
    /// counter-strafe stops look like a real opponent's ADAD.
    /// </summary>
    public sealed class Target : MonoBehaviour, IDamageable
    {
        public int Health { get; private set; } = 100;
        public bool Alive => Health > 0;
        public bool HasArmor { get; set; } = true;
        public bool HasHelmet { get; set; } = true;
        public TargetMotion Motion { get; set; }
        public float SpawnTime { get; private set; }
        public float StrafeSpeed { get; set; } = 250f;
        public float StrafeHalfWidth { get; set; } = 120f;

        public event Action<Target, HitResult> Hit;
        public event Action<Target> Killed;

        readonly SourceMovement _move = new SourceMovement();
        Vector3 _anchor;
        Vector3 _right;
        float _dir = 1f;
        float _nextFlip;
        Renderer[] _renderers;
        Color[] _baseColors;
        float _flashUntil;

        static Material _bodyMat, _headMat;

        public static Target Create(Vector3 feet, Vector3 facing, TargetMotion motion)
        {
            var go = new GameObject("Target");
            go.transform.position = feet;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z));
            var t = go.AddComponent<Target>();
            t.Motion = motion;
            t.Build();
            return t;
        }

        void Build()
        {
            if (_bodyMat == null)
            {
                _bodyMat = Materials.Lit(new Color(0.55f, 0.47f, 0.33f));
                _headMat = Materials.Lit(new Color(0.85f, 0.7f, 0.55f));
            }

            // Dimensions in Source units, approximating CS2 hitboxes.
            Part(PrimitiveType.Sphere, HitGroup.Head, new Vector3(0, 65f, 0), new Vector3(9.5f, 10.5f, 9.5f), _headMat);
            Part(PrimitiveType.Cube, HitGroup.Chest, new Vector3(0, 51.5f, 0), new Vector3(20f, 15f, 11f), _bodyMat);
            Part(PrimitiveType.Cube, HitGroup.Stomach, new Vector3(0, 38.5f, 0), new Vector3(17f, 11f, 10f), _bodyMat);
            Part(PrimitiveType.Capsule, HitGroup.LeftArm, new Vector3(-13.5f, 46f, 0), new Vector3(6f, 13f, 6f), _bodyMat);
            Part(PrimitiveType.Capsule, HitGroup.RightArm, new Vector3(13.5f, 46f, 0), new Vector3(6f, 13f, 6f), _bodyMat);
            Part(PrimitiveType.Capsule, HitGroup.LeftLeg, new Vector3(-5f, 16.5f, 0), new Vector3(7.5f, 16.5f, 7.5f), _bodyMat);
            Part(PrimitiveType.Capsule, HitGroup.RightLeg, new Vector3(5f, 16.5f, 0), new Vector3(7.5f, 16.5f, 7.5f), _bodyMat);

            _renderers = GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _baseColors[i] = _renderers[i].sharedMaterial.color;

            _anchor = transform.position;
            _right = transform.right;
            SpawnTime = Time.time;
            _nextFlip = Time.time + UnityEngine.Random.Range(0.25f, 0.9f);
        }

        void Part(PrimitiveType type, HitGroup group, Vector3 localPos, Vector3 size, Material mat)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = group.ToString();
            p.transform.SetParent(transform, false);
            p.transform.localPosition = localPos;
            // Unity capsule is 2 units tall at scale 1, sphere/cube are 1.
            p.transform.localScale = type == PrimitiveType.Capsule ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
            var r = p.GetComponent<Renderer>();
            r.material = new Material(mat);
            var hb = p.AddComponent<Hitbox>();
            hb.group = group;
            hb.owner = this;
        }

        public void ApplyHit(in HitResult hit)
        {
            if (!Alive) return;
            Health = Mathf.Max(0, Health - hit.damage);
            _flashUntil = Time.time + 0.08f;
            Hit?.Invoke(this, hit);
            if (!Alive)
            {
                Killed?.Invoke(this);
                Destroy(gameObject, 0.05f);
            }
        }

        void Update()
        {
            bool flash = Time.time < _flashUntil;
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].material.color = flash ? Color.red : _baseColors[i];

            if (Motion == TargetMotion.Static || !Alive) return;

            float dt = Time.deltaTime;
            if (Motion == TargetMotion.Strafe)
            {
                // ADAD: flip on a timer or at the edge of the lane.
                float offset = Vector3.Dot(transform.position - _anchor, _right);
                if (Time.time >= _nextFlip || Mathf.Abs(offset) > StrafeHalfWidth && Mathf.Sign(offset) == _dir)
                {
                    _dir = -_dir;
                    _nextFlip = Time.time + UnityEngine.Random.Range(0.3f, 1.0f);
                }
                _move.Tick(dt, _right * _dir, StrafeSpeed, true, false, false);
            }
            else // Peek: wide swing out from cover, hold, swing back
            {
                float phase = Mathf.Repeat(Time.time - SpawnTime, 2.4f);
                float wish = phase < 0.6f ? 1f : phase < 1.4f ? 0f : phase < 2.0f ? -1f : 0f;
                _move.Tick(dt, _right * wish, wish == 0f ? 0f : StrafeSpeed, true, false, false);
            }

            transform.position += new Vector3(_move.Velocity.x, 0f, _move.Velocity.z) * dt;
        }

        public Vector3 HeadPosition => transform.position + Vector3.up * 65f;
    }
}
