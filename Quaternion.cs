using System;
using System.Runtime.InteropServices;
using VRCFaceTracking.Core.Types;

namespace VirtualDesktop.FaceTracking
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Quaternion
    {
        #region Static Fields
        public static readonly Quaternion Identity = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
        #endregion

        #region Fields
        public float X;
        public float Y;
        public float Z;
        public float W;
        #endregion

        #region Constructor
        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
        #endregion

        #region Methods
        public Vector2 Cartesian()
        {
            float magnitude = (float)Math.Sqrt(X*X + Y*Y + Z*Z + W*W);
            float Xm = X / magnitude;
            float Ym = Y / magnitude;
            float Zm = Z / magnitude;
            float Wm = W / magnitude;

            float pitch = (float)Math.Asin(2 * (Xm*Zm - Wm*Ym));
            float yaw = (float)Math.Atan2(2 * (Ym*Zm + Wm*Xm), Wm*Wm - Xm*Xm - Ym*Ym + Zm*Zm);

            return new Vector2(pitch, yaw);
        }

        public static Quaternion Slerp(Quaternion q1, Quaternion q2, float t)
        {
            float dot = q1.X * q2.X + q1.Y * q2.Y + q1.Z * q2.Z + q1.W * q2.W;

            if (dot < 0.0f)
            {
                q2 = new Quaternion(-q2.X, -q2.Y, -q2.Z, -q2.W);
                dot = -dot;
            }

            const float DOT_THRESHOLD = 0.9995f;
            if (dot > DOT_THRESHOLD)
            {
                float diff = 1.0f - t;
                return new Quaternion(
                    diff * q1.X + t * q2.X,
                    diff * q1.Y + t * q2.Y,
                    diff * q1.Z + t * q2.Z,
                    diff * q1.W + t * q2.W);
            }

            float theta_0 = (float)Math.Acos(dot);
            float theta = theta_0 * t;
            float sin_theta = (float)Math.Sin(theta);
            float sin_theta_0 = (float)Math.Sin(theta_0);

            float s0 = (float)Math.Cos(theta) - dot * sin_theta / sin_theta_0;
            float s1 = sin_theta / sin_theta_0;

            return new Quaternion(
                s0 * q1.X + s1 * q2.X,
                s0 * q1.Y + s1 * q2.Y,
                s0 * q1.Z + s1 * q2.Z,
                s0 * q1.W + s1 * q2.W);
        }
        #endregion
    }
}