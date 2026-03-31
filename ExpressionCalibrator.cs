using System;

namespace VirtualDesktop.FaceTracking
{
    public unsafe class ExpressionCalibrator
    {
        private const int ExpressionCount = FaceState.ExpressionCount;
        private const int WarmupSamples = 120;

        // How fast the resting floor decays toward 0 per frame.
        // At ~72Hz this takes ~14 seconds to halve, so the floor tracks slow drift
        // without over-reacting to brief low values.
        private const float FloorDecayRate = 0.001f;

        // How much to amplify floor-removed values. 1.3 = 30% boost.
        // Expressions that reach ~0.77 raw will saturate at 1.0; typical
        // 0.4-0.6 range expressions land around 0.55-0.78 output.
        private const float GainFactor = 1.3f;

        // Power curve applied after gain. Values < 1 boost the low-to-mid
        // range (0.85 gives roughly +10% at midpoint) without affecting 0 or 1.
        private const float Gamma = 0.85f;

        // Fraction of (1.0 - floor) that maps to zero output, suppressing
        // low-level noise and sensor jitter at rest. Needs to be wide enough
        // to catch LipsToward's natural resting offset (~0.05-0.06 raw).
        private const float DeadZoneFraction = 0.08f;

        private readonly float[] _observedFloor;
        private readonly int[] _sampleCount;
        private readonly float[] _calibrated;

        public ExpressionCalibrator()
        {
            _observedFloor = new float[ExpressionCount];
            _sampleCount = new int[ExpressionCount];
            _calibrated = new float[ExpressionCount];

            // Start floor at 1.0 so first real value sets the baseline immediately.
            for (int i = 0; i < ExpressionCount; i++)
                _observedFloor[i] = 1.0f;
        }

        public float Calibrate(int index, float rawValue)
        {
            _sampleCount[index]++;

            // Track the resting floor: expand downward instantly, decay toward 0 slowly.
            if (rawValue < _observedFloor[index])
                _observedFloor[index] = rawValue;
            _observedFloor[index] += (0.0f - _observedFloor[index]) * FloorDecayRate;

            float floor = _observedFloor[index];
            float naturalRange = Math.Max(0.1f, 1.0f - floor);

            // Remove resting baseline.
            float adjusted = Math.Max(0f, rawValue - floor);

            // Normalize against the theoretical natural range, then apply gain.
            float normalized = Math.Min(1f, (adjusted / naturalRange) * GainFactor);

            // Dead zone: suppress small values near rest.
            float deadZone = DeadZoneFraction;
            if (normalized < deadZone)
                normalized = 0f;
            else
                normalized = (normalized - deadZone) / (1f - deadZone);

            // Gamma curve: gently boost mid-range without saturating high values.
            if (normalized > 0f)
                normalized = (float)Math.Pow(normalized, Gamma);

            normalized = Math.Max(0f, Math.Min(1f, normalized));

            // Warm-up: blend from raw toward calibrated over the first ~2 seconds.
            int count = _sampleCount[index];
            if (count < WarmupSamples)
            {
                float blend = (float)count / WarmupSamples;
                normalized = rawValue + (normalized - rawValue) * blend;
                normalized = Math.Max(0f, Math.Min(1f, normalized));
            }

            return normalized;
        }

        public float[] CalibrateAll(float* raw)
        {
            for (int i = 0; i < ExpressionCount; i++)
                _calibrated[i] = Calibrate(i, raw[i]);
            return _calibrated;
        }

        public float GetFloor(int index) => _observedFloor[index];

        public void Reset()
        {
            for (int i = 0; i < ExpressionCount; i++)
            {
                _observedFloor[i] = 1.0f;
                _sampleCount[i] = 0;
                _calibrated[i] = 0f;
            }
        }
    }
}
