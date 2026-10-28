using System;
using System.Collections.Generic;
using System.Linq;

namespace Ratings.AccAi
{
    // Same as RatingAPI's Curve and Pp.PpCalculator (portaBLerebalancer2), see the comments there for where the values come from.
    internal class Curve
    {
        // The base acc curve: acc pp = curve(acc) * AccWeight * acc rating^AccRatingPower
        public static readonly (double x, double y)[] BaseTable =
        {
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.094),
                (0.955, 1.039),
                (0.95, 1.000),
                (0.94, 0.931),
                (0.93, 0.867),
                (0.92, 0.813),
                (0.91, 0.781),
                (0.9, 0.753),
                (0.875, 0.699),
                (0.85, 0.649),
                (0.825, 0.605),
                (0.8, 0.568),
                (0.75, 0.485),
                (0.7, 0.414),
                (0.65, 0.355),
                (0.6, 0.307),
                (0.0, 0.000)
        };

        // Stars are what a score at this accuracy is worth
        public const float StarAccuracy = 0.96f;

        // Base curve (maps without a usable predicted acc), with the low note and length nerf
        public static List<(double x, double y)> GetCurve(int noteCount, double noteSpan)
        {
            double nerf = CurveSettings.Current.LowNoteMultiplier(noteCount, noteSpan);
            return BaseTable.Select(p => (p.x, p.y * nerf)).ToList();
        }

        // Per-map acc curve, from 100% down: ModifiableCurve with its parameters read from CurveSettings.Table by the map's
        // predicted acc, relative to the default ModifiableCurve (value = base table(x) * modified(x) / default(x)), the
        // slider tilt, and the low note and length nerf on the whole curve. Never above the base curve.
        // sliderPercentage: sliders and curved sliders per swing; noteCount: color notes; noteSpan: first to last color note
        // in real time, seconds (0 = unknown for all three).
        public static List<(double x, double y)> GetCurve(double predictedAcc, double sliderPercentage, int noteCount, double noteSpan)
        {
            var curves = PerMapCurves(predictedAcc);
            double tilt = SliderTilt(sliderPercentage, predictedAcc);
            var settings = CurveSettings.Current;
            double nerf = settings.LowNoteMultiplier(noteCount, noteSpan);
            if (curves == null && tilt == 0)
            {
                return GetCurve(noteCount, noteSpan);
            }
            double a = curves?.modified.A ?? 1.0;

            // Base table points, 0.5% steps from 60% to 95%, 0.05% steps from 95% to 100%, and A
            var xs = BaseTable.Select(p => p.x)
                .Concat(Enumerable.Range(0, 71).Select(i => 0.6 + i * 0.005))
                .Concat(Enumerable.Range(0, 101).Select(i => 0.95 + i * 0.0005))
                .Append(a)
                .Select(x => Math.Round(x, 6))
                .Where(x => x >= 0 && x <= 1)
                .Distinct()
                .OrderByDescending(x => x);

            double Raw(double x)
            {
                double value = curves == null ? BaseValue(x) : PerMapValue(curves.Value, x);
                if (tilt != 0)
                {
                    // Slider tilt: steeper around the predicted acc (unchanged there), up to SliderTiltRange away from it
                    double distance = Math.Clamp(x - predictedAcc, -settings.SliderTiltRange, settings.SliderTiltRange);
                    value *= Math.Exp(tilt * distance * 100);
                }
                return value;
            }
            return xs.Select(x => (x, Raw(x) * nerf)).ToList();
        }

        // Sliders and curved sliders per swing of a rated map
        public static double SliderPercentage(beatleader_analyzer.BeatmapScanner.Data.Ratings ratings) =>
            ratings.SwingData.Count > 0 ? (ratings.Statistics.Sliders + ratings.Statistics.CurvedSliders) / (double)ratings.SwingData.Count : 0;

