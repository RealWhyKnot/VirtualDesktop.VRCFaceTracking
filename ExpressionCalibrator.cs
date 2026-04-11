using System;
using System.Collections.Generic;

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

        // How fast floor/ceiling converge toward a new extreme per frame.
        // 0.5 = half the gap per frame. Dampens single-frame sensor noise
        // while converging within 3-4 frames for real changes.
        // (1 frame → 50%, 2 → 75%, 3 → 87.5%, 4 → 93.75%)
        private const float FloorSnapRate = 0.5f;
        private const float CeilingSnapRate = 0.5f;

        // How fast the observed ceiling decays toward its default per frame.
        private const float CeilingDecayRate = 0.001f;

        // Default ceiling to decay toward. Below 1.0 because most expressions
        // never truly reach 1.0 raw from the headset.
        private const float CeilingDefault = 0.8f;

        // How much to amplify floor-removed values. Reduced from 1.3 now that
        // ceiling tracking handles most of what gain was compensating for.
        // A small boost still helps since users rarely hit absolute max.
        private const float GainFactor = 1.1f;

        // Power curve applied after gain. Values < 1 boost the low-to-mid
        // range (0.85 gives roughly +10% at midpoint) without affecting 0 or 1.
        private const float Gamma = 0.85f;

        // Fraction of (1.0 - floor) that maps to zero output, suppressing
        // low-level noise and sensor jitter at rest. Needs to be wide enough
        // to catch LipsToward's natural resting offset (~0.05-0.06 raw).
        private const float DeadZoneFraction = 0.08f;

        // Directional/unused expressions that bypass calibration entirely.
        // EyesLook* (14-21): directional gaze weights not consumed by this module
        // (eye gaze comes from quaternion data). Calibrating them would distort
        // their semantics with floor removal and gain.
        private static readonly HashSet<int> PassthroughExpressions = new HashSet<int>
        {
            (int)Expressions.EyesLookDownL,
            (int)Expressions.EyesLookDownR,
            (int)Expressions.EyesLookLeftL,
            (int)Expressions.EyesLookLeftR,
            (int)Expressions.EyesLookRightL,
            (int)Expressions.EyesLookRightR,
            (int)Expressions.EyesLookUpL,
            (int)Expressions.EyesLookUpR,
        };

        private readonly float[] _observedFloor;
        private readonly float[] _observedCeiling;
        private readonly int[] _sampleCount;
        private readonly float[] _calibrated;

        public ExpressionCalibrator()
        {
            _observedFloor = new float[ExpressionCount];
            _observedCeiling = new float[ExpressionCount];
            _sampleCount = new int[ExpressionCount];
            _calibrated = new float[ExpressionCount];

            for (int i = 0; i < ExpressionCount; i++)
            {
                // Start floor at 1.0 so first real value sets the baseline immediately.
                _observedFloor[i] = 1.0f;
                // Start ceiling at 0.0 so first real value sets it immediately.
                _observedCeiling[i] = 0.0f;
            }
        }

        public float Calibrate(int index, float rawValue)
        {
            if (PassthroughExpressions.Contains(index))
                return rawValue;

            _sampleCount[index]++;

            // Track the resting floor: converge downward quickly, decay toward 0 slowly.
            // Uses EMA instead of instant snap to dampen single-frame sensor noise.
            if (rawValue < _observedFloor[index])
                _observedFloor[index] += (rawValue - _observedFloor[index]) * FloorSnapRate;
            else
                _observedFloor[index] += (0.0f - _observedFloor[index]) * FloorDecayRate;

            // Track the observed ceiling: converge upward quickly, decay toward default slowly.
            if (rawValue > _observedCeiling[index])
                _observedCeiling[index] += (rawValue - _observedCeiling[index]) * CeilingSnapRate;
            else
                _observedCeiling[index] += (CeilingDefault - _observedCeiling[index]) * CeilingDecayRate;

            float floor = _observedFloor[index];
            float ceiling = Math.Max(floor + 0.1f, _observedCeiling[index]);
            float naturalRange = ceiling - floor;

            // Remove resting baseline.
            float adjusted = Math.Max(0f, rawValue - floor);

            // Normalize against the observed natural range, then apply gain.
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
        public float GetCeiling(int index) => _observedCeiling[index];

        public void Reset()
        {
            for (int i = 0; i < ExpressionCount; i++)
            {
                _observedFloor[i] = 1.0f;
                _observedCeiling[i] = 0.0f;
                _sampleCount[i] = 0;
                _calibrated[i] = 0f;
            }
        }
    }
}
