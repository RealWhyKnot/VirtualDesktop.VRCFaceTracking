using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;
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
        private const float SixthRootConst = 1f / 6f;
        private const float EyeOpenThreshold = 0.95f;
        private const float MouthClosedThreshold = 0.95f;
        #endregion

        #region Fields
        private MemoryMappedFile _mappedFile;
        private MemoryMappedViewAccessor _mappedView;
        private FaceState* _faceState;
        private bool _eyeAvailable, _expressionAvailable;
        private EventWaitHandle _faceStateEvent;
        private bool? _isTracking = null;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private long _updateCount = 0;
        private double _totalUpdateTicks = 0;
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
                    if (value == true)
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
            
            Logger.LogInformation("[VirtualDesktop] Initialized successfully. Eye: {Eye}, Expression: {Expr}", _eyeAvailable, _expressionAvailable);
            
            return (_eyeAvailable, _expressionAvailable);
        }

        public override void Update()
        {
            if (Status == ModuleState.Active)
            {
                if (_faceStateEvent.WaitOne(50))
                {
                    _stopwatch.Restart();
                    UpdateTracking();
                    _stopwatch.Stop();

                    _totalUpdateTicks += _stopwatch.ElapsedTicks;
                    _updateCount++;

                    if (_updateCount >= 1000)
                    {
                        var avgMs = (_totalUpdateTicks / _updateCount) / (double)TimeSpan.TicksPerMillisecond;
                        Logger.LogDebug("[VirtualDesktop] Avg update time: {Avg:F4}ms", avgMs);
                        _updateCount = 0;
                        _totalUpdateTicks = 0;
                    }
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateTracking()
        {
            var isTracking = false;

            var faceState = _faceState;
            if (faceState != null)
            {
                var expressions = faceState->ExpressionWeights;

                if (_eyeAvailable && (faceState->LeftEyeIsValid || faceState->RightEyeIsValid))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateEyeData(UnifiedEyeData eye, float* expressions, Quaternion orientationL, Quaternion orientationR)
        {
            var leftOpenness = 1.0f - Math.Clamp(expressions[(int)Expressions.EyesClosedL]
                + expressions[(int)Expressions.CheekRaiserL] * expressions[(int)Expressions.LidTightenerL], 0.0f, 1.0f);
            eye.Left.Openness = leftOpenness >= EyeOpenThreshold ? 1.0f : leftOpenness;

            var rightOpenness = 1.0f - Math.Clamp(expressions[(int)Expressions.EyesClosedR]
                + expressions[(int)Expressions.CheekRaiserR] * expressions[(int)Expressions.LidTightenerR], 0.0f, 1.0f);
            eye.Right.Openness = rightOpenness >= EyeOpenThreshold ? 1.0f : rightOpenness;

            eye.Right.Gaze = orientationR.Cartesian();
            eye.Left.Gaze = orientationL.Cartesian();

            eye.Left.PupilDiameter_MM = 5f;
            eye.Right.PupilDiameter_MM = 5f;

            eye._minDilation = 0;
            eye._maxDilation = 10;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateEyeExpressions(UnifiedExpressionShape[] unifiedExpressions, float* expressions)
        {
            var tongueUp = expressions[(int)Expressions.TongueTipAlveolar];

            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = expressions[(int)Expressions.UpperLidRaiserL];
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = expressions[(int)Expressions.UpperLidRaiserR];

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = expressions[(int)Expressions.LidTightenerL];
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = expressions[(int)Expressions.LidTightenerR];

            var browInnerUpL = expressions[(int)Expressions.InnerBrowRaiserL];
            var browInnerUpR = expressions[(int)Expressions.InnerBrowRaiserR];
            var browOuterUpL = expressions[(int)Expressions.OuterBrowRaiserL];
            var browOuterUpR = expressions[(int)Expressions.OuterBrowRaiserR];

            // Link tongue up to eyebrows
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = Math.Clamp(browInnerUpL + tongueUp, 0.0f, 1.0f);
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = Math.Clamp(browInnerUpR + tongueUp, 0.0f, 1.0f);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = Math.Clamp(browOuterUpL + tongueUp, 0.0f, 1.0f);
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = Math.Clamp(browOuterUpR + tongueUp, 0.0f, 1.0f);

            var browLowerL = expressions[(int)Expressions.BrowLowererL];
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = browLowerL;
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = browLowerL;

            var browLowerR = expressions[(int)Expressions.BrowLowererR];
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = browLowerR;
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = browLowerR;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateMouthExpressions(UnifiedExpressionShape[] unifiedExpressions, float* expressions)
        {
            var tongueOut = expressions[(int)Expressions.TongueOut];
            var jawOpen = expressions[(int)Expressions.JawDrop];

            // Ensure mouth is open when tongue is out
            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight = Math.Max(jawOpen, tongueOut);
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight = expressions[(int)Expressions.JawSidewaysLeft];
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight = expressions[(int)Expressions.JawSidewaysRight];
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = expressions[(int)Expressions.JawThrust];

            var mouthClosed = expressions[(int)Expressions.LipsToward];
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = mouthClosed >= MouthClosedThreshold ? 1.0f : mouthClosed;

            var mouthLeft = expressions[(int)Expressions.MouthLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight = mouthLeft;
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight = mouthLeft;

            var mouthRight = expressions[(int)Expressions.MouthRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = mouthRight;
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = mouthRight;

            var lipCornerPullL = expressions[(int)Expressions.LipCornerPullerL];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = lipCornerPullL;
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = lipCornerPullL;

            var lipCornerPullR = expressions[(int)Expressions.LipCornerPullerR];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight = lipCornerPullR;
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = lipCornerPullR;

            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight = expressions[(int)Expressions.LipCornerDepressorL];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = expressions[(int)Expressions.LipCornerDepressorR];

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = expressions[(int)Expressions.LowerLipDepressorL];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight = expressions[(int)Expressions.LowerLipDepressorR];

            var upperLipRaiserL = expressions[(int)Expressions.UpperLipRaiserL];
            var noseWrinklerL = expressions[(int)Expressions.NoseWrinklerL];
            var upperUpL = Math.Max(0, upperLipRaiserL - noseWrinklerL);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = upperUpL;
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight = upperUpL;

            var upperLipRaiserR = expressions[(int)Expressions.UpperLipRaiserR];
            var noseWrinklerR = expressions[(int)Expressions.NoseWrinklerR];
            var upperUpR = Math.Max(0, upperLipRaiserR - noseWrinklerR);
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight = upperUpR;
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = upperUpR;

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = expressions[(int)Expressions.ChinRaiserT];
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = expressions[(int)Expressions.ChinRaiserB];

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight = expressions[(int)Expressions.DimplerL];
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = expressions[(int)Expressions.DimplerR];

            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerLeft].Weight = expressions[(int)Expressions.LipTightenerL];
            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerRight].Weight = expressions[(int)Expressions.LipTightenerR];

            unifiedExpressions[(int)UnifiedExpressions.MouthPressLeft].Weight = expressions[(int)Expressions.LipPressorL];
            unifiedExpressions[(int)UnifiedExpressions.MouthPressRight].Weight = expressions[(int)Expressions.LipPressorR];

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight = expressions[(int)Expressions.LipStretcherL];
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = expressions[(int)Expressions.LipStretcherR];

            var lipPuckerR = expressions[(int)Expressions.LipPuckerR];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = lipPuckerR;
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = lipPuckerR;

            var lipPuckerL = expressions[(int)Expressions.LipPuckerL];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = lipPuckerL;
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = lipPuckerL;

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = expressions[(int)Expressions.LipFunnelerLt];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = expressions[(int)Expressions.LipFunnelerRt];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = expressions[(int)Expressions.LipFunnelerLb];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = expressions[(int)Expressions.LipFunnelerRb];

            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = Math.Min(1f - (float)Math.Pow(upperLipRaiserL, SixthRootConst), expressions[(int)Expressions.LipSuckLt]);
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = Math.Min(1f - (float)Math.Pow(upperLipRaiserR, SixthRootConst), expressions[(int)Expressions.LipSuckRt]);
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = expressions[(int)Expressions.LipSuckLb];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = expressions[(int)Expressions.LipSuckRb];

            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight = expressions[(int)Expressions.CheekPuffL];
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight = expressions[(int)Expressions.CheekPuffR];
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight = expressions[(int)Expressions.CheekSuckL];
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight = expressions[(int)Expressions.CheekSuckR];
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight = expressions[(int)Expressions.CheekRaiserL];
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = expressions[(int)Expressions.CheekRaiserR];

            unifiedExpressions[(int)UnifiedExpressions.NoseSneerLeft].Weight = noseWrinklerL;
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerRight].Weight = noseWrinklerR;

            // Tongue Expression Set   
            var tongueUp = expressions[(int)Expressions.TongueTipAlveolar];
            var browUpAvg = (expressions[(int)Expressions.InnerBrowRaiserL] + expressions[(int)Expressions.InnerBrowRaiserR] + 
                             expressions[(int)Expressions.OuterBrowRaiserL] + expressions[(int)Expressions.OuterBrowRaiserR]) / 4.0f;
            
            unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = tongueOut;
            unifiedExpressions[(int)UnifiedExpressions.TongueCurlUp].Weight = Math.Clamp(tongueUp + browUpAvg, 0.0f, 1.0f);
        }
        #endregion
    }
}