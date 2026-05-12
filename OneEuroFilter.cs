using System;

namespace VirtualDesktop.FaceTracking
{
    /// <summary>
    /// 1-euro low-pass filter (Casiez et al., 2012). Calmer at rest, more responsive
    /// during fast movement. Adapts the low-pass cutoff frequency based on the filtered
    /// derivative of the signal, so it removes neutral-jitter without adding lag on
    /// intentional expressions.
    ///
    /// One instance per channel. Pass dt seconds; the filter handles variable cadence.
    /// </summary>
    public sealed class OneEuroFilter
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _dCutoff;

        private float _xPrev;
        private float _dxPrev;
        private bool _initialized;

        public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.007f, float dCutoff = 1.0f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
        }

        public float Filter(float value, float dt)
        {
            if (!_initialized || dt <= 0f)
            {
                _initialized = true;
                _xPrev = value;
                _dxPrev = 0f;
                return value;
            }

            float dx = (value - _xPrev) / dt;
            float edx = LowPass(dx, _dxPrev, Alpha(_dCutoff, dt));
            float cutoff = _minCutoff + _beta * Math.Abs(edx);
            float filtered = LowPass(value, _xPrev, Alpha(cutoff, dt));

            _xPrev = filtered;
            _dxPrev = edx;
            return filtered;
        }

        public void Reset()
        {
            _initialized = false;
            _xPrev = 0f;
            _dxPrev = 0f;
        }

        private static float Alpha(float cutoff, float dt)
        {
            float tau = 1.0f / (2f * (float)Math.PI * cutoff);
            return 1.0f / (1.0f + tau / dt);
        }

        private static float LowPass(float value, float prev, float alpha)
        {
            return alpha * value + (1f - alpha) * prev;
        }
    }
}
