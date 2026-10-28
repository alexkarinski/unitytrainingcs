using System.Collections.Generic;
using System.Globalization;
using CsAim.Core;
using CsAim.Data;
using UnityEngine;

namespace CsAim.Weapons
{
    /// <summary>
    /// Per-shot recoil, in one of two forms:
    ///
    ///  Seeded   - the engine's own approach. For each of the 64 table slots draw
    ///             angle     = recoil_angle     ± recoil_angle_variance
    ///             magnitude = recoil_magnitude ± recoil_magnitude_variance
    ///             from a CUniformRandomStream seeded with recoil_seed. Each shot then kicks the
    ///             aim-punch velocity by that vector and the punch integrates with decay.
    ///
    ///  Measured - a cumulative offset per shot recorded in-game (sv_showimpacts against a wall,
    ///             no mouse movement). Used verbatim; this is the calibration escape hatch when
    ///             the seeded reconstruction and the game disagree.
    /// </summary>
    public sealed class RecoilTable
    {
        public struct Kick
        {
            public float angleDeg;
            public float magnitude;
        }

        readonly Kick[,] _kicks = new Kick[2, CsConstants.RecoilTableSize];
        readonly List<Vector2> _measured = new List<Vector2>();

        public bool IsMeasured => _measured.Count > 0;
        public int MeasuredCount => _measured.Count;

        public RecoilTable(WeaponDefinition w)
        {
            Generate(w);
            ParseMeasured(w.measuredPatternCsv);
        }

        void Generate(WeaponDefinition w)
        {
            var stream = new ValveUniformRandomStream(w.recoilSeed);
            for (int mode = 0; mode < 2; mode++)
            {
                var m = mode == 0 ? w.primary : (w.hasAltMode ? w.secondary : w.primary);
                for (int i = 0; i < CsConstants.RecoilTableSize; i++)
                {
                    float angle = m.recoilAngle + stream.RandomFloat(-m.recoilAngleVariance, m.recoilAngleVariance);
                    float mag = m.recoilMagnitude + stream.RandomFloat(-m.recoilMagnitudeVariance, m.recoilMagnitudeVariance);
                    _kicks[mode, i] = new Kick { angleDeg = angle, magnitude = mag };
                }
            }
        }

        void ParseMeasured(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return;
            foreach (var rawLine in csv.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ', '\t', ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var yaw))
                    _measured.Add(new Vector2(pitch, yaw));
            }
        }

        public Kick GetKick(bool alt, int index)
        {
            index = Mathf.Clamp(index, 0, CsConstants.RecoilTableSize - 1);
            return _kicks[alt ? 1 : 0, index];
        }

        /// <summary>Cumulative bullet offset (pitch up, yaw right) in degrees for shot <paramref name="index"/>.</summary>
        public Vector2 GetMeasured(int index)
        {
            if (_measured.Count == 0) return Vector2.zero;
            return _measured[Mathf.Clamp(index, 0, _measured.Count - 1)];
        }
    }
}
