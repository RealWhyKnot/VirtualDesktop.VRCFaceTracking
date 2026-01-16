using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;
using Vector2 = VRCFaceTracking.Core.Types.Vector2;

namespace VirtualDesktop.FaceTracking
{
    public unsafe class TrackingModule : ExtTrackingModule
    {
        #region Constants
        private const string BodyStateMapName = "VirtualDesktop.BodyState";
        private const string BodyStateEventName = "VirtualDesktop.BodyStateEvent";
        #endregion

        #region Fields
        private MemoryMappedFile _mappedFile;
        private MemoryMappedViewAccessor _mappedView;
        private FaceState* _faceState;
        private bool _eyeAvailable, _expressionAvailable;
        private EventWaitHandle _faceStateEvent;
        private bool? _isTracking = null;
        private float[] _prevMouthWeights;
        #endregion

        #region Properties
        private bool? IsTracking
        {
            get { return _isTracking; }
            set
            {
                if (value != _isTracking)
                {
                    _isTracking = value;
                    if ((bool)value)
                    {
                        Logger.LogInformation("[VirtualDesktop] Tracking is now active!");
                    }
                    else
                    {
                        Logger.LogWarning("[VirtualDesktop] Tracking is not active. Make sure you are connected to your computer, a VR game or SteamVR is launched and 'Forward tracking data' is enabled in the Streaming tab.");
                    }
                }
            }
        }
        #endregion

        #region Overrides
        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            ModuleInformation.Name = "Virtual Desktop";

            var stream = GetType().Assembly.GetManifestResourceStream("VirtualDesktop.FaceTracking.Resources.Logo256.png");
            if (stream != null)
            {
                ModuleInformation.StaticImages = new List<Stream> { stream };
            }

            try
            {
                var size = Marshal.SizeOf<FaceState>();
                _mappedFile = MemoryMappedFile.OpenExisting(BodyStateMapName, MemoryMappedFileRights.ReadWrite);
                _mappedView = _mappedFile.CreateViewAccessor(0, size);

                byte* ptr = null;
                _mappedView.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                _faceState = (FaceState*)ptr;

                _faceStateEvent = EventWaitHandle.OpenExisting(BodyStateEventName);
            }
            catch
            {
                Logger.LogError("[VirtualDesktop] Failed to open MemoryMappedFile. Make sure the Virtual Desktop Streamer (v1.30 or later) is running.");
                return (false, false);
            }

            (_eyeAvailable, _expressionAvailable) = (eyeAvailable, expressionAvailable);
            _prevMouthWeights = new float[256];
            return (_eyeAvailable, _expressionAvailable);
        }

        public override void Update()
        {
            if (Status == ModuleState.Active)
            {
                if (_faceStateEvent.WaitOne(50))
                {
                    UpdateTracking();
                }
                else
                {
                    var faceState = _faceState;
                    IsTracking = faceState != null && (faceState->LeftEyeIsValid || faceState->RightEyeIsValid || faceState->IsEyeFollowingBlendshapesValid || faceState->FaceIsValid);
                }
            }
            else
            {
                Thread.Sleep(10);
            }
        }

