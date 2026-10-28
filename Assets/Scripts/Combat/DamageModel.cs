using CsAim.Core;
using CsAim.Data;
using UnityEngine;

namespace CsAim.Combat
{
    /// <summary>
    /// CS damage formula (CCSPlayer::OnTakeDamage + FireBullet):
    ///   damage *= range_modifier ^ (distance / 500)
    ///   damage *= hitgroup multiplier          (head uses the weapon's headshot multiplier)
    ///   if armour covers the hitgroup:
    ///       health damage = damage * armor_ratio * 0.5
    /// Legs are never armoured; the head is armoured only with a helmet.
    /// </summary>
    public static class DamageModel
    {
        public static int Compute(WeaponDefinition w, HitGroup group, float distance, bool armor, bool helmet)
        {
            float dmg = w.damage * Mathf.Pow(w.rangeModifier, distance / CsConstants.RangeModifierDistance);

            bool armoured;
            switch (group)
            {
                case HitGroup.Head:
                    dmg *= w.headshotMultiplier > 0f ? w.headshotMultiplier : CsConstants.HeadMultiplier;
                    armoured = helmet;
                    break;
                case HitGroup.Stomach:
                    dmg *= CsConstants.StomachMultiplier;
                    armoured = armor;
                    break;
                case HitGroup.LeftLeg:
                case HitGroup.RightLeg:
                    dmg *= CsConstants.LegMultiplier;
                    armoured = false;
                    break;
                default:
                    dmg *= CsConstants.ChestMultiplier;
                    armoured = armor;
                    break;
            }

            if (armoured)
                dmg *= w.armorRatio * CsConstants.ArmorBonus;

            // CS truncates toward zero when converting to health.
            return Mathf.Max(1, (int)dmg);
        }
    }
}
