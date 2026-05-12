using System;
using System.IO;
using System.Text.Json;

namespace VirtualDesktop.FaceTracking
{
    public unsafe class ExpressionCalibrator
    {
        private const int ExpressionCount = FaceState.ExpressionCount;
        private const int ProfileVersion = 2;
        private const int WarmupSamples = 120;
        private const int SaveIntervalFrames = 72 * 5;
        private const int ActiveSamplesForFullTrust = 72 * 20;

        // Floor learning is deliberately asymmetric:
        // - fast downward movement lets startup neutral settle quickly
        // - slow upward movement only happens while the channel looks idle/stable
        // This avoids the old behavior where the neutral baseline slowly decayed toward 0.
        private const float FloorDropRate = 0.35f;
        private const float FloorRestRiseRate = 0.012f;
        private const float FloorRestTolerance = 0.035f;
        private const float StableRawDelta = 0.008f;

        // Ceiling learning is no longer trusted immediately. A new high value updates the
        // observed ceiling, but the output mapper only trusts a smaller personal ceiling
        // after enough active samples have accumulated, and that count is persisted.
        private const float CeilingRiseRate = 0.055f;
        private const float CeilingDecayRate = 0.00002f;
        private const float CeilingHeadroom = 1.10f;
        private const float MinimumRange = 0.22f;

        // Expression shaping. This gives low/mid values some life without pulling the
        // top end to 1.0 unless the raw signal actually exceeds the learned range.
        private const float DeadZoneAbsolute = 0.018f;
        private const float DeadZoneRangeFraction = 0.055f;
        private const float Gamma = 0.92f;
        private const float AssistStrength = 0.55f;
        private const float SoftMaxStart = 0.90f;
        private const float SoftMaxCompression = 0.65f;
        private const float OvershootToFullScale = 0.12f;

        private static readonly bool[] PassthroughExpression = BuildPassthroughExpressions();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _profilePath;
        private readonly ChannelState[] _channels;
        private readonly float[] _calibrated;
        private int _framesSinceSave;
        private bool _dirty;

        public ExpressionCalibrator(string profilePath = null)
        {
            _profilePath = profilePath;
            _channels = LoadProfileOrDefault(profilePath);
            _calibrated = new float[ExpressionCount];
        }

        public float Calibrate(int index, float rawValue)
        {
            if ((uint)index >= ExpressionCount)
                return Clamp01(rawValue);

            rawValue = Clamp01(rawValue);

            if (PassthroughExpression[index])
                return rawValue;

            ChannelState channel = _channels[index];
            channel.SampleCount++;

            float rawDelta = Math.Abs(rawValue - channel.LastRaw);
            UpdateFloor(index, channel, rawValue, rawDelta);

            float rangeBeforeCeilingUpdate = GetEffectiveRange(index);
            float activeGate = Math.Max(DeadZoneAbsolute * 2f, rangeBeforeCeilingUpdate * 0.12f);
            bool active = rawValue > channel.Floor + activeGate;
            if (active && channel.ActiveSampleCount < int.MaxValue - 1)
                channel.ActiveSampleCount++;

            UpdateCeiling(channel, rawValue, active);

            float output = ShapeOutput(index, channel, rawValue);

            // For a brand-new profile, blend from raw to calibrated briefly so startup
            // never jumps. Loaded profiles skip this because their sample counts are
            // restored beyond warmup.
            if (channel.SampleCount < WarmupSamples)
            {
                float blend = channel.SampleCount / (float)WarmupSamples;
                output = rawValue + (output - rawValue) * blend;
            }

            output = Clamp01(output);
            channel.LastRaw = rawValue;
            channel.LastOutput = output;
            _dirty = true;
            return output;
        }

        public float[] CalibrateAll(float* raw)
        {
            for (int i = 0; i < ExpressionCount; i++)
                _calibrated[i] = Calibrate(i, raw[i]);

            _framesSinceSave++;
            if (_dirty && _framesSinceSave >= SaveIntervalFrames)
                SaveNow();

            return _calibrated;
        }

