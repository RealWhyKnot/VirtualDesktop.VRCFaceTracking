using System;
using System.IO;
using System.Text;

namespace VirtualDesktop.FaceTracking
{
    public unsafe class TrackingDiagnostics : IDisposable
    {
        #region Constants
        private const int FrameRate = 72;
        private const int LogIntervalFrames = 72;            // ~1 second
        private const int SummaryIntervalFrames = 72 * 30;   // ~30 seconds
        private const int ExpressionCount = FaceState.ExpressionCount;

        // Anomaly thresholds
        private const float EyeOpennessMismatchThreshold = 0.6f;
        private const float GazeAngleDivergenceDeg = 15.0f;
        private const int StuckFrames = (int)(5.0f * FrameRate);       // 5 seconds
        private const int FloorAboveRawLogFrames = 72;                 // log once/sec
        private const float NaturalAsymmetryMax = 0.3f;

        // "Looking dumb" duration thresholds (in frames)
        private const int AsymEyeFrames = (int)(0.6f * FrameRate);
        private const int MouthStuckOpenFrames = (int)(2.0f * FrameRate);
        private const int MouthAsymmetryFrames = (int)(1.5f * FrameRate);
        private const int AllZeroFrames = (int)(0.3f * FrameRate);
        private const int FrozenFaceFrames = (int)(3.0f * FrameRate);
        private const float FrozenDeltaThreshold = 0.015f;

        // Conflict pair cooldown
        private const int ConflictCooldownFrames = 72; // log at most once/sec per pair
        #endregion

        #region Expression conflict pairs
        private static readonly (int A, int B, string Desc)[] ConflictPairs =
        {
            ((int)Expressions.LipCornerPullerL, (int)Expressions.LipCornerDepressorL, "Smile+Frown L"),
            ((int)Expressions.LipCornerPullerR, (int)Expressions.LipCornerDepressorR, "Smile+Frown R"),
            ((int)Expressions.CheekPuffL, (int)Expressions.CheekSuckL, "CheekPuff+CheekSuck L"),
            ((int)Expressions.CheekPuffR, (int)Expressions.CheekSuckR, "CheekPuff+CheekSuck R"),
            ((int)Expressions.LipPuckerL, (int)Expressions.LipStretcherL, "Pucker+Stretch L"),
            ((int)Expressions.LipPuckerR, (int)Expressions.LipStretcherR, "Pucker+Stretch R"),
            ((int)Expressions.JawDrop, (int)Expressions.ChinRaiserB, "JawDrop+ChinRaiser"),
            ((int)Expressions.LipSuckLt, (int)Expressions.UpperLipRaiserL, "LipSuck+UpperLipRaiser L"),
            ((int)Expressions.LipSuckRt, (int)Expressions.UpperLipRaiserR, "LipSuck+UpperLipRaiser R"),
            ((int)Expressions.TongueOut, (int)Expressions.TongueRetreat, "TongueOut+TongueRetreat"),
        };

        // Symmetric mouth expression pairs for asymmetry detection
        private static readonly (int L, int R, string Name)[] SymmetricPairs =
        {
            ((int)Expressions.LipCornerPullerL, (int)Expressions.LipCornerPullerR, "LipCornerPuller"),
            ((int)Expressions.LipCornerDepressorL, (int)Expressions.LipCornerDepressorR, "LipCornerDepressor"),
            ((int)Expressions.CheekRaiserL, (int)Expressions.CheekRaiserR, "CheekRaiser"),
            ((int)Expressions.CheekPuffL, (int)Expressions.CheekPuffR, "CheekPuff"),
            ((int)Expressions.LipStretcherL, (int)Expressions.LipStretcherR, "LipStretcher"),
        };
        #endregion

