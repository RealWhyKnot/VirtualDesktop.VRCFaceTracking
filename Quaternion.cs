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
            float forwardX = -2 * (X*Z + W*Y);
            float forwardY = -2 * (Y*Z - W*X);
            float forwardZ = -(W*W - X*X - Y*Y + Z*Z);

            float yaw = forwardX / -forwardZ;
            float pitch = forwardY / (float)Math.Sqrt(forwardX*forwardX + forwardZ*forwardZ);

            return new Vector2(yaw, pitch);
        }
        #endregion
    }
}