        // Slider tilt per 1% acc of a map's curve (0: none)
        private static double SliderTilt(double sliderPercentage, double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || settings.SliderTilt == 0 || predictedAcc <= 0.6 || predictedAcc >= 0.9995) return 0;
            return settings.SliderTilt * sliderPercentage;
        }

        // Value of the per-map curve at acc, computed directly instead of from the sampled points (used by the acc rating)
        public static double PerMapValue(double predictedAcc, double acc)
        {
            var curves = PerMapCurves(predictedAcc);
            return curves == null ? BaseValue(acc) : PerMapValue(curves.Value, acc);
        }

        // The modified and default ModifiableCurve of a map, null when it gets the base curve
        private static (ModifiableCurve modified, ModifiableCurve standard)? PerMapCurves(double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || predictedAcc <= 0.6 || predictedAcc >= 0.9995)
            {
                return null;
            }
            var row = settings.ParamsAt(predictedAcc);
            if (row.IsDefault)
            {
                return null;
            }
            double a = Math.Min(Math.Max(row.A, 0.951), 1.0);
            return (new ModifiableCurve { A = a, C = row.C, K = row.K, W = row.W, K2 = row.K2, P2 = row.P2 }, new ModifiableCurve());
        }

        private static double PerMapValue((ModifiableCurve modified, ModifiableCurve standard) curves, double x)
        {
            double baseValue = SmoothBaseValue(x);
            double reference = curves.standard.GetValue(x);
            double value = reference > 0 ? baseValue * curves.modified.GetValue(x) / reference : baseValue;
            return Math.Min(baseValue, value);
        }

        public static double BaseValue(double acc)
        {
            int i = 0;
            for (; i < BaseTable.Length; i++)
            {
                if (BaseTable[i].x <= acc) break;
            }
            if (i == 0) i = 1;
            if (i >= BaseTable.Length) i = BaseTable.Length - 1;
            double middle_dis = (acc - BaseTable[i - 1].x) / (BaseTable[i].x - BaseTable[i - 1].x);
            return BaseTable[i - 1].y + middle_dis * (BaseTable[i].y - BaseTable[i - 1].y);
        }

        // The base table with monotone cubic interpolation (Fritsch-Carlson) instead of linear. Per-map curves only.
        public static double SmoothBaseValue(double acc)
        {
            var (xs, ys, slopes) = Smooth.Value;
            if (acc <= xs[0]) return ys[0];
            if (acc >= xs[xs.Length - 1]) return ys[ys.Length - 1];
            int i = 0;
            while (xs[i + 1] < acc) i++;
            double h = xs[i + 1] - xs[i];
            double t = (acc - xs[i]) / h;
            double t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * slopes[i]
                + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * slopes[i + 1];
        }

        // Table points ascending and the slope at each (weighted harmonic mean of the neighbouring segments' slopes)
        private static readonly Lazy<(double[] xs, double[] ys, double[] slopes)> Smooth = new(() =>
        {
            var points = BaseTable.OrderBy(p => p.x).ToArray();
            int n = points.Length;
            double[] xs = points.Select(p => p.x).ToArray(), ys = points.Select(p => p.y).ToArray();
            double[] h = new double[n - 1], d = new double[n - 1], slopes = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = xs[i + 1] - xs[i];
                d[i] = (ys[i + 1] - ys[i]) / h[i];
            }
            slopes[0] = d[0];
            slopes[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0) continue;
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                slopes[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
            return (xs, ys, slopes);
        });

        // Value of a map's acc curve at acc (points from 100% down). Null or fewer than 2 points: the base curve.
        public static double CurveValue(double acc, IReadOnlyList<(double x, double y)>? curve)
        {
            if (curve == null || curve.Count < 2)
            {
                return BaseValue(acc);
            }

            int i = 0;
            for (; i < curve.Count; i++)
            {
                if (curve[i].x <= acc) break;
            }
            if (i == 0) i = 1;
            if (i >= curve.Count) i = curve.Count - 1;

            double middle_dis = (acc - curve[i - 1].x) / (curve[i].x - curve[i - 1].x);
            return curve[i - 1].y + middle_dis * (curve[i].y - curve[i - 1].y);
        }

        // Stars: what a score at accuracy (StarAccuracy for the real stars) without modifiers is worth, with the map's curve
        // (null: the base curve) and the low star bonus, on the star scale
        public static double ToStars(double accuracy, double accRating, double passRating, double techRating, IReadOnlyList<(double x, double y)>? curve = null)
        {
            var formula = PpFormula.Current;
            float acc = (float)accuracy;
            float curveValue = (float)CurveValue(acc, curve);
            // The acc top compression, then the acc curve power
            if (formula.AccTopSlope != 1 && acc > formula.AccTopKnee)
                curveValue = formula.AccTop(curveValue, (float)CurveValue(formula.AccTopKnee, curve));
            if (formula.AccCurvePower != 1) curveValue = formula.AccCurve(curveValue, (float)CurveValue(StarAccuracy, curve), formula.AccCurvePower);

            float pp = formula.PassPp((float)passRating) + formula.AccPp(curveValue, (float)accRating) + formula.TechPp(acc, (float)techRating);
            return formula.StarsFromPp(pp);
        }

        // From the AccCurvePerMap branch. Default curve parameters from Desmos https://www.desmos.com/calculator/hlhqzmtzon
        public class ModifiableCurve
        {
            public double Y0 { get; set; } = 0.256;
            public double K { get; set; } = 4.86;
            public double W { get; set; } = 0.6;
            public double K2 { get; set; } = 2.88;
            public double P2 { get; set; } = 2.46;
            // Taper of the extension above A: 0 = keeps the slope at A (straight line), 1 = the slope fades out (flattens)
            public double C { get; set; } = 1.0;
            // Where the extension starts
            public double A { get; set; } = 1.0;

            public double GetValue(double x)
            {
                if (x <= 0.60) return (Y0 / 0.60) * x;
                if (x <= 0.95) return GetF1(x);
                if (x <= A) return GetF2(x);
                return GetL(x);
            }

            // Slope at x = 0.95
            public double GetM()
            {
                double term1 = (1.0 - Y0) / 0.35;
                double term2 = (1.0 - W) + W * (K * Math.Exp(K)) / (Math.Exp(K) - 1.0);
                return term1 * term2;
            }

            // 0.60 < x <= 0.95
            public double GetF1(double x)
            {
                double u = (x - 0.6) / 0.35;
                double blend = (1.0 - W) * u + W * (Math.Exp(K * u) - 1.0) / (Math.Exp(K) - 1.0);
                return Y0 + (1.0 - Y0) * blend;
            }

            // 0.95 < x <= A
            public double GetF2(double x)
            {
                double m = GetM();
                double u2 = (x - 0.95) / 0.05;
                double expTerm = (Math.Exp(K2 * Math.Pow(u2, P2)) - 1.0) / (Math.Exp(K2) - 1.0);
                return 1.0 + m * (x - 0.95) + (6.424 - 0.05 * m) * expTerm;
            }

            // High-pass extension above A: starts with the slope S at A, which decays towards S * (1 - C)
            public double GetL(double x)
            {
                double s = GetS();
                double s1 = s * (1.0 - C);
                double f2A = GetF2(A);

                if (Math.Abs(A - 1.0) < 1e-9)
                {
                    return f2A + s1 * (x - A);
                }

                double expTerm = 1.0 - Math.Exp(-(x - A) / (1.0 - A));
                return f2A + s1 * (x - A) + (s - s1) * (1.0 - A) * expTerm;
            }

            // Slope at x = A
            public double GetS()
            {
                double m = GetM();
                double uA = (A - 0.95) / 0.05;
                double uAPower = uA > 0 ? Math.Pow(uA, P2) : 0;
                double uAPowerMinus1 = uA > 0 ? Math.Pow(uA, P2 - 1.0) : 0;
                double expTerm = Math.Exp(K2 * uAPower) / (Math.Exp(K2) - 1.0);
                double derivativeFactor = (K2 * P2 / 0.05) * uAPowerMinus1;
                return m + (6.424 - 0.05 * m) * expTerm * derivativeFactor;
            }
        }
    }

    // One row of CurveSettings.Table: ModifiableCurve parameters for a predicted acc
    internal class CurveRow
    {
        public double PredictedAcc { get; set; }
        public double A { get; set; } = 1.0;
        public double C { get; set; } = 0;
        public double K { get; set; } = 4.86;
        public double W { get; set; } = 0.6;
        public double K2 { get; set; } = 2.88;
        public double P2 { get; set; } = 2.46;

        // A at 100% and the default shape: the base curve
        public bool IsDefault => A >= 0.9995 && K == 4.86 && W == 0.6 && K2 == 2.88 && P2 == 2.46;
    }

    // Settings of the per-map curves: ModifiableCurve parameters by predicted acc, linearly interpolated between the rows;
    // the first / last row applies below / above them.
    internal class CurveSettings
    {
        public bool Enabled { get; set; } = true;

        public List<CurveRow> Table { get; set; } = new()
        {
            new CurveRow { PredictedAcc = 0.955, A = 1.0,     C = -5.30, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.960, A = 0.99,    C = -3.00, K = 4.30, W = 0.66, K2 = 4.80, P2 = 1.85 },
            new CurveRow { PredictedAcc = 0.965, A = 0.988,   C = -2.40, K = 4.50, W = 0.64, K2 = 4.50, P2 = 1.95 },
            new CurveRow { PredictedAcc = 0.970, A = 0.986,   C = -2.20, K = 4.70, W = 0.62, K2 = 4.20, P2 = 2.05 },
            new CurveRow { PredictedAcc = 0.975, A = 0.985,   C = -1.80, K = 4.86, W = 0.60, K2 = 3.80, P2 = 2.15 },
            new CurveRow { PredictedAcc = 0.980, A = 0.985,   C = -1.40, K = 4.86, W = 0.60, K2 = 3.35, P2 = 2.30 },
            new CurveRow { PredictedAcc = 0.985, A = 0.985,   C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.990, A = 0.9915,  C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.995, A = 0.9915,  C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
        };

        // C never goes above 1 (the curve would go down)
        public double MaxC { get; set; } = 1;

        // Slider tilt: the curve is multiplied by e^(SliderTilt x slider share x (acc - predicted acc) in %), up to
        // SliderTiltRange from the predicted acc. 0 = off.
        public double SliderTilt { get; set; } = 0.2;
        public double SliderTiltRange { get; set; } = 0.015;

        // Low note and length nerf: the whole curve is multiplied by LowNoteMultiplier = LowNoteFactor x ShortFactor, each 1
        // at most. LowNoteStart / ShortStart 0 = off.
        public double LowNoteStart { get; set; } = 115;
        public double LowNoteRefNotes { get; set; } = 20;
        public double LowNoteRefMultiplier { get; set; } = 0.95;
        public double LowNoteMinMultiplier { get; set; } = 0.95;
        public double ShortStart { get; set; } = 75;
        public double ShortRefSpan { get; set; } = 12;
        public double ShortRefMultiplier { get; set; } = 0.9;
        public double ShortMinMultiplier { get; set; } = 0.9;

        // The whole curve's multiplier (notes: color notes; span: the first to the last of them in real time, in seconds;
        // 0 = unknown)
        public double LowNoteMultiplier(int notes, double span = 0) => LowNoteFactor(notes) * ShortFactor(span);

        public double LowNoteFactor(int notes)
        {
            if (LowNoteStart <= 0 || notes <= 0 || notes >= LowNoteStart || LowNoteRefNotes >= LowNoteStart) return 1;
            double nerf = (1 - LowNoteRefMultiplier) * Math.Log(LowNoteStart / notes) / Math.Log(LowNoteStart / LowNoteRefNotes);
            return Math.Clamp(1 - nerf, LowNoteMinMultiplier, 1);
        }

        public double ShortFactor(double span)
        {
            if (ShortStart <= 0 || span <= 0 || span >= ShortStart || ShortRefSpan >= ShortStart) return 1;
            double nerf = (1 - ShortRefMultiplier) * Math.Log(ShortStart / span) / Math.Log(ShortStart / ShortRefSpan);
            return Math.Clamp(1 - nerf, ShortMinMultiplier, 1);
        }

        public CurveRow ParamsAt(double predictedAcc)
        {
            var rows = Table.OrderBy(r => r.PredictedAcc).ToList();
            if (rows.Count == 0) return new CurveRow { PredictedAcc = predictedAcc };
            CurveRow Clamp(CurveRow r) => new CurveRow { PredictedAcc = predictedAcc, A = r.A, C = Math.Min(r.C, MaxC), K = r.K, W = r.W, K2 = r.K2, P2 = r.P2 };
            if (predictedAcc <= rows[0].PredictedAcc) return Clamp(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                if (predictedAcc <= rows[i].PredictedAcc)
                {
                    var p = rows[i - 1]; var n = rows[i];
                    double t = (predictedAcc - p.PredictedAcc) / (n.PredictedAcc - p.PredictedAcc);
                    double L(double x, double y) => x + t * (y - x);
                    return Clamp(new CurveRow { A = L(p.A, n.A), C = L(p.C, n.C), K = L(p.K, n.K), W = L(p.W, n.W), K2 = L(p.K2, n.K2), P2 = L(p.P2, n.P2) });
                }
            }
            return Clamp(rows[rows.Count - 1]);
        }

        public static CurveSettings Current { get; set; } = new();
    }
}
