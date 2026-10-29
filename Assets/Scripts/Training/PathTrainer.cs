using CsAim.Core;
using UnityEngine;

namespace CsAim.Training
{
    /// <summary>
    /// "Follow the drawing" drill. Each bullet is checked against the ideal trajectory: where
    /// it would land *without* random spread, relative to where the spray started. Leave the
    /// corridor and the attempt is over - the spray restarts from the first bullet.
    ///
    /// Spread is excluded from the pass/fail check on purpose: it is random, and failing someone
    /// for a lucky/unlucky roll would teach nothing. It still counts for the on-target rate.
    ///
    /// The goal is a reflex, so the visual guide is taken away in steps: line + next-bullet marker,
    /// then a faint line, then nothing (muscle memory only). Each step unlocks after a few clean
    /// full-magazine sprays. Per-bullet error is kept across attempts to show the weak segment.
    /// </summary>
    public sealed class PathTrainer
    {
        public enum Outcome { Continue, Failed, Completed }

        /// <summary>How much of the drawing is shown while spraying.</summary>
        public enum Guide { Full, Line, Blind }

        public Guide GuideLevel { get; private set; } = Guide.Full;
        /// <summary>Remove a guide step automatically after enough clean full sprays.</summary>
        public bool AutoFade = true;
        public const int CleanToAdvance = 3;
        public int CleanAtLevel { get; private set; }

        const int MaxBullets = 64;
        readonly float[] _errorSumAt = new float[MaxBullets];
        readonly int[] _errorCountAt = new int[MaxBullets];

        /// <summary>Allowed deviation in degrees. 0.7 is about a head's radius at 10 m.</summary>
        public float ToleranceDeg = 0.7f;
        /// <summary>Start with the first 10 bullets and add 5 after each clean attempt.</summary>
        public bool Progressive = true;
        public const int ProgressiveStart = 10;
        public const int ProgressiveStep = 5;

        public int TargetLength { get; private set; }
        public int ClipSize { get; private set; }
        public int CurrentBullet { get; private set; }
        public bool Active { get; private set; }
        public Vector2 StartEye { get; private set; }

        public int Attempts { get; private set; }
        public int CleanSprays { get; private set; }
        public int BestBullets { get; private set; }

        public string Message { get; private set; } = string.Empty;
        public bool MessageIsSuccess { get; private set; }
        public float MessageTime { get; private set; } = -100f;

        int _shots, _onTarget;
        float _scoreSum, _errorSum, _lastError;

        /// <summary>Average per-bullet score: 100% = dead on the line, 0% = at or past the edge.</summary>
        public float PathAccuracy => _shots == 0 ? 0f : 100f * _scoreSum / _shots;
        /// <summary>Bullets (spread included) that landed inside the corridor around the aim point.</summary>
        public float OnTargetRate => _shots == 0 ? 0f : 100f * _onTarget / _shots;
        public float MeanErrorDeg => _shots == 0 ? 0f : _errorSum / _shots;
        public float LastErrorDeg => _lastError;
        public int ShotsScored => _shots;

        public void Reset(int clipSize)
        {
            ClipSize = Mathf.Max(1, clipSize);
            TargetLength = Progressive ? Mathf.Min(ProgressiveStart, ClipSize) : ClipSize;
            Active = false;
            CurrentBullet = 0;
            Attempts = CleanSprays = BestBullets = 0;
            _shots = _onTarget = 0;
            _scoreSum = _errorSum = _lastError = 0f;
            Message = string.Empty;
            GuideLevel = Guide.Full;
            CleanAtLevel = 0;
            System.Array.Clear(_errorSumAt, 0, MaxBullets);
            System.Array.Clear(_errorCountAt, 0, MaxBullets);
        }

        public void SetGuide(Guide level)
        {
            GuideLevel = level;
            CleanAtLevel = 0;
        }

        /// <summary>Average error of bullet i over all attempts, or -1 if it was never fired.</summary>
        public float MeanErrorAt(int i) =>
            i < 0 || i >= MaxBullets || _errorCountAt[i] == 0 ? -1f : _errorSumAt[i] / _errorCountAt[i];

