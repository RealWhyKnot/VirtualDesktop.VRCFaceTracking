using System;
using Xunit;

namespace VirtualDesktop.FaceTracking.Tests
{
    public class GazeTests
    {
        private static Quaternion Multiply(Quaternion a, Quaternion b)
        {
            return new Quaternion(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        private static Quaternion Rotation(float x, float y, float z, double degrees)
        {
            double half = degrees * Math.PI / 360.0;
            float s = (float)Math.Sin(half);
            return new Quaternion(x * s, y * s, z * s, (float)Math.Cos(half));
        }

        private static Quaternion Eye(double rightDegrees, double upDegrees, double rollDegrees = 0)
        {
            var yaw = Rotation(0, 1, 0, -rightDegrees);
            var pitch = Rotation(1, 0, 0, upDegrees);
            var roll = Rotation(0, 0, 1, rollDegrees);
            return Multiply(Multiply(yaw, pitch), roll);
        }

        [Theory]
        [InlineData(10, 0)]
        [InlineData(30, 0)]
        [InlineData(40, 0)]
        [InlineData(-40, 0)]
        [InlineData(0, 10)]
        [InlineData(0, 25)]
        [InlineData(0, -25)]
        [InlineData(30, 20)]
        [InlineData(-35, -15)]
        public void Cartesian_GivesTheHeadsetAngles(double right, double up)
        {
            var gaze = Eye(right, up).Cartesian();

            Assert.Equal(right, gaze.ToYaw(), 0.01);
            Assert.Equal(-up, gaze.ToPitch(), 0.01);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(30)]
        [InlineData(-20)]
        public void Cartesian_IgnoresEyeRoll(double roll)
        {
            var gaze = Eye(30, 20, roll).Cartesian();

            Assert.Equal(30, gaze.ToYaw(), 0.01);
            Assert.Equal(-20, gaze.ToPitch(), 0.01);
        }

        [Fact]
        public void Cartesian_AcceptsQuaternionsThatAreNotUnitLength()
        {
            var unit = Eye(25, -10);
            var scaled = new Quaternion(unit.X * 3, unit.Y * 3, unit.Z * 3, unit.W * 3);

            Assert.Equal(unit.Cartesian().x, scaled.Cartesian().x, 0.0001);
            Assert.Equal(unit.Cartesian().y, scaled.Cartesian().y, 0.0001);
        }

        [Fact]
        public void Cartesian_LooksStraightAhead_ForIdentity()
        {
            var gaze = Quaternion.Identity.Cartesian();

            Assert.Equal(0, gaze.x, 0.0001);
            Assert.Equal(0, gaze.y, 0.0001);
        }
    }
}
