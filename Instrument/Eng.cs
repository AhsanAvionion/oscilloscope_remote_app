using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScopeControl.Instrument
{
    /// <summary>Engineering notation, the way a scope front panel shows numbers.</summary>
    public static class Eng
    {
        private static readonly (int Exp, string Prefix)[] Prefixes =
        {
            (-15, "f"), (-12, "p"), (-9, "n"), (-6, "u"), (-3, "m"),
            (0, ""), (3, "k"), (6, "M"), (9, "G")
        };

        private static readonly Dictionary<char, double> Multipliers = new Dictionary<char, double>
        {
            { 'f', 1e-15 }, { 'p', 1e-12 }, { 'n', 1e-9 },
            { 'u', 1e-6 },  { 'µ', 1e-6 },  { 'U', 1e-6 },
            { 'm', 1e-3 },  { 'k', 1e3 },   { 'K', 1e3 },
            { 'M', 1e6 },   { 'G', 1e9 }
        };

        /// <summary>Formats 0.002 as "2 mV", 1.5e-6 as "1.5 us".</summary>
        public static string Format(double value, string unit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "----";
            if (Math.Abs(value) < 1e-15) return "0 " + unit;

            int exp = (int)Math.Floor(Math.Log10(Math.Abs(value)) / 3.0) * 3;
            if (exp < -15) exp = -15;
            if (exp > 9) exp = 9;

            double mantissa = value / Math.Pow(10, exp);

            // Log10 rounding can land us on 1000.0 - shift up one decade.
            if (Math.Abs(mantissa) >= 1000 && exp < 9)
            {
                exp += 3;
                mantissa = value / Math.Pow(10, exp);
            }

            return mantissa.ToString("0.####", CultureInfo.InvariantCulture) + " " + PrefixFor(exp) + unit;
        }

        private static string PrefixFor(int exp)
        {
            foreach (var p in Prefixes) if (p.Exp == exp) return p.Prefix;
            return "e" + exp;
        }

        /// <summary>Reads "500 mV", "1.2ms", "-3.5" and friends.</summary>
        public static bool TryParse(string text, string unit, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();

            // Drop the unit if it is there ("1.2 ms" -> "1.2 m", "500 mV" -> "500 m").
            if (!string.IsNullOrEmpty(unit) &&
                s.Length > unit.Length &&
                s.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - unit.Length).Trim();
            }

            double multiplier = 1.0;
            if (s.Length > 0 && Multipliers.TryGetValue(s[s.Length - 1], out double m))
            {
                // Only treat a trailing letter as a prefix when a number precedes it.
                string head = s.Substring(0, s.Length - 1).Trim();
                if (head.Length > 0 && (char.IsDigit(head[head.Length - 1]) || head[head.Length - 1] == '.'))
                {
                    multiplier = m;
                    s = head;
                }
            }

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return false;

            value = parsed * multiplier;
            return true;
        }

        /// <summary>1-2-5 sequence between two limits, the way scope knobs step.</summary>
        public static double[] Sequence125(double min, double max)
        {
            var list = new List<double>();
            for (int exp = -15; exp <= 3; exp++)
            {
                foreach (double m in new[] { 1.0, 2.0, 5.0 })
                {
                    double v = m * Math.Pow(10, exp);
                    if (v >= min * 0.9999 && v <= max * 1.0001) list.Add(v);
                }
            }
            return list.ToArray();
        }

        /// <summary>Index of the entry closest to <paramref name="value"/> on a log scale.</summary>
        public static int ClosestIndex(double[] values, double value)
        {
            int best = 0;
            double bestErr = double.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                double err = Math.Abs(Math.Log10(values[i]) - Math.Log10(Math.Abs(value) < 1e-18 ? 1e-18 : Math.Abs(value)));
                if (err < bestErr) { bestErr = err; best = i; }
            }
            return best;
        }

        /// <summary>SCPI-safe number, always invariant culture.</summary>
        public static string Scpi(double value) => value.ToString("G9", CultureInfo.InvariantCulture);

        public static double ParseScpi(string s)
        {
            return double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