        public void SaveNow()
        {
            if (string.IsNullOrEmpty(_profilePath) || !_dirty)
                return;

            try
            {
                string directory = Path.GetDirectoryName(_profilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                CalibrationProfile profile = new CalibrationProfile
                {
                    Version = ProfileVersion,
                    UpdatedUtc = DateTime.UtcNow.ToString("O"),
                    Channels = _channels
                };

                string tempPath = _profilePath + ".tmp";
                string json = JsonSerializer.Serialize(profile, JsonOptions);
                File.WriteAllText(tempPath, json);

                if (File.Exists(_profilePath))
                    File.Replace(tempPath, _profilePath, null);
                else
                    File.Move(tempPath, _profilePath);

                _dirty = false;
                _framesSinceSave = 0;
            }
            catch
            {
                // Calibration persistence is useful, but it should never break tracking.
            }
        }

        public float GetFloor(int index) => IsValidIndex(index) ? _channels[index].Floor : 0f;
        public float GetCeiling(int index) => IsValidIndex(index) ? Clamp01(_channels[index].Floor + GetEffectiveRange(index)) : 1f;
        public float GetObservedCeiling(int index) => IsValidIndex(index) ? _channels[index].Ceiling : 0f;
        public int GetSampleCount(int index) => IsValidIndex(index) ? _channels[index].SampleCount : 0;
        public int GetActiveSampleCount(int index) => IsValidIndex(index) ? _channels[index].ActiveSampleCount : 0;
        public float GetCalibrated(int index) => IsValidIndex(index) ? _calibrated[index] : 0f;
        public bool IsPassthrough(int index) => IsValidIndex(index) && PassthroughExpression[index];
        public bool IsWarmedUp(int index) => GetSampleCount(index) >= WarmupSamples;

        public void Reset()
        {
            for (int i = 0; i < ExpressionCount; i++)
                _channels[i] = CreateDefaultChannel();

            Array.Clear(_calibrated, 0, _calibrated.Length);
            _dirty = true;
            _framesSinceSave = SaveIntervalFrames;
        }

        private void UpdateFloor(int index, ChannelState channel, float rawValue, float rawDelta)
        {
            if (rawValue < channel.Floor)
            {
                channel.Floor += (rawValue - channel.Floor) * FloorDropRate;
            }
            else
            {
                float neutralWindow = Math.Max(FloorRestTolerance, GetDefaultRange(index) * 0.25f);
                bool looksNeutral = channel.LastOutput < 0.08f && rawDelta < StableRawDelta && rawValue <= channel.Floor + neutralWindow;
                bool closeToFloor = rawValue <= channel.Floor + FloorRestTolerance;

                if (looksNeutral || closeToFloor)
                    channel.Floor += (rawValue - channel.Floor) * FloorRestRiseRate;
            }

            channel.Floor = Clamp01(channel.Floor);
        }

        private static void UpdateCeiling(ChannelState channel, float rawValue, bool active)
        {
            if (rawValue > channel.Ceiling)
            {
                float rate = active ? CeilingRiseRate : CeilingRiseRate * 0.25f;
                channel.Ceiling += (rawValue - channel.Ceiling) * rate;
            }
            else if (channel.Ceiling > rawValue + 0.02f)
            {
                // Forget old outliers very slowly. Persistent storage means this can be tiny.
                channel.Ceiling += (rawValue - channel.Ceiling) * CeilingDecayRate;
            }

            channel.Ceiling = Clamp01(channel.Ceiling);
        }

        private float ShapeOutput(int index, ChannelState channel, float rawValue)
        {
            float range = GetEffectiveRange(index);
            float deadZone = Math.Max(DeadZoneAbsolute, range * DeadZoneRangeFraction);
            float adjusted = rawValue - channel.Floor - deadZone;
            if (adjusted <= 0f)
                return 0f;

            float linear = Clamp01(adjusted / Math.Max(0.001f, range - deadZone));
            float curved = (float)Math.Pow(linear, Gamma);
            float shaped = linear + (curved - linear) * AssistStrength;

            // Do not stick to 1.0 just because we reached the current range estimate.
            if (shaped > SoftMaxStart)
                shaped = SoftMaxStart + (shaped - SoftMaxStart) * SoftMaxCompression;

            // Still allow a true max when the user clearly exceeds the learned ceiling.
            float overshoot = (rawValue - (channel.Floor + range)) / Math.Max(0.001f, range * OvershootToFullScale);
            if (overshoot > 0f)
                shaped += (1f - shaped) * Clamp01(overshoot);

            return Clamp01(shaped);
        }

        private float GetEffectiveRange(int index)
        {
            ChannelState channel = _channels[index];
            float defaultRange = Math.Max(MinimumRange, GetDefaultRange(index));
            float learnedRange = Math.Max(MinimumRange, (channel.Ceiling - channel.Floor) * CeilingHeadroom);

            // If the user actually reaches beyond the default range, use it immediately.
            // The trust blend is only for shrinking the range, which is what causes fake max snaps.
            if (learnedRange >= defaultRange)
                return Clamp01(learnedRange);

            float trust = GetCeilingTrust(channel);
            return Clamp01(defaultRange + (learnedRange - defaultRange) * trust);
        }

        private static float GetCeilingTrust(ChannelState channel)
        {
            if (channel.SampleCount < WarmupSamples)
                return 0f;

            return Clamp01(channel.ActiveSampleCount / (float)ActiveSamplesForFullTrust);
        }

        private static ChannelState[] LoadProfileOrDefault(string profilePath)
        {
            ChannelState[] channels = CreateDefaultChannels();
            if (string.IsNullOrEmpty(profilePath) || !File.Exists(profilePath))
                return channels;

            try
            {
                CalibrationProfile profile = JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(profilePath));
                if (profile == null || profile.Version != ProfileVersion || profile.Channels == null || profile.Channels.Length != ExpressionCount)
                    return channels;

                for (int i = 0; i < ExpressionCount; i++)
                {
                    ChannelState saved = profile.Channels[i];
                    if (saved == null)
                        continue;

                    channels[i] = new ChannelState
                    {
                        Floor = Clamp01(saved.Floor),
                        Ceiling = Clamp01(saved.Ceiling),
                        SampleCount = Math.Max(saved.SampleCount, WarmupSamples),
                        ActiveSampleCount = Math.Max(0, saved.ActiveSampleCount),
                        LastRaw = Clamp01(saved.LastRaw),
                        LastOutput = 0f
                    };
                }
            }
            catch
            {
                // Corrupt or incompatible profile: start fresh rather than breaking tracking.
            }

            return channels;
        }

