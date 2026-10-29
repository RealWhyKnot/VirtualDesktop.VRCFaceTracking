using System;
using System.Diagnostics;

namespace VirtualDesktop.FaceTracking
{
    internal sealed class NeutralFade
    {
        #region Constants
        public const double HoldSeconds = 0.25;
        public const double FadeSeconds = 0.75;
        #endregion

        #region Fields
        private long _freshTimestamp;
        private float _remaining = 1f;
        #endregion

        #region Methods
        public void Refresh(long timestamp)
        {
            _freshTimestamp = timestamp;
            _remaining = 1f;
        }

        public float Step(long timestamp)
        {
            if (_remaining <= 0f)
            {
                return 1f;
            }

            float remaining = Remaining((timestamp - _freshTimestamp) / (double)Stopwatch.Frequency);
            float scale = remaining / _remaining;
            _remaining = remaining;
            return scale;
        }

        public static float Remaining(double staleSeconds)
        {
            if (staleSeconds <= HoldSeconds)
            {
                return 1f;
            }

            double t = Math.Min((staleSeconds - HoldSeconds) / FadeSeconds, 1.0);
            return (float)(1.0 - t * t * (3.0 - 2.0 * t));
        }
        #endregion
    }
}
