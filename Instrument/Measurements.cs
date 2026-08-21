using System.Collections.Generic;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// One measurement the instrument can make. Most take just a source, but a
    /// few carry extra arguments: VRMS wants an interval and a type
    /// (":MEASure:VRMS DISPlay,AC,CHANnel1"), and the averaging ones want an
    /// interval. Sending the wrong argument count is a command error, so the
    /// shape is recorded here rather than assumed at the call site.
    /// </summary>
    public sealed class Measurement
    {
        public string Text;         // shown in the UI
        public string Keyword;      // SCPI keyword
        public string Unit;         // for formatting a queried value
        public bool NeedsInterval;  // DISPlay or CYCLe
        public bool NeedsType;      // AC or DC

        public Measurement(string text, string keyword, string unit,
                           bool needsInterval = false, bool needsType = false)
        {
            Text = text;
            Keyword = keyword;
            Unit = unit;
            NeedsInterval = needsInterval;
            NeedsType = needsType;
        }

        public override string ToString() => Text;

        private string Arguments(string source, string interval, string type)
        {
            var parts = new List<string>();
            if (NeedsInterval) parts.Add(interval);
            if (NeedsType) parts.Add(type);
            parts.Add(source);
            return string.Join(",", parts);
        }

        /// <summary>Adds this measurement to the instrument's on-screen list.</summary>
        public string AddCommand(string source, string interval = "DISPlay", string type = "AC")
        {
            return ":MEASure:" + Keyword + " " + Arguments(source, interval, type);
        }

        /// <summary>Reads the value back without changing what is on screen.</summary>
        public string QueryCommand(string source, string interval = "DISPlay", string type = "AC")
        {
            return ":MEASure:" + Keyword + "? " + Arguments(source, interval, type);
        }
    }

    /// <summary>
    /// A measurement currently showing on the instrument. Tracked here because
    /// the command set can add one and clear them all, but cannot delete a
    /// single one, so removing means clearing and re-adding the survivors.
    /// </summary>
    public sealed class ActiveMeasurement
    {
        public Measurement Kind;
        public string Source;
        public string Interval;
        public string Type;

        public ActiveMeasurement(Measurement kind, string source, string interval, string type)
        {
            Kind = kind;
            Source = source;
            Interval = interval;
            Type = type;
        }

        public string Key => Kind.Keyword + "|" + Source;

        public string AddCommand() => Kind.AddCommand(Source, Interval, Type);
    }

    public static class Measurements
    {
        /// <summary>
        /// Placeholder for an unused readout slot. Selecting it sends nothing:
        /// every slot costs a query per poll, so an empty one should cost none.
        /// </summary>
        public static readonly Measurement None = new Measurement("— none —", "", "");

        public static bool IsNone(Measurement measurement)
        {
            return measurement == null || string.IsNullOrEmpty(measurement.Keyword);
        }

        public static readonly Measurement[] Voltage =
        {
            new Measurement("Vpp", "VPP", "V"),
            new Measurement("Vmax", "VMAX", "V"),
            new Measurement("Vmin", "VMIN", "V"),
            new Measurement("Vamp", "VAMPlitude", "V"),
            new Measurement("Vtop", "VTOP", "V"),
            new Measurement("Vbase", "VBASe", "V"),
            new Measurement("Vavg", "VAVerage", "V", needsInterval: true),
            new Measurement("Vrms", "VRMS", "V", needsInterval: true, needsType: true),
            new Measurement("Over", "OVERshoot", "%"),
            new Measurement("Pre", "PREShoot", "%")
        };

        public static readonly Measurement[] Time =
        {
            new Measurement("Frequency", "FREQuency", "Hz"),
            new Measurement("Period", "PERiod", "s"),
            new Measurement("Rise time", "RISetime", "s"),
            new Measurement("Fall time", "FALLtime", "s"),
            new Measurement("+ Width", "PWIDth", "s"),
            new Measurement("- Width", "NWIDth", "s"),
            new Measurement("Duty", "DUTYcycle", "%"),
            new Measurement("X at max", "XMAX", "s"),
            new Measurement("X at min", "XMIN", "s")
        };

        public static readonly Measurement[] Counting =
        {
            new Measurement("Counter", "COUNter", "Hz"),
            new Measurement("+ Pulses", "PPULses", ""),
            new Measurement("- Pulses", "NPULses", ""),
            new Measurement("Rise edges", "PEDGes", ""),
            new Measurement("Fall edges", "NEDGes", "")
        };

        /// <summary>Everything, in the order the tabs present it.</summary>
        public static IEnumerable<Measurement> All
        {
            get
            {
                foreach (var m in Voltage) yield return m;
                foreach (var m in Time) yield return m;
                foreach (var m in Counting) yield return m;
            }
        }

        public static Measurement Find(string keyword)
        {
            foreach (var m in All)
                if (m.Keyword == keyword) return m;
            return null;
        }
    }
}