        private static ChannelState[] CreateDefaultChannels()
        {
            ChannelState[] channels = new ChannelState[ExpressionCount];
            for (int i = 0; i < ExpressionCount; i++)
                channels[i] = CreateDefaultChannel();
            return channels;
        }

        private static ChannelState CreateDefaultChannel()
        {
            return new ChannelState
            {
                Floor = 1.0f,
                Ceiling = 0.0f,
                SampleCount = 0,
                ActiveSampleCount = 0,
                LastRaw = 0.0f,
                LastOutput = 0.0f
            };
        }

        private static float GetDefaultRange(int index)
        {
            switch ((Expressions)index)
            {
                case Expressions.EyesClosedL:
                case Expressions.EyesClosedR:
                    return 0.42f;

                case Expressions.LidTightenerL:
                case Expressions.LidTightenerR:
                case Expressions.UpperLidRaiserL:
                case Expressions.UpperLidRaiserR:
                    return 0.35f;

                case Expressions.BrowLowererL:
                case Expressions.BrowLowererR:
                case Expressions.InnerBrowRaiserL:
                case Expressions.InnerBrowRaiserR:
                case Expressions.OuterBrowRaiserL:
                case Expressions.OuterBrowRaiserR:
                    return 0.42f;

                case Expressions.JawDrop:
                    return 0.55f;

                case Expressions.JawSidewaysLeft:
                case Expressions.JawSidewaysRight:
                case Expressions.JawThrust:
                case Expressions.MouthLeft:
                case Expressions.MouthRight:
                    return 0.35f;

                case Expressions.LipsToward:
                    return 0.30f;

                case Expressions.TongueTipInterdental:
                case Expressions.TongueTipAlveolar:
                case Expressions.TongueFrontDorsalPalate:
                case Expressions.TongueMidDorsalPalate:
                case Expressions.TongueBackDorsalVelar:
                case Expressions.TongueOut:
                case Expressions.TongueRetreat:
                    return 0.65f;
            }

            // Most lip/cheek/nose channels are real but naturally smaller than jaw/tongue.
            if (index >= (int)Expressions.LipCornerDepressorL && index <= (int)Expressions.UpperLipRaiserR)
                return 0.38f;

            if (index >= (int)Expressions.CheekPuffL && index <= (int)Expressions.DimplerR)
                return 0.40f;

            return 0.50f;
        }

        private static bool[] BuildPassthroughExpressions()
        {
            bool[] passthrough = new bool[ExpressionCount];
            passthrough[(int)Expressions.EyesLookDownL] = true;
            passthrough[(int)Expressions.EyesLookDownR] = true;
            passthrough[(int)Expressions.EyesLookLeftL] = true;
            passthrough[(int)Expressions.EyesLookLeftR] = true;
            passthrough[(int)Expressions.EyesLookRightL] = true;
            passthrough[(int)Expressions.EyesLookRightR] = true;
            passthrough[(int)Expressions.EyesLookUpL] = true;
            passthrough[(int)Expressions.EyesLookUpR] = true;
            return passthrough;
        }

        private static bool IsValidIndex(int index) => (uint)index < ExpressionCount;
        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        public class ChannelState
        {
            public float Floor { get; set; }
            public float Ceiling { get; set; }
            public int SampleCount { get; set; }
            public int ActiveSampleCount { get; set; }
            public float LastRaw { get; set; }
            public float LastOutput { get; set; }
        }

        public class CalibrationProfile
        {
            public int Version { get; set; }
            public string UpdatedUtc { get; set; }
            public ChannelState[] Channels { get; set; }
        }
    }
}
