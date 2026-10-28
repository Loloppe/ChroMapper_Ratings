using System;

namespace Ratings.AccAi
{
    // Same as RatingAPI's AccRating (portaBLerebalancer2), see the comments there for where the constants come from.
    public class AccRating
    {
        // acc rating = ACC_SCALE x Curve(EffectiveAcc(predictedAcc, pass, hit distance) + ACC_OFFSET) ^ -ACC_EXPONENT
        public const double ACC_SCALE = 13.5382;
        public const double ACC_EXPONENT = 0.8222;
        public const double ACC_OFFSET = 0.0019;

        // Hard map correction: below HARD_MAP_KNEE predicted acc, the acc rating uses a predicted acc lowered by
        // HARD_MAP_SLOPE times the distance below the knee, capped at HARD_MAP_FLOOR
        public const double HARD_MAP_KNEE = 0.9675;
        public const double HARD_MAP_SLOPE = 1.24;
        public const double HARD_MAP_FLOOR = 0.95;

        // Movement and high pass corrections: -3% acc per meter of mean hit distance above 0.62 m (capped at 0.8 m, phased
        // out between 97% and 97.5% predicted acc), and -0.214% per pass point above 14.52
        public const double HIT_DISTANCE_KNEE = 0.62;
        public const double HIT_DISTANCE_SLOPE = 0.030;
        public const double HIT_DISTANCE_CAP = 0.80;
        public const double HIT_DISTANCE_FULL_BELOW = 0.97;
        public const double HIT_DISTANCE_NONE_ABOVE = 0.975;
        public const double HIGH_PASS_KNEE = 14.52;
        public const double HIGH_PASS_SLOPE = 0.00214;

        // Very high pass buff: HIGH_PASS_BUFF less effective acc from HIGH_PASS_BUFF_FULL pass, ramped in linearly from
        // HIGH_PASS_BUFF_START
        public const double HIGH_PASS_BUFF_START = 13.5;
        public const double HIGH_PASS_BUFF_FULL = 14.5;
        public const double HIGH_PASS_BUFF = 0.0055;

        // The predicted acc the acc rating uses (see the corrections above). passRating: the pass rating; hitDistance:
        // Ratings.HitDistance. 0 leaves that correction out.
        public static double EffectiveAcc(double predictedAcc, double passRating = 0, double hitDistance = 0) =>
            predictedAcc - HARD_MAP_SLOPE * Math.Max(0, HARD_MAP_KNEE - Math.Max(predictedAcc, HARD_MAP_FLOOR))
            - HIT_DISTANCE_SLOPE * Math.Max(0, Math.Min(hitDistance, HIT_DISTANCE_CAP) - HIT_DISTANCE_KNEE)
                * Math.Clamp((HIT_DISTANCE_NONE_ABOVE - predictedAcc) / (HIT_DISTANCE_NONE_ABOVE - HIT_DISTANCE_FULL_BELOW), 0, 1)
            - HIGH_PASS_SLOPE * Math.Max(0, passRating - HIGH_PASS_KNEE)
            - HIGH_PASS_BUFF * Math.Clamp((passRating - HIGH_PASS_BUFF_START) / (HIGH_PASS_BUFF_FULL - HIGH_PASS_BUFF_START), 0, 1);

        // Hard swing correction: the acc rating is multiplied by 1 + HARD_SWING_SLOPE x the excess of (ratio - 1) x pass over
        // HARD_SWING_KNEE, up to HARD_SWING_CAP. One hand maps (ratio 0) aren't changed.
        public const double HARD_SWING_KNEE = 2.0;
        public const double HARD_SWING_SLOPE = 0.02;
        public const double HARD_SWING_CAP = 0.12;

        // The hard swing correction's multiplier of the acc rating. hardSwingRatio: Ratings.HardSwingRatio (0: none)
        public static double HardSwingMultiplier(double passRating, double hardSwingRatio) =>
            hardSwingRatio > 0
                ? 1 + Math.Min(HARD_SWING_SLOPE * Math.Max(0, (hardSwingRatio - 1) * passRating - HARD_SWING_KNEE), HARD_SWING_CAP)
                : 1;

        public double GetRating(double? predictedAcc, double? passRating, double? techRating, double hitDistance = 0, double hardSwingRatio = 0)
        {
            double difficulty_to_acc;
            if (predictedAcc > 0)
            {
                double effective = EffectiveAcc(predictedAcc ?? 0, passRating ?? 0, hitDistance);
                difficulty_to_acc = ACC_SCALE * Math.Pow(Curve(predictedAcc ?? 0, effective + ACC_OFFSET), -ACC_EXPONENT)
                    * HardSwingMultiplier(passRating ?? 0, hardSwingRatio);
            }
            else
            {
                double tiny_tech = 0.0208f * (techRating ?? 0) + 1.1284f;
                difficulty_to_acc = (-Math.Pow(tiny_tech, -(passRating ?? 0)) + 1) * 8 + 2 + 0.01f * (techRating ?? 0) * (passRating ?? 0);
            }
            if (double.IsInfinity(difficulty_to_acc) || double.IsNaN(difficulty_to_acc) || double.IsNegativeInfinity(difficulty_to_acc))
            {
                difficulty_to_acc = 0;
            }
            return difficulty_to_acc;
        }

        // The map's own per-map acc curve (picked by its predicted acc, see Curve.GetCurve) at acc
        public static float Curve(double predictedAcc, double acc) => (float)AccAi.Curve.PerMapValue(predictedAcc, acc);
    }
}
