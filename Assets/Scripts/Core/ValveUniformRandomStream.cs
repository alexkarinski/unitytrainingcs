using UnityEngine;

namespace CsAim.Core
{
    /// <summary>
    /// Reimplementation of Valve's CUniformRandomStream (vstdlib/random.cpp), which is the
    /// "ran1" generator from Numerical Recipes (Park-Miller with Bays-Durham shuffle).
    ///
    /// Why this matters here: the engine does not ship a hand-authored spray pattern. It
    /// *generates* each weapon's recoil table from the weapon's "recoil_seed" attribute using
    /// this exact stream. Reproducing the stream bit-for-bit is therefore the only way to get
    /// the real AK-47 pattern out of nothing but the seed in items_game.txt.
    ///
    /// All arithmetic is deliberately 32-bit int. Schrage's trick below keeps every
    /// intermediate inside int range, so there is no overflow to worry about.
    /// </summary>
    public sealed class ValveUniformRandomStream
    {
        const int NTAB = 32;
        const int IA = 16807;
        const int IM = 2147483647;
        const int IQ = 127773;
        const int IR = 2836;
        const int NDIV = 1 + (IM - 1) / NTAB;
        const double AM = 1.0 / IM;
        const double EPS = 1.2e-7;
        const double RNMX = 1.0 - EPS;
        const int MaxRandomRange = 0x7FFFFFFF;

        readonly int[] _iv = new int[NTAB];
        int _idum;
        int _iy;

        public ValveUniformRandomStream(int seed = 0) { SetSeed(seed); }

        public void SetSeed(int seed)
        {
            // Valve: m_idum = ( ( iSeed < 0 ) ? iSeed : -iSeed ); m_iy = 0;
            _idum = seed < 0 ? seed : -seed;
            _iy = 0;
        }

        public int GenerateRandomNumber()
        {
            if (_idum <= 0 || _iy == 0)
            {
                _idum = -_idum < 1 ? 1 : -_idum;

                for (int j = NTAB + 7; j >= 0; j--)
                {
                    int k0 = _idum / IQ;
                    _idum = IA * (_idum - k0 * IQ) - IR * k0;
                    if (_idum < 0) _idum += IM;
                    if (j < NTAB) _iv[j] = _idum;
                }

                _iy = _iv[0];
            }

            int k = _idum / IQ;
            _idum = IA * (_idum - k * IQ) - IR * k;
            if (_idum < 0) _idum += IM;

            int idx = _iy / NDIV;
            _iy = _iv[idx];
            _iv[idx] = _idum;

            return _iy;
        }

        public float RandomFloat(float low = 0f, float high = 1f)
        {
            double fl = AM * GenerateRandomNumber();
            if (fl > RNMX) fl = RNMX;
            return (float)(fl * (high - low)) + low;
        }

        public int RandomInt(int low, int high)
        {
            int x = high - low + 1;
            if (x <= 1 || MaxRandomRange < x - 1) return low;

            // Rejection sampling, exactly as Valve does it, so the stream advances identically.
            int maxAcceptable = MaxRandomRange - (int)((MaxRandomRange + 1L) % x);
            int n;
            do { n = GenerateRandomNumber(); } while (n > maxAcceptable);
            return low + n % x;
        }

        /// <summary>Gaussian draw used by the engine's CGaussianRandomStream (Box-Muller, polar form).</summary>
        public float RandomGaussian(float mean = 0f, float stdDev = 1f)
        {
            float x1, x2, w;
            do
            {
                x1 = RandomFloat(-1f, 1f);
                x2 = RandomFloat(-1f, 1f);
                w = x1 * x1 + x2 * x2;
            } while (w >= 1f || w == 0f);

            w = Mathf.Sqrt(-2f * Mathf.Log(w) / w);
            return mean + x1 * w * stdDev;
        }
    }
}
