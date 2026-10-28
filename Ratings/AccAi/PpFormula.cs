using System;

namespace Ratings.AccAi
{
    // Same as RatingAPI's Pp.PpFormula (portaBLerebalancer2), the parts the stars need. See the comments there for where
    // the values come from.
    //   pass pp = PassScale * e^(PassInner * pass^(1/PassExponent)) - PassOffset   (0 when negative; above PassTopKnee
    //             each point counts PassTopSlope)
    //   acc pp  = curve(acc) * AccWeight * accRating^AccRatingPower   (the map's own curve, Curve.GetCurve)
    //   tech pp = e^(TechAccExponent * acc) * TechWeight * tech^TechRatingPower   (above TechTopKnee each point counts TechTopSlope)
    //   stars   = StarScale * (pp of a 96% score / StarDivisor)^StarExponent, low star bonus included (StarsFromPp)
    internal class PpFormula
    {
        public double PassScale { get; set; } = 12.3;
        public double PassExponent { get; set; } = 2.62;
        public double PassOffset { get; set; } = 30;
        public double PassInner { get; set; } = 0.85966;
        public double PassTopKnee { get; set; } = 13;
        public double PassTopSlope { get; set; } = 0.5;

        public double AccWeight { get; set; } = 21.584;
        public double AccRatingPower { get; set; } = 1.2163;
        // curve(acc) is replaced by curve(96%) x (curve(acc) / curve(96%))^AccCurvePower, so stars (read at 96%) don't move
        public double AccCurvePower { get; set; } = 0.9;
        // Above AccTopKnee accuracy, the curve rises AccTopSlope as fast
        public double AccTopKnee { get; set; } = 0.99;
        public double AccTopSlope { get; set; } = 0.8;

        public double TechWeight { get; set; } = 0.1612;
        public double TechRatingPower { get; set; } = 1.2477;
        public double TechAccExponent { get; set; } = 3.5;
        public double TechTopKnee { get; set; } = 12;
        public double TechTopSlope { get; set; } = 0.33;

        public double StarDivisor { get; set; } = 83.6925;
        public double StarScale { get; set; } = 1.1653;
        public double StarExponent { get; set; } = 1.17507;

        public double LowStarBonus { get; set; } = 0.0126742;
        public double LowStarBonusEnd { get; set; } = 8.6454;
        public double LowStarBonusPower { get; set; } = 1.36;

        public static PpFormula Current { get; set; } = new();

        // The curve value with the acc top compression (curveAtKnee: the curve at AccTopKnee)
        public float AccTop(float curveValue, float curveAtKnee)
        {
            if (AccTopSlope == 1 || !(curveValue > curveAtKnee)) return curveValue;
            return curveAtKnee + (curveValue - curveAtKnee) * (float)AccTopSlope;
        }

        public float PassPp(float passRating)
        {
            float pass = passRating;
            if (PassTopSlope != 1 && pass > PassTopKnee) pass = (float)(PassTopKnee + (pass - PassTopKnee) * PassTopSlope);
            float passPP = (float)PassScale * MathF.Exp((float)PassInner * MathF.Pow(pass, 1 / (float)PassExponent)) - (float)PassOffset;
            if (float.IsInfinity(passPP) || float.IsNaN(passPP) || passPP < 0) passPP = 0;
            return passPP;
        }

        // The curve value acc pp uses, from the curve at the score's accuracy and at 96%
        public float AccCurve(float curveValue, float curveAt96, double power)
        {
            if (power == 1 || !(curveValue > 0) || !(curveAt96 > 0)) return curveValue;
            return curveAt96 * MathF.Pow(curveValue / curveAt96, (float)power);
        }

        public float AccPp(float curveValue, float accRating) =>
            accRating > 0 ? curveValue * (float)AccWeight * MathF.Pow(accRating, (float)AccRatingPower) : 0;

        public float TechPp(float accuracy, float techRating)
        {
            if (!(techRating > 0)) return 0;
            float tech = techRating;
            if (tech > TechTopKnee) tech = (float)(TechTopKnee + (tech - TechTopKnee) * TechTopSlope);
            return MathF.Exp((float)TechAccExponent * accuracy) * (float)TechWeight * MathF.Pow(tech, (float)TechRatingPower);
        }

        // Stars from pp at 96% (pass + acc + tech), without the low star bonus
        public float Stars(float ppAt96)
        {
            double basis = ppAt96 / StarDivisor;
            return basis > 0 ? (float)(StarScale * Math.Pow(basis, StarExponent)) : (float)basis;
        }

        // Stars from pp at 96% before the low star bonus: the stars s with s = Stars(ppAt96 * LowStarMultiplier(s))
        public float StarsFromPp(float ppAt96)
        {
            float stars = Stars(ppAt96);
            if (!(ppAt96 > 0) || LowStarBonus <= 0 || stars >= LowStarBonusEnd) return stars;

            double lo = stars, hi = LowStarBonusEnd;
            while (hi - lo > 1e-6)
            {
                double mid = (lo + hi) / 2;
                if (mid < Stars(ppAt96 * LowStarMultiplier((float)mid))) lo = mid; else hi = mid;
            }
            return (float)((lo + hi) / 2);
        }

        public float LowStarMultiplier(float stars)
        {
            if (LowStarBonus <= 0 || float.IsNaN(stars) || stars >= LowStarBonusEnd) return 1;
            return 1 + (float)(LowStarBonus * Math.Pow(LowStarBonusEnd - Math.Max(stars, 0), LowStarBonusPower));
        }
    }
}