        public override void Teardown()
        {
            if (_faceState != null)
            {
                _faceState = null;
                if (_mappedView != null)
                {
                    _mappedView.Dispose();
                    _mappedView = null;
                }
                if (_mappedFile != null)
                {
                    _mappedFile.Dispose();
                    _mappedFile = null;
                }
            }
            if (_faceStateEvent != null)
            {
                _faceStateEvent.Dispose();
                _faceStateEvent = null;
            }
            _isTracking = null;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Credit https://github.com/regzo2/VRCFaceTracking-QuestProOpenXR for calculations on converting from OpenXR weigths to VRCFT shapes
        /// </summary>
        private void UpdateTracking()
        {
            var isTracking = false;

            var faceState = _faceState;
            if (faceState != null)
            {
                var expressions = faceState->ExpressionWeights;

                if (_eyeAvailable && faceState->LeftEyeIsValid || faceState->RightEyeIsValid)
                {                    
                    var leftEyePose = faceState->LeftEyePose;
                    var rightEyePose = faceState->RightEyePose;
                    UpdateEyeData(UnifiedTracking.Data.Eye, expressions, leftEyePose.Orientation, rightEyePose.Orientation);
                    isTracking = true;
                }

                if (_eyeAvailable && faceState->IsEyeFollowingBlendshapesValid)
                {
                    UpdateEyeExpressions(UnifiedTracking.Data.Shapes, expressions);
                    isTracking = true;
                }

                if (_expressionAvailable && faceState->FaceIsValid)
                {
                    UpdateMouthExpressions(UnifiedTracking.Data.Shapes, expressions);
                    isTracking = true;
                }
            }
            IsTracking = isTracking;
        }

                private void UpdateEyeData(UnifiedEyeData eye, float* expressions, Quaternion orientationL, Quaternion orientationR)
                {
                    #region Eye Openness parsing
        
                    // Multiplier for eyes closing
                    float closeL = expressions[(int)Expressions.EyesClosedL] + expressions[(int)Expressions.CheekRaiserL] * expressions[(int)Expressions.LidTightenerL];
                    float closeR = expressions[(int)Expressions.EyesClosedR] + expressions[(int)Expressions.CheekRaiserR] * expressions[(int)Expressions.LidTightenerR];
        
                    float openL = 1.0f - Math.Max(0, Math.Min(1, closeL));
                    float openR = 1.0f - Math.Max(0, Math.Min(1, closeR));
        
                    // Force synchronization to the most closed eye (minimum openness)
                    float minOpen = Math.Min(openL, openR);
                    openL = minOpen;
                    openR = minOpen;
        
                    eye.Left.Openness = openL;
                    eye.Right.Openness = openR;
        
                    #endregion
        
                    #region Eye Data to UnifiedEye
        
                    // Synchronize gaze (Average L/R)
                    var combinedRotation = Quaternion.Slerp(orientationL, orientationR, 0.5f);
                    var combinedGaze = combinedRotation.Cartesian();
        
                    eye.Right.Gaze = combinedGaze;
                    eye.Left.Gaze = combinedGaze;
        
                    // Eye dilation code, automated process maybe?
                    eye.Left.PupilDiameter_MM = 5f;
                    eye.Right.PupilDiameter_MM = 5f;
        
                    // Force the normalization values of Dilation to fit avg. pupil values.
                    eye._minDilation = 0;
                    eye._maxDilation = 10;
        
                    #endregion
                }
        private void UpdateEyeExpressions(UnifiedExpressionShape[] unifiedExpressions, float* expressions)
        {
            // Eye Expressions Set
            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = expressions[(int)Expressions.UpperLidRaiserL];
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = expressions[(int)Expressions.UpperLidRaiserR];

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = expressions[(int)Expressions.LidTightenerL];
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = expressions[(int)Expressions.LidTightenerR];

            // Brow Expressions Set
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = expressions[(int)Expressions.InnerBrowRaiserL];
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = expressions[(int)Expressions.InnerBrowRaiserR];
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = expressions[(int)Expressions.OuterBrowRaiserL];
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = expressions[(int)Expressions.OuterBrowRaiserR];

            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = expressions[(int)Expressions.BrowLowererL];
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = expressions[(int)Expressions.BrowLowererL];
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = expressions[(int)Expressions.BrowLowererR];
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = expressions[(int)Expressions.BrowLowererR];
        }

        private void SetSmooth(UnifiedExpressionShape[] unifiedExpressions, UnifiedExpressions index, float newValue)
        {
            int i = (int)index;
            float prevValue = _prevMouthWeights[i];
            float smoothedValue = prevValue + (newValue - prevValue) * 0.35f;
            _prevMouthWeights[i] = smoothedValue;
            unifiedExpressions[i].Weight = smoothedValue;
        }

        private void UpdateMouthExpressions(UnifiedExpressionShape[] unifiedExpressions, float* expressions)
        {
            // Jaw Expression Set                        
            SetSmooth(unifiedExpressions, UnifiedExpressions.JawOpen, expressions[(int)Expressions.JawDrop]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.JawLeft, expressions[(int)Expressions.JawSidewaysLeft]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.JawRight, expressions[(int)Expressions.JawSidewaysRight]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.JawForward, expressions[(int)Expressions.JawThrust]);

            // Mouth Expression Set   
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthClosed, expressions[(int)Expressions.LipsToward]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthUpperLeft, expressions[(int)Expressions.MouthLeft]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthLowerLeft, expressions[(int)Expressions.MouthLeft]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthUpperRight, expressions[(int)Expressions.MouthRight]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthLowerRight, expressions[(int)Expressions.MouthRight]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthCornerPullLeft, expressions[(int)Expressions.LipCornerPullerL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthCornerSlantLeft, expressions[(int)Expressions.LipCornerPullerL]); // Slant (Sharp Corner Raiser) is baked into Corner Puller.
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthCornerPullRight, expressions[(int)Expressions.LipCornerPullerR]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthCornerSlantRight, expressions[(int)Expressions.LipCornerPullerR]); // Slant (Sharp Corner Raiser) is baked into Corner Puller.
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthFrownLeft, expressions[(int)Expressions.LipCornerDepressorL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthFrownRight, expressions[(int)Expressions.LipCornerDepressorR]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthLowerDownLeft, expressions[(int)Expressions.LowerLipDepressorL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthLowerDownRight, expressions[(int)Expressions.LowerLipDepressorR]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthUpperUpLeft, Math.Max(0, expressions[(int)Expressions.UpperLipRaiserL] - expressions[(int)Expressions.NoseWrinklerL])); // Workaround for upper lip up wierd tracking quirk.
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthUpperDeepenLeft, Math.Max(0, expressions[(int)Expressions.UpperLipRaiserL] - expressions[(int)Expressions.NoseWrinklerL])); // Workaround for upper lip up wierd tracking quirk.
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthUpperUpRight, Math.Max(0, expressions[(int)Expressions.UpperLipRaiserR] - expressions[(int)Expressions.NoseWrinklerR])); // Workaround for upper lip up wierd tracking quirk.
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthUpperDeepenRight, Math.Max(0, expressions[(int)Expressions.UpperLipRaiserR] - expressions[(int)Expressions.NoseWrinklerR])); // Workaround for upper lip up wierd tracking quirk.

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthRaiserUpper, expressions[(int)Expressions.ChinRaiserT]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthRaiserLower, expressions[(int)Expressions.ChinRaiserB]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthDimpleLeft, expressions[(int)Expressions.DimplerL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthDimpleRight, expressions[(int)Expressions.DimplerR]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthTightenerLeft, expressions[(int)Expressions.LipTightenerL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthTightenerRight, expressions[(int)Expressions.LipTightenerR]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthPressLeft, expressions[(int)Expressions.LipPressorL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthPressRight, expressions[(int)Expressions.LipPressorR]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthStretchLeft, expressions[(int)Expressions.LipStretcherL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.MouthStretchRight, expressions[(int)Expressions.LipStretcherR]);

            // Lip Expression Set   
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipPuckerUpperRight, expressions[(int)Expressions.LipPuckerR]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipPuckerLowerRight, expressions[(int)Expressions.LipPuckerR]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipPuckerUpperLeft, expressions[(int)Expressions.LipPuckerL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipPuckerLowerLeft, expressions[(int)Expressions.LipPuckerL]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.LipFunnelUpperLeft, expressions[(int)Expressions.LipFunnelerLt]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipFunnelUpperRight, expressions[(int)Expressions.LipFunnelerRt]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipFunnelLowerLeft, expressions[(int)Expressions.LipFunnelerLb]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipFunnelLowerRight, expressions[(int)Expressions.LipFunnelerRb]);

            SetSmooth(unifiedExpressions, UnifiedExpressions.LipSuckUpperLeft, Math.Min(1f - (float)Math.Pow(expressions[(int)Expressions.UpperLipRaiserL], 1f / 6f), expressions[(int)Expressions.LipSuckLt]));
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipSuckUpperRight, Math.Min(1f - (float)Math.Pow(expressions[(int)Expressions.UpperLipRaiserR], 1f / 6f), expressions[(int)Expressions.LipSuckRt]));
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipSuckLowerLeft, expressions[(int)Expressions.LipSuckLb]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.LipSuckLowerRight, expressions[(int)Expressions.LipSuckRb]);

            // Cheek Expression Set   
            SetSmooth(unifiedExpressions, UnifiedExpressions.CheekPuffLeft, expressions[(int)Expressions.CheekPuffL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.CheekPuffRight, expressions[(int)Expressions.CheekPuffR]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.CheekSuckLeft, expressions[(int)Expressions.CheekSuckL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.CheekSuckRight, expressions[(int)Expressions.CheekSuckR]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.CheekSquintLeft, expressions[(int)Expressions.CheekRaiserL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.CheekSquintRight, expressions[(int)Expressions.CheekRaiserR]);

            // Nose Expression Set             
            SetSmooth(unifiedExpressions, UnifiedExpressions.NoseSneerLeft, expressions[(int)Expressions.NoseWrinklerL]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.NoseSneerRight, expressions[(int)Expressions.NoseWrinklerR]);

            // Tongue Expression Set   
            // Future placeholder
            SetSmooth(unifiedExpressions, UnifiedExpressions.TongueOut, expressions[(int)Expressions.TongueOut]);
            SetSmooth(unifiedExpressions, UnifiedExpressions.TongueCurlUp, expressions[(int)Expressions.TongueTipAlveolar]);
        }
        #endregion
    }
}