        #region Expression category groups (for log formatting)
        private static readonly (string Name, int Start, int End)[] ExpressionGroups =
        {
            ("EYES", (int)Expressions.EyesClosedL, (int)Expressions.EyesLookUpR),
            ("BROW", (int)Expressions.BrowLowererL, (int)Expressions.BrowLowererR),
            ("BROW", (int)Expressions.InnerBrowRaiserL, (int)Expressions.InnerBrowRaiserR),
            ("BROW", (int)Expressions.OuterBrowRaiserL, (int)Expressions.OuterBrowRaiserR),
            ("LID",  (int)Expressions.LidTightenerL, (int)Expressions.LidTightenerR),
            ("LID",  (int)Expressions.UpperLidRaiserL, (int)Expressions.UpperLidRaiserR),
            ("JAW",  (int)Expressions.JawDrop, (int)Expressions.JawThrust),
            ("MOUTH", (int)Expressions.LipCornerDepressorL, (int)Expressions.LipCornerPullerR),
            ("LIP",  (int)Expressions.LipFunnelerLb, (int)Expressions.LipTightenerR),
            ("LIP",  (int)Expressions.LipsToward, (int)Expressions.LipsToward),
            ("LIP",  (int)Expressions.LowerLipDepressorL, (int)Expressions.LowerLipDepressorR),
            ("LIP",  (int)Expressions.UpperLipRaiserL, (int)Expressions.UpperLipRaiserR),
            ("CHEEK", (int)Expressions.CheekPuffL, (int)Expressions.CheekSuckR),
            ("CHEEK", (int)Expressions.CheekRaiserL, (int)Expressions.CheekRaiserR),
            ("CHIN", (int)Expressions.ChinRaiserB, (int)Expressions.ChinRaiserT),
            ("DIMPLE", (int)Expressions.DimplerL, (int)Expressions.DimplerR),
            ("MOUTH", (int)Expressions.MouthLeft, (int)Expressions.MouthRight),
            ("NOSE", (int)Expressions.NoseWrinklerL, (int)Expressions.NoseWrinklerR),
            ("TONGUE", (int)Expressions.TongueTipInterdental, (int)Expressions.TongueRetreat),
        };
        #endregion

        #region Fields
        private readonly StreamWriter _writer;
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private int _frameCount;

        // Per-expression anomaly state
        private readonly int[] _stuckAtZeroFrames = new int[ExpressionCount];
        private readonly int[] _stuckAtOneFrames = new int[ExpressionCount];
        private readonly bool[] _wasStuckZero = new bool[ExpressionCount];
        private readonly bool[] _wasStuckOne = new bool[ExpressionCount];
        private readonly int[] _floorAboveRawFrames = new int[ExpressionCount];
        private readonly float[] _prevCalibrated = new float[ExpressionCount];

        // Conflict pair cooldowns
        private readonly int[] _conflictCooldown = new int[ConflictPairs.Length];

        // Eye state
        private int _eyeMismatchFrames;
        private bool _wasEyeMismatch;

        // "Looking dumb" counters
        private int _asymEyeFrames;
        private bool _wasAsymEye;
        private int _mouthStuckOpenFrames;
        private bool _wasMouthStuckOpen;
        private readonly int[] _mouthAsymFrames = new int[SymmetricPairs.Length];
        private readonly bool[] _wasMouthAsym = new bool[SymmetricPairs.Length];
        private int _allZeroFrames;
        private bool _wasAllZero;
        private int _frozenFrames;
        private bool _wasFrozen;

        // Cached expression names
        private static readonly string[] ExpressionNames;
        #endregion

        #region Static Constructor
        static TrackingDiagnostics()
        {
            ExpressionNames = new string[ExpressionCount];
            var values = Enum.GetValues(typeof(Expressions));
            foreach (Expressions e in values)
            {
                int idx = (int)e;
                if (idx >= 0 && idx < ExpressionCount)
                    ExpressionNames[idx] = e.ToString();
            }
            for (int i = 0; i < ExpressionCount; i++)
            {
                if (ExpressionNames[i] == null)
                    ExpressionNames[i] = $"Expr{i}";
            }
        }
        #endregion