        /// <summary>The run of <paramref name="window"/> consecutive bullets with the highest mean error.</summary>
        public bool TryWeakestSegment(int window, out int start, out float meanError)
        {
            start = -1;
            meanError = 0f;
            int limit = Mathf.Min(ClipSize, MaxBullets);
            for (int s = 1; s + window <= limit; s++)        // bullet 1 has no recoil yet: skip it
            {
                float sum = 0f;
                int n = 0;
                for (int i = s; i < s + window; i++)
                {
                    if (_errorCountAt[i] < 2) { n = 0; break; }
                    sum += _errorSumAt[i] / _errorCountAt[i];
                    n++;
                }
                if (n == window && sum / n > meanError)
                {
                    meanError = sum / n;
                    start = s;
                }
            }
            return start >= 0;
        }

        public void SetProgressive(bool progressive)
        {
            Progressive = progressive;
            TargetLength = progressive ? Mathf.Min(ProgressiveStart, ClipSize) : ClipSize;
        }

        /// <param name="shotIndex">0-based bullet index in the spray.</param>
        /// <param name="eyeAngles">Where the player aimed (pitch up, yaw right), without recoil.</param>
        /// <param name="recoilDeg">Recoil offset of this bullet.</param>
        /// <param name="spreadDeg">Random spread of this bullet.</param>
        public Outcome OnShot(int shotIndex, Vector2 eyeAngles, Vector2 recoilDeg, Vector2 spreadDeg, float now)
        {
            if (shotIndex == 0 || !Active)
            {
                Active = true;
                StartEye = eyeAngles;
                Attempts++;
            }

            Vector2 mouse = eyeAngles - StartEye;
            mouse.y = Mathf.DeltaAngle(0f, mouse.y);

            // Where the bullet goes if spread were zero: the part you control.
            float error = (mouse + recoilDeg).magnitude;
            _lastError = error;
            _shots++;
            _errorSum += error;
            _scoreSum += Mathf.Clamp01(1f - error / ToleranceDeg);
            if (shotIndex < MaxBullets)
            {
                _errorSumAt[shotIndex] += error;
                _errorCountAt[shotIndex]++;
            }
            if ((mouse + recoilDeg + spreadDeg).magnitude <= ToleranceDeg) _onTarget++;

            CurrentBullet = shotIndex + 1;

            if (error > ToleranceDeg)
            {
                Active = false;
                BestBullets = Mathf.Max(BestBullets, shotIndex);   // this bullet was the miss
                Say($"Сорвался на {CurrentBullet}-й пуле: ушёл на {error:0.00}° (допуск {ToleranceDeg:0.00}°). Заново!", false, now);
                return Outcome.Failed;
            }

            BestBullets = Mathf.Max(BestBullets, CurrentBullet);

            if (CurrentBullet >= TargetLength)
            {
                Active = false;
                CleanSprays++;
                if (Progressive && TargetLength < ClipSize)
                {
                    TargetLength = Mathf.Min(ClipSize, TargetLength + ProgressiveStep);
                    Say($"Чисто! Следующая цель: {TargetLength} пуль.", true, now);
                }
                else
                {
                    CleanAtLevel++;
                    if (AutoFade && GuideLevel != Guide.Blind && CleanAtLevel >= CleanToAdvance)
                    {
                        // Next step towards muscle memory: less to look at, start short again.
                        GuideLevel = GuideLevel == Guide.Full ? Guide.Line : Guide.Blind;
                        CleanAtLevel = 0;
                        TargetLength = Progressive ? Mathf.Min(ProgressiveStart, ClipSize) : ClipSize;
                        Say(GuideLevel == Guide.Line
                            ? "Отлично! Маркер убран, осталась бледная линия."
                            : "Отлично! Теперь вслепую: только по памяти руки.", true, now);
                    }
                    else
                    {
                        string next = AutoFade && GuideLevel != Guide.Blind ? $" ({CleanAtLevel}/{CleanToAdvance} до снятия подсказок)" : "";
                        Say($"Чистый спрей на {CurrentBullet} пуль!{next}", true, now);
                    }
                }
                return Outcome.Completed;
            }

            return Outcome.Continue;
        }

        public void OnTriggerReleased(float now)
        {
            if (!Active) return;
            Active = false;
            Say($"Отпустил на {CurrentBullet}-й пуле, держи до {TargetLength}.", false, now);
        }

        void Say(string text, bool success, float now)
        {
            Message = text;
            MessageIsSuccess = success;
            MessageTime = now;
        }

        /// <summary>Where the screen-centre crosshair must be for bullet i to land on the aim point.</summary>
        public static Vector2 CrosshairTarget(Vector2 recoilDeg) => -recoilDeg * (1f - CsConstants.ViewRecoilTracking);
    }
}
