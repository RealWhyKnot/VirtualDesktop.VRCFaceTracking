using System.Diagnostics;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;
using Xunit;

namespace VirtualDesktop.FaceTracking.Tests
{
    public class NeutralFadeTests
    {
        private static long At(double seconds) => (long)(seconds * Stopwatch.Frequency);

        [Theory]
        [InlineData(0.0, 1.0)]
        [InlineData(0.25, 1.0)]
        [InlineData(0.625, 0.5)]
        [InlineData(1.0, 0.0)]
        [InlineData(10.0, 0.0)]
        public void Remaining_HoldsThenEasesToZeroByOneSecond(double seconds, double expected)
        {
            Assert.Equal(expected, NeutralFade.Remaining(seconds), 0.0001);
        }

        [Fact]
        public void Remaining_NeverRises()
        {
            var previous = 1f;
            for (var ms = 0; ms <= 1200; ms += 5)
            {
                var remaining = NeutralFade.Remaining(ms / 1000.0);
                Assert.True(remaining <= previous);
                previous = remaining;
            }
        }

        [Fact]
        public void Step_ScalesAValueAlongTheCurve_WhateverTheTickRate()
        {
            var fade = new NeutralFade();
            fade.Refresh(At(5));
            var value = 0.8f;

            for (var ms = 0; ms <= 625; ms += 11)
            {
                value *= fade.Step(At(5 + ms / 1000.0));
            }
            value *= fade.Step(At(5.625));

            Assert.Equal(0.4f, value, 0.0001);
        }

        [Fact]
        public void Step_LeavesValuesAlone_DuringTheHold()
        {
            var fade = new NeutralFade();
            fade.Refresh(At(5));

            Assert.Equal(1f, fade.Step(At(5.1)));
            Assert.Equal(1f, fade.Step(At(5.25)));
        }

        [Fact]
        public void Step_StopsTouchingValues_OnceNeutral()
        {
            var fade = new NeutralFade();
            fade.Refresh(At(5));

            Assert.Equal(0f, fade.Step(At(6.5)));
            Assert.Equal(1f, fade.Step(At(7)));
        }

        [Fact]
        public void Refresh_RestartsTheHold()
        {
            var fade = new NeutralFade();
            fade.Refresh(At(5));
            fade.Step(At(5.9));

            fade.Refresh(At(6));

            Assert.Equal(1f, fade.Step(At(6.2)));
        }

        [Fact]
        public void ApplyFade_MovesEachRegionTowardNeutral_OnItsOwn()
        {
            var data = new UnifiedTrackingData();
            data.Eye.Left.Gaze = new Vector2(0.5f, -0.3f);
            data.Eye.Right.Gaze = new Vector2(0.4f, 0.2f);
            data.Eye.Left.Openness = 0.2f;
            data.Eye.Right.Openness = 0.6f;
            data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = 0.6f;
            data.Shapes[(int)UnifiedExpressions.BrowLowererRight].Weight = 0.4f;
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = 0.8f;
            data.Shapes[(int)UnifiedExpressions.TongueOut].Weight = 0.5f;

            TrackingModule.ApplyFade(data, 0.5f, 1f, 1f);

            Assert.Equal(0.25f, data.Eye.Left.Gaze.x, 0.0001);
            Assert.Equal(-0.15f, data.Eye.Left.Gaze.y, 0.0001);
            Assert.Equal(0.1f, data.Eye.Right.Gaze.y, 0.0001);
            Assert.Equal(0.2f, data.Eye.Left.Openness, 0.0001);
            Assert.Equal(0.8f, data.Shapes[(int)UnifiedExpressions.JawOpen].Weight, 0.0001);

            TrackingModule.ApplyFade(data, 1f, 0.5f, 1f);

            Assert.Equal(0.6f, data.Eye.Left.Openness, 0.0001);
            Assert.Equal(0.8f, data.Eye.Right.Openness, 0.0001);
            Assert.Equal(0.3f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 0.0001);
            Assert.Equal(0.2f, data.Shapes[(int)UnifiedExpressions.BrowLowererRight].Weight, 0.0001);
            Assert.Equal(0.8f, data.Shapes[(int)UnifiedExpressions.JawOpen].Weight, 0.0001);
            Assert.Equal(0.25f, data.Eye.Left.Gaze.x, 0.0001);

            TrackingModule.ApplyFade(data, 1f, 1f, 0f);

            Assert.Equal(0f, data.Shapes[(int)UnifiedExpressions.JawOpen].Weight);
            Assert.Equal(0f, data.Shapes[(int)UnifiedExpressions.TongueOut].Weight);
            Assert.Equal(0.3f, data.Shapes[(int)UnifiedExpressions.EyeWideLeft].Weight, 0.0001);
        }
    }
}