        #region Constructor / Dispose
        public TrackingDiagnostics(string logPath)
        {
            _writer = new StreamWriter(logPath, append: false, Encoding.UTF8, bufferSize: 8192);
            _writer.AutoFlush = false;
            Log("DIAG", $"=== Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _writer.Flush();
        }

        public void Dispose()
        {
            Log("DIAG", $"=== Session ended {DateTime.Now:yyyy-MM-dd HH:mm:ss} (frames={_frameCount}) ===");
            _writer.Flush();
            _writer.Dispose();
        }
        #endregion

        #region Logging helpers
        private void Log(string tag, string message)
        {
            _writer.Write($"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {message}\n");
        }

        private void LogImmediate(string tag, string message)
        {
            Log(tag, message);
            _writer.Flush();
        }
        #endregion

        #region Core diagnostic methods

        /// <summary>
        /// Called after CalibrateAll(). Runs per-expression anomaly checks every frame,
        /// logs calibration snapshots once per second, and health summaries every 30 seconds.
        /// </summary>
        public void OnFrameBegin(float* rawExpressions, ExpressionCalibrator calibrator, float[] calibrated)
        {
            _frameCount++;

            // Per-expression anomaly detection (every frame, log on transitions)
            CheckStuckExpressions(calibrated);
            CheckFloorAboveRaw(rawExpressions, calibrator);

            // Regular calibration snapshot (~1 second)
            if (_frameCount % LogIntervalFrames == 0)
                LogCalibrationSnapshot(rawExpressions, calibrator, calibrated);

            // Health summary (~30 seconds)
            if (_frameCount % SummaryIntervalFrames == 0)
                LogCalibrationSummary(rawExpressions, calibrator, calibrated);

            // Store for next frame comparison
            Array.Copy(calibrated, _prevCalibrated, ExpressionCount);
        }

        /// <summary>
        /// Called after UpdateEyeData(). Checks eye sync and gaze divergence.
        /// </summary>
        public void OnEyeData(
            float postSyncOpenL, float postSyncOpenR,
            float preSyncOpenL, float preSyncOpenR,
            Quaternion orientationL, Quaternion orientationR,
            float leftConfidence, float rightConfidence)
        {
            // Eye openness mismatch detection
            float preSyncDelta = Math.Abs(preSyncOpenL - preSyncOpenR);
            if (preSyncDelta > EyeOpennessMismatchThreshold)
            {
                _eyeMismatchFrames++;
                if (!_wasEyeMismatch || _eyeMismatchFrames % LogIntervalFrames == 0)
                {
                    LogImmediate("SYNC",
                        $"Eye openness mismatch: preSyncL={preSyncOpenL:F3} preSyncR={preSyncOpenR:F3} delta={preSyncDelta:F3} " +
                        $"=> postSyncL={postSyncOpenL:F3} postSyncR={postSyncOpenR:F3} " +
                        $"(syncCorrL={postSyncOpenL - preSyncOpenL:+0.000;-0.000} syncCorrR={postSyncOpenR - preSyncOpenR:+0.000;-0.000})");
                }
                _wasEyeMismatch = true;
            }
            else
            {
                if (_wasEyeMismatch)
                    Log("SYNC", $"Eye openness mismatch resolved after {_eyeMismatchFrames / (float)FrameRate:F1}s");
                _eyeMismatchFrames = 0;
                _wasEyeMismatch = false;
            }

            // Gaze divergence (check once per second)
            if (_frameCount % LogIntervalFrames == 0)
            {
                var gazeL = orientationL.Cartesian();
                var gazeR = orientationR.Cartesian();
                float pitchDiff = Math.Abs(gazeL.x - gazeR.x) * (180f / (float)Math.PI);
                float yawDiff = Math.Abs(gazeL.y - gazeR.y) * (180f / (float)Math.PI);
                float totalDivergence = (float)Math.Sqrt(pitchDiff * pitchDiff + yawDiff * yawDiff);

                if (totalDivergence > GazeAngleDivergenceDeg)
                {
                    Log("EYE",
                        $"Gaze divergence: {totalDivergence:F1}deg " +
                        $"(pitchDiff={pitchDiff:F1} yawDiff={yawDiff:F1}) " +
                        $"L=({gazeL.x * 180f / (float)Math.PI:F1},{gazeL.y * 180f / (float)Math.PI:F1}) " +
                        $"R=({gazeR.x * 180f / (float)Math.PI:F1},{gazeR.y * 180f / (float)Math.PI:F1})");
                }

                // Low confidence warning
                if (leftConfidence < 0.5f || rightConfidence < 0.5f)
                    Log("EYE", $"Low confidence: L={leftConfidence:F2} R={rightConfidence:F2}");
            }
        }

        /// <summary>
        /// Called after UpdateMouthExpressions(). Checks conflicts and "looking dumb" heuristics.
        /// </summary>
        public void OnMouthExpressionsComplete(float[] calibrated)
        {
            CheckConflicts(calibrated);
            CheckLookingDumb(calibrated);
        }

        /// <summary>
        /// Called when tracking goes inactive and calibration resets.
        /// </summary>
        public void OnTrackingReset()
        {
            LogImmediate("DIAG", "Tracking reset - calibration cleared");
            Array.Clear(_stuckAtZeroFrames, 0, ExpressionCount);
            Array.Clear(_stuckAtOneFrames, 0, ExpressionCount);
            Array.Clear(_wasStuckZero, 0, ExpressionCount);
            Array.Clear(_wasStuckOne, 0, ExpressionCount);
            Array.Clear(_floorAboveRawFrames, 0, ExpressionCount);
            Array.Clear(_prevCalibrated, 0, ExpressionCount);
            Array.Clear(_conflictCooldown, 0, _conflictCooldown.Length);
            _eyeMismatchFrames = 0;
            _wasEyeMismatch = false;
            _asymEyeFrames = 0;
            _wasAsymEye = false;
            _mouthStuckOpenFrames = 0;
            _wasMouthStuckOpen = false;
            Array.Clear(_mouthAsymFrames, 0, _mouthAsymFrames.Length);
            Array.Clear(_wasMouthAsym, 0, _wasMouthAsym.Length);
            _allZeroFrames = 0;
            _wasAllZero = false;
            _frozenFrames = 0;
            _wasFrozen = false;
        }

        /// <summary>
        /// Throttled flush - call once per frame, only actually flushes once per second.
        /// </summary>
        public void Flush()
        {
            if (_frameCount % LogIntervalFrames == 0)
                _writer.Flush();
        }

        #endregion

        #region Stuck expression detection

        private void CheckStuckExpressions(float[] calibrated)
        {
            for (int i = 0; i < ExpressionCount; i++)
            {
                // Stuck at zero
                if (calibrated[i] <= 0.001f)
                {
                    _stuckAtZeroFrames[i]++;
                    if (_stuckAtZeroFrames[i] == StuckFrames && !_wasStuckZero[i])
                    {
                        LogImmediate("STUCK", $"{ExpressionNames[i]} stuck at 0.0 for {StuckFrames / (float)FrameRate:F1}s");
                        _wasStuckZero[i] = true;
                    }
                }
                else
                {
                    if (_wasStuckZero[i])
                    {
                        Log("STUCK", $"{ExpressionNames[i]} unstuck from 0.0 after {_stuckAtZeroFrames[i] / (float)FrameRate:F1}s");
                        _wasStuckZero[i] = false;
                    }
                    _stuckAtZeroFrames[i] = 0;
                }

                // Stuck at one
                if (calibrated[i] >= 0.99f)
                {
                    _stuckAtOneFrames[i]++;
                    if (_stuckAtOneFrames[i] == StuckFrames && !_wasStuckOne[i])
                    {
                        LogImmediate("STUCK", $"{ExpressionNames[i]} stuck at 1.0 for {StuckFrames / (float)FrameRate:F1}s");
                        _wasStuckOne[i] = true;
                    }
                }
                else
                {
                    if (_wasStuckOne[i])
                    {
                        Log("STUCK", $"{ExpressionNames[i]} unstuck from 1.0 after {_stuckAtOneFrames[i] / (float)FrameRate:F1}s");
                        _wasStuckOne[i] = false;
                    }
                    _stuckAtOneFrames[i] = 0;
                }
            }
        }

        #endregion

        #region Floor above raw detection

        private void CheckFloorAboveRaw(float* raw, ExpressionCalibrator calibrator)
        {
            for (int i = 0; i < ExpressionCount; i++)
            {
                if (calibrator.IsPassthrough(i) || calibrator.GetSampleCount(i) <= 10)
                    continue;

                if (raw[i] < calibrator.GetFloor(i))
                {
                    _floorAboveRawFrames[i]++;
                    if (_floorAboveRawFrames[i] % FloorAboveRawLogFrames == 0)
                    {
                        Log("CAL", $"{ExpressionNames[i]}: floor({calibrator.GetFloor(i):F3}) > raw({raw[i]:F3}) for {_floorAboveRawFrames[i] / (float)FrameRate:F1}s - calibration unconverged");
                    }
                }
                else
                {
                    _floorAboveRawFrames[i] = 0;
                }
            }
        }

        #endregion

        #region Calibration snapshot (once per second)

        private void LogCalibrationSnapshot(float* raw, ExpressionCalibrator calibrator, float[] calibrated)
        {
            _sb.Clear();
            _sb.AppendLine($"Frame {_frameCount} snapshot ({ExpressionCount} expressions):");

            for (int i = 0; i < ExpressionCount; i++)
            {
                if (calibrator.IsPassthrough(i))
                    continue;

                string status = calibrator.IsWarmedUp(i) ? "ready" : $"warmup {calibrator.GetSampleCount(i) * 100 / 120}%";
                string cat = GetCategory(i);
                _sb.Append($"  {cat,-6} {ExpressionNames[i],-25} raw={raw[i]:F3} flr={calibrator.GetFloor(i):F3} ceil={calibrator.GetCeiling(i):F3} cal={calibrated[i]:F3} [{status}]\n");
            }

            // Write as a single block
            Log("CAL", _sb.ToString());
        }

        private static string GetCategory(int index)
        {
            foreach (var g in ExpressionGroups)
            {
                if (index >= g.Start && index <= g.End)
                    return g.Name;
            }
            return "OTHER";
        }

        #endregion

        #region Calibration health summary (every 30 seconds)

        private void LogCalibrationSummary(float* raw, ExpressionCalibrator calibrator, float[] calibrated)
        {
            _sb.Clear();
            _sb.AppendLine($"=== Calibration Health (frame {_frameCount}, +{_frameCount / FrameRate}s) ===");

            int converged = 0, warmup = 0, problematic = 0, passthrough = 0;
            var convergedNames = new StringBuilder();
            var warmupNames = new StringBuilder();
            var problemDetails = new StringBuilder();
            var passthroughNames = new StringBuilder();

            for (int i = 0; i < ExpressionCount; i++)
            {
                if (calibrator.IsPassthrough(i))
                {
                    passthrough++;
                    if (passthroughNames.Length > 0) passthroughNames.Append(' ');
                    passthroughNames.Append(ExpressionNames[i]);
                    continue;
                }

                bool isProblematic = false;

                if (!calibrator.IsWarmedUp(i))
                {
                    warmup++;
                    if (warmupNames.Length > 0) warmupNames.Append(' ');
                    warmupNames.Append(ExpressionNames[i]);
                    continue;
                }

                // Check for problems
                float ceil = calibrator.GetCeiling(i);
                float flr = calibrator.GetFloor(i);

                if (ceil < 0.15f)
                {
                    isProblematic = true;
                    problemDetails.AppendLine($"  {ExpressionNames[i]}: ceiling={ceil:F3} (too low - user may not have activated this expression)");
                }

                if (_stuckAtZeroFrames[i] >= StuckFrames)
                {
                    isProblematic = true;
                    problemDetails.AppendLine($"  {ExpressionNames[i]}: stuck at 0.0 for {_stuckAtZeroFrames[i] / (float)FrameRate:F1}s");
                }

                if (_stuckAtOneFrames[i] >= StuckFrames)
                {
                    isProblematic = true;
                    problemDetails.AppendLine($"  {ExpressionNames[i]}: stuck at 1.0 for {_stuckAtOneFrames[i] / (float)FrameRate:F1}s");
                }

                if (_floorAboveRawFrames[i] >= FloorAboveRawLogFrames)
                {
                    isProblematic = true;
                    problemDetails.AppendLine($"  {ExpressionNames[i]}: floor({flr:F3}) persistently above raw({raw[i]:F3})");
                }

                if (isProblematic)
                    problematic++;
                else
                {
                    converged++;
                    if (convergedNames.Length > 0) convergedNames.Append(' ');
                    convergedNames.Append(ExpressionNames[i]);
                }
            }

            int calibratable = ExpressionCount - passthrough;
            _sb.AppendLine($"Converged ({converged}/{calibratable}): {convergedNames}");
            if (warmup > 0)
                _sb.AppendLine($"Warmup ({warmup}/{calibratable}): {warmupNames}");
            if (problematic > 0)
            {
                _sb.AppendLine($"Problematic ({problematic}/{calibratable}):");
                _sb.Append(problemDetails);
            }
            _sb.AppendLine($"Passthrough ({passthrough}): {passthroughNames}");
            _sb.Append("===");

            Log("SUM", _sb.ToString());
            _writer.Flush();
        }

        #endregion

        #region Conflict detection

        private void CheckConflicts(float[] calibrated)
        {
            for (int p = 0; p < ConflictPairs.Length; p++)
            {
                // Decrement cooldown
                if (_conflictCooldown[p] > 0)
                {
                    _conflictCooldown[p]--;
                    continue;
                }

                var pair = ConflictPairs[p];
                if (calibrated[pair.A] > 0.5f && calibrated[pair.B] > 0.5f)
                {
                    LogImmediate("ANOM",
                        $"Conflict: {pair.Desc} ({ExpressionNames[pair.A]}={calibrated[pair.A]:F2} {ExpressionNames[pair.B]}={calibrated[pair.B]:F2})");
                    _conflictCooldown[p] = ConflictCooldownFrames;
                }
            }
        }

        #endregion

        #region "Looking dumb" heuristics

        private void CheckLookingDumb(float[] calibrated)
        {
            CheckAsymmetricEyes(calibrated);
            CheckMouthStuckOpen(calibrated);
            CheckMouthAsymmetry(calibrated);
            CheckAllZero(calibrated);
            CheckFrozenFace(calibrated);
        }

        /// <summary>
        /// One eye nearly closed, other wide open, without wink indicators.
        /// </summary>
        private void CheckAsymmetricEyes(float[] calibrated)
        {
            float eyeClosedL = calibrated[(int)Expressions.EyesClosedL];
            float eyeClosedR = calibrated[(int)Expressions.EyesClosedR];
            float cheekL = calibrated[(int)Expressions.CheekRaiserL];
            float cheekR = calibrated[(int)Expressions.CheekRaiserR];
            float lidL = calibrated[(int)Expressions.LidTightenerL];
            float lidR = calibrated[(int)Expressions.LidTightenerR];

            // One eye much more closed than the other
            bool asymmetric = Math.Abs(eyeClosedL - eyeClosedR) > 0.45f;

            // Wink indicators: CheekRaiser + LidTightener on the more closed side
            bool winkContextL = cheekL > 0.3f || lidL > 0.3f;
            bool winkContextR = cheekR > 0.3f || lidR > 0.3f;
            bool hasWinkContext = (eyeClosedL > eyeClosedR) ? winkContextL : winkContextR;

            if (asymmetric && !hasWinkContext)
            {
                _asymEyeFrames++;
                if (_asymEyeFrames == AsymEyeFrames || (_wasAsymEye && _asymEyeFrames % LogIntervalFrames == 0))
                {
                    LogImmediate("DUMB",
                        $"Asymmetric eyes without wink context: EyesClosedL={eyeClosedL:F2} EyesClosedR={eyeClosedR:F2} " +
                        $"(no matching CheekRaiser/LidTightener) for {_asymEyeFrames / (float)FrameRate:F1}s");
                    _wasAsymEye = true;
                }
            }
            else
            {
                if (_wasAsymEye)
                    Log("DUMB", $"Asymmetric eyes resolved after {_asymEyeFrames / (float)FrameRate:F1}s");
                _asymEyeFrames = 0;
                _wasAsymEye = false;
            }
        }

        /// <summary>
        /// Jaw slightly open for extended period without mouth-closed intent.
        /// </summary>
        private void CheckMouthStuckOpen(float[] calibrated)
        {
            float jaw = calibrated[(int)Expressions.JawDrop];
            float lipsToward = calibrated[(int)Expressions.LipsToward];

            if (jaw > 0.15f && jaw < 0.35f && lipsToward < 0.1f)
            {
                _mouthStuckOpenFrames++;
                if (_mouthStuckOpenFrames == MouthStuckOpenFrames || (_wasMouthStuckOpen && _mouthStuckOpenFrames % LogIntervalFrames == 0))
                {
                    LogImmediate("DUMB",
                        $"Mouth stuck slightly open: jaw={jaw:F2} lipsToward={lipsToward:F2} for {_mouthStuckOpenFrames / (float)FrameRate:F1}s");
                    _wasMouthStuckOpen = true;
                }
            }
            else
            {
                if (_wasMouthStuckOpen)
                    Log("DUMB", $"Mouth stuck open resolved after {_mouthStuckOpenFrames / (float)FrameRate:F1}s");
                _mouthStuckOpenFrames = 0;
                _wasMouthStuckOpen = false;
            }
        }

        /// <summary>
        /// Large L/R difference in symmetric mouth expressions.
        /// </summary>
        private void CheckMouthAsymmetry(float[] calibrated)
        {
            for (int p = 0; p < SymmetricPairs.Length; p++)
            {
                var pair = SymmetricPairs[p];
                float delta = Math.Abs(calibrated[pair.L] - calibrated[pair.R]);

                if (delta > NaturalAsymmetryMax && (calibrated[pair.L] > 0.3f || calibrated[pair.R] > 0.3f))
                {
                    _mouthAsymFrames[p]++;
                    if (_mouthAsymFrames[p] == MouthAsymmetryFrames || (_wasMouthAsym[p] && _mouthAsymFrames[p] % LogIntervalFrames == 0))
                    {
                        LogImmediate("DUMB",
                            $"Mouth asymmetry: {pair.Name} L={calibrated[pair.L]:F2} R={calibrated[pair.R]:F2} delta={delta:F2} for {_mouthAsymFrames[p] / (float)FrameRate:F1}s");
                        _wasMouthAsym[p] = true;
                    }
                }
                else
                {
                    if (_wasMouthAsym[p])
                        Log("DUMB", $"{pair.Name} asymmetry resolved after {_mouthAsymFrames[p] / (float)FrameRate:F1}s");
                    _mouthAsymFrames[p] = 0;
                    _wasMouthAsym[p] = false;
                }
            }
        }

        /// <summary>
        /// All non-passthrough expressions at zero (tracking dropout).
        /// </summary>
        private void CheckAllZero(float[] calibrated)
        {
            bool allZero = true;
            for (int i = 0; i < ExpressionCount; i++)
            {
                if (calibrated[i] > 0.001f)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                _allZeroFrames++;
                if (_allZeroFrames == AllZeroFrames && !_wasAllZero)
                {
                    LogImmediate("DUMB", $"All expressions zero - possible tracking dropout ({_allZeroFrames / (float)FrameRate:F1}s)");
                    _wasAllZero = true;
                }
            }
            else
            {
                if (_wasAllZero)
                    Log("DUMB", $"Tracking dropout resolved after {_allZeroFrames / (float)FrameRate:F1}s");
                _allZeroFrames = 0;
                _wasAllZero = false;
            }
        }

        /// <summary>
        /// Face completely frozen (no expression changes across frames).
        /// </summary>
        private void CheckFrozenFace(float[] calibrated)
        {
            float totalDelta = 0f;
            for (int i = 0; i < ExpressionCount; i++)
                totalDelta += Math.Abs(calibrated[i] - _prevCalibrated[i]);

            if (totalDelta < FrozenDeltaThreshold)
            {
                _frozenFrames++;
                if (_frozenFrames == FrozenFaceFrames && !_wasFrozen)
                {
                    LogImmediate("DUMB", $"Face frozen - no expression change for {_frozenFrames / (float)FrameRate:F1}s (total delta={totalDelta:F4})");
                    _wasFrozen = true;
                }
            }
            else
            {
                if (_wasFrozen)
                    Log("DUMB", $"Face frozen resolved after {_frozenFrames / (float)FrameRate:F1}s");
                _frozenFrames = 0;
                _wasFrozen = false;
            }
        }

        #endregion
    }
}
