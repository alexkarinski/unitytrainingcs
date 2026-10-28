using UnityEngine;

namespace CsAim.Combat
{
    public enum HitGroup { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }

    /// <summary>Marks a collider as part of a damageable target.</summary>
    public sealed class Hitbox : MonoBehaviour
    {
        public HitGroup group;
        public IDamageable owner;
    }

    public interface IDamageable
    {
        bool Alive { get; }
        bool HasArmor { get; }
        bool HasHelmet { get; }
        void ApplyHit(in HitResult hit);
    }

    public struct HitResult
    {
        public HitGroup group;
        public int damage;
        public float distance;
        public Vector3 point;
        public Vector3 normal;
        public int shotIndex;
        public bool killed;
    }
}
