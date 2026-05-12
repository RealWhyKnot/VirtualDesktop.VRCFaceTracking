namespace VirtualDesktop.FaceTracking
{
    public readonly struct ConflictPair
    {
        public readonly int A;
        public readonly int B;
        public readonly string Description;

        public ConflictPair(int a, int b, string description)
        {
            A = a;
            B = b;
            Description = description;
        }
    }

    /// <summary>
    /// Single source of truth for antagonistic expression pairs. Two views:
    ///  - Detection: every pair that should be flagged in the debug log.
    ///  - Arbitration: only the pairs whose co-activation is physically impossible
    ///    and safe to suppress at runtime via subtractive coupling.
    ///
    /// Pairs left in Detection-only are ones with legitimate brief co-activation
    /// (yawning opens the jaw while the chin raises; certain phonemes co-fire
    /// LipSuck and UpperLipRaiser). Suppressing those would erase real expression.
    /// </summary>
    public static class ExpressionConflicts
    {
        public static readonly ConflictPair[] Detection =
        {
            new ConflictPair((int)Expressions.LipCornerPullerL, (int)Expressions.LipCornerDepressorL, "Smile+Frown L"),
            new ConflictPair((int)Expressions.LipCornerPullerR, (int)Expressions.LipCornerDepressorR, "Smile+Frown R"),
            new ConflictPair((int)Expressions.CheekPuffL, (int)Expressions.CheekSuckL, "CheekPuff+CheekSuck L"),
            new ConflictPair((int)Expressions.CheekPuffR, (int)Expressions.CheekSuckR, "CheekPuff+CheekSuck R"),
            new ConflictPair((int)Expressions.LipPuckerL, (int)Expressions.LipStretcherL, "Pucker+Stretch L"),
            new ConflictPair((int)Expressions.LipPuckerR, (int)Expressions.LipStretcherR, "Pucker+Stretch R"),
            new ConflictPair((int)Expressions.JawDrop, (int)Expressions.ChinRaiserB, "JawDrop+ChinRaiser"),
            new ConflictPair((int)Expressions.LipSuckLt, (int)Expressions.UpperLipRaiserL, "LipSuck+UpperLipRaiser L"),
            new ConflictPair((int)Expressions.LipSuckRt, (int)Expressions.UpperLipRaiserR, "LipSuck+UpperLipRaiser R"),
            new ConflictPair((int)Expressions.TongueOut, (int)Expressions.TongueRetreat, "TongueOut+TongueRetreat"),
        };

        public static readonly ConflictPair[] Arbitration =
        {
            new ConflictPair((int)Expressions.LipCornerPullerL, (int)Expressions.LipCornerDepressorL, "Smile+Frown L"),
            new ConflictPair((int)Expressions.LipCornerPullerR, (int)Expressions.LipCornerDepressorR, "Smile+Frown R"),
            new ConflictPair((int)Expressions.CheekPuffL, (int)Expressions.CheekSuckL, "CheekPuff+CheekSuck L"),
            new ConflictPair((int)Expressions.CheekPuffR, (int)Expressions.CheekSuckR, "CheekPuff+CheekSuck R"),
            new ConflictPair((int)Expressions.LipPuckerL, (int)Expressions.LipStretcherL, "Pucker+Stretch L"),
            new ConflictPair((int)Expressions.LipPuckerR, (int)Expressions.LipStretcherR, "Pucker+Stretch R"),
            new ConflictPair((int)Expressions.TongueOut, (int)Expressions.TongueRetreat, "TongueOut+TongueRetreat"),
        };

        // Subtractive coupling strength used during arbitration: a' = max(0, a - lambda*b).
        // 0.6 sits in the middle of the [0.5, 0.8] range that the redesign analysis
        // recommended; small enough that brief, real co-activation is still visible,
        // large enough that "smile + frown both 0.5" collapses to ~0.2 on the weaker side.
        public const float ArbitrationLambda = 0.6f;
    }
}
