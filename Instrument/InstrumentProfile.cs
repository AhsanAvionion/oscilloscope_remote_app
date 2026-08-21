using System;
using System.Collections.Generic;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// What a given instrument family accepts. The InfiniiVision families share
    /// most of their command set, but the smaller ones lack some math operators
    /// and measurements, and asking for one they do not have produces silence
    /// rather than an answer.
    ///
    /// This table is best-effort and deliberately conservative. Anything the
    /// instrument rejects is reported in the console as "-113 Undefined header",
    /// which is the signal to correct the entry here.
    /// </summary>
    public sealed class InstrumentProfile
    {
        public string Name;
        public string Scpi;

        /// <summary>Math operators to offer. Empty means offer everything.</summary>
        public string[] MathOperations = new string[0];

        /// <summary>Measurement keywords the family is not known to support.</summary>
        public string[] UnsupportedMeasurements = new string[0];

        /// <summary>
        /// Left true for every family. The instrument is asked directly and the
        /// answer remembered for the session, which beats a table I cannot
        /// verify against every firmware.
        /// </summary>
        public bool SupportsInkSaver = true;

        public override string ToString() => Name;

        public bool SupportsMeasurement(string keyword)
        {
            return Array.IndexOf(UnsupportedMeasurements, keyword) < 0;
        }

        public bool SupportsMathOperation(string scpi)
        {
            if (MathOperations.Length == 0) return true;
            return Array.IndexOf(MathOperations, scpi) >= 0;
        }

        // ---------------------------------------------------------- the profiles

        /// <summary>Everything this app knows how to send. Nothing filtered.</summary>
        public static readonly InstrumentProfile Generic = new InstrumentProfile
        {
            Name = "All commands (no filtering)",
            Scpi = "GENERIC"
        };

        /// <summary>
        /// InfiniiVision 2000 X-Series. Fewer math operators, and the edge and
        /// pulse counting measurements belong to the larger families.
        /// </summary>
        public static readonly InstrumentProfile Series2000 = new InstrumentProfile
        {
            Name = "InfiniiVision 2000 X-Series",
            Scpi = "2000X",
            MathOperations = new[]
            {
                "ADD", "SUBTract", "MULTiply", "DIVide",
                "FFT", "INTegrate", "DIFFerentiate", "SQRt"
            },
            UnsupportedMeasurements = new[]
            {
                "XMAX", "XMIN", "PPULses", "NPULses", "PEDGes", "NEDGes"
            }
        };

        /// <summary>InfiniiVision 3000/4000 X-Series. The full set.</summary>
        public static readonly InstrumentProfile Series3000 = new InstrumentProfile
        {
            Name = "InfiniiVision 3000/4000 X-Series",
            Scpi = "3000X"
        };

        public static readonly InstrumentProfile[] All = { Generic, Series2000, Series3000 };

        /// <summary>
        /// Works out the family from the *IDN? reply, e.g.
        /// "AGILENT TECHNOLOGIES,MSO-X 2024A,MY63400145,02.65…" -> 2000 X-Series.
        /// </summary>
        public static InstrumentProfile FromIdentity(string identity)
        {
            string text = (identity ?? string.Empty).ToUpperInvariant();

            // Model sits in the second comma-separated field, like "MSO-X 2024A".
            string[] fields = text.Split(',');
            string model = fields.Length > 1 ? fields[1].Trim() : text;

            var digits = new List<char>();
            foreach (char c in model)
            {
                if (char.IsDigit(c)) digits.Add(c);
                else if (digits.Count > 0) break;
            }

            if (digits.Count >= 4)
            {
                string number = new string(digits.ToArray());
                if (number.StartsWith("2")) return Series2000;
                if (number.StartsWith("3") || number.StartsWith("4")) return Series3000;
                if (number.StartsWith("1")) return Series2000;   // 1000 X is smaller still
            }
            return Generic;
        }

        public static InstrumentProfile ByScpi(string scpi)
        {
            foreach (var profile in All)
                if (string.Equals(profile.Scpi, scpi, StringComparison.OrdinalIgnoreCase))
                    return profile;
            return Generic;
        }
    }
}
