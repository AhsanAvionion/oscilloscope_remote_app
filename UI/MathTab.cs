using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// Picks what the math waveform computes. The second source only applies to
    /// the two-source operations, and the FFT settings only to the transforms,
    /// so both are greyed out when they do not apply rather than being sent and
    /// rejected.
    /// </summary>
    public sealed class MathTab : UserControl
    {
        private readonly ComboBox _operation;
        private readonly ComboBox _source1;
        private readonly ComboBox _source2;
        private readonly ComboBox _window;
        private readonly EngBox _center;
        private readonly EngBox _span;
        private readonly Label _source2Caption;
        private readonly Label _fftCaption;

        private readonly System.Collections.Generic.List<Choice> _allOperations =
            new System.Collections.Generic.List<Choice>();

        private bool _updating;

        public MathTab()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Chassis;

            var title = new Label
            {
                Text = "MATH FUNCTION",
                Location = new Point(12, 10),
                Size = new Size(200, 16),
                ForeColor = Theme.Math,
                Font = Theme.Header
            };

            Controls.Add(UiFactory.Caption("Operator", 12, 34, 66));
            _operation = UiFactory.Combo(84, 34, 180);
            UiFactory.FillCombo(_operation,
                new Choice("Add  (a + b)", "ADD"),
                new Choice("Subtract  (a - b)", "SUBTract"),
                new Choice("Multiply  (a * b)", "MULTiply"),
                new Choice("Divide  (a / b)", "DIVide"),
                new Choice("FFT magnitude", "FFT"),
                new Choice("FFT phase", "FFTPhase"),
                new Choice("Integrate", "INTegrate"),
                new Choice("Differentiate", "DIFFerentiate"),
                new Choice("Square root", "SQRt"),
                new Choice("Absolute value", "ABSolute"),
                new Choice("Square", "SQUare"),
                new Choice("Natural log", "LN"),
                new Choice("Log base 10", "LOG"),
                new Choice("Exponential", "EXP"),
                new Choice("Base 10 exponential", "TEN"),
                new Choice("Low pass filter", "LOWPass"),
                new Choice("High pass filter", "HIGHpass"),
                new Choice("Magnify", "MAGNify"));
            foreach (Choice choice in _operation.Items) _allOperations.Add(choice);

            _operation.SelectedIndexChanged += (s, e) =>
            {
                if (_operation.SelectedItem == null) return;
                string op = ((Choice)_operation.SelectedItem).Scpi;
                UpdateEnabledState(op);
                if (_updating) return;
                OperationChanged?.Invoke(op);
            };

            Controls.Add(UiFactory.Caption("Source a", 12, 60, 66));
            _source1 = UiFactory.Combo(84, 60, 180);
            FillSources(_source1);
            _source1.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _source1.SelectedItem == null) return;
                SourceChanged?.Invoke(1, ((Choice)_source1.SelectedItem).Scpi);
            };

            _source2Caption = UiFactory.Caption("Source b", 12, 86, 66);
            Controls.Add(_source2Caption);
            _source2 = UiFactory.Combo(84, 86, 180);
            FillSources(_source2);
            _source2.SelectedIndex = 1;
            _source2.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _source2.SelectedItem == null) return;
                SourceChanged?.Invoke(2, ((Choice)_source2.SelectedItem).Scpi);
            };

            _fftCaption = new Label
            {
                Text = "FFT",
                Location = new Point(300, 10),
                Size = new Size(200, 16),
                ForeColor = Theme.Accent,
                Font = Theme.Header
            };

            Controls.Add(UiFactory.Caption("Window", 300, 34, 66));
            _window = UiFactory.Combo(372, 34, 150);
            UiFactory.FillCombo(_window,
                new Choice("Hanning", "HANNing"),
                new Choice("Flat top", "FLATtop"),
                new Choice("Rectangular", "RECTangular"),
                new Choice("Blackman-Harris", "BHARris"));
            _window.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _window.SelectedItem == null) return;
                WindowChanged?.Invoke(((Choice)_window.SelectedItem).Scpi);
            };

            Controls.Add(UiFactory.Caption("Centre", 300, 60, 66));
            _center = new EngBox
            {
                Location = new Point(372, 60),
                Size = new Size(150, 22),
                Unit = "Hz",
                Step = 1e3,
                Minimum = 0,
                Maximum = 1e9
            };
            _center.ValueCommitted += (s, e) => { if (!_updating) CenterChanged?.Invoke(_center.Value); };

            Controls.Add(UiFactory.Caption("Span", 300, 86, 66));
            _span = new EngBox
            {
                Location = new Point(372, 86),
                Size = new Size(150, 22),
                Unit = "Hz",
                Step = 1e3,
                Minimum = 0,
                Maximum = 1e9
            };
            _span.ValueCommitted += (s, e) => { if (!_updating) SpanChanged?.Invoke(_span.Value); };

            var note = new Label
            {
                Text = "Vertical scale and offset for the math waveform are on the Channels tab.\r\n" +
                       "An operator your firmware does not support will show up in the console as a command error.",
                Location = new Point(560, 34),
                Size = new Size(420, 46),
                ForeColor = Theme.TextDim,
                Font = Theme.Ui
            };

            Controls.AddRange(new Control[]
            {
                title, _operation, _source1, _source2, _fftCaption, _window, _center, _span, note
            });

            UpdateEnabledState("ADD");
        }

        public event Action<string> OperationChanged;
        public event Action<int, string> SourceChanged;
        public event Action<string> WindowChanged;
        public event Action<double> CenterChanged;
        public event Action<double> SpanChanged;

        private static void FillSources(ComboBox combo)
        {
            UiFactory.FillCombo(combo,
                new Choice("CH 1", "CHANnel1"),
                new Choice("CH 2", "CHANnel2"),
                new Choice("CH 3", "CHANnel3"),
                new Choice("CH 4", "CHANnel4"));
        }

        private void UpdateEnabledState(string operation)
        {
            bool binary = KeysightScope.IsBinaryOperation(operation);
            bool fft = KeysightScope.IsFftOperation(operation);

            _source2.Enabled = binary;
            _source2Caption.ForeColor = binary ? Theme.TextDim : Theme.Border;

            _window.Enabled = fft;
            _center.Enabled = fft;
            _span.Enabled = fft;
            _fftCaption.ForeColor = fft ? Theme.Accent : Theme.Border;
        }

        /// <summary>Offers only the operators the selected model is known to have.</summary>
        public void ApplyProfile(InstrumentProfile profile)
        {
            _updating = true;
            try
            {
                var keep = new System.Collections.Generic.List<Choice>();
                foreach (Choice choice in _allOperations)
                    if (profile.SupportsMathOperation(choice.Scpi)) keep.Add(choice);

                string previous = _operation.SelectedItem == null
                    ? null : ((Choice)_operation.SelectedItem).Scpi;

                _operation.Items.Clear();
                foreach (var choice in keep) _operation.Items.Add(choice);
                if (_operation.Items.Count > 0) _operation.SelectedIndex = 0;
                if (previous != null) UiFactory.SelectScpi(_operation, previous);
            }
            finally { _updating = false; }
        }

        public void Apply(MathState state)
        {
            _updating = true;
            try
            {
                UiFactory.SelectScpi(_operation, state.Operation);
                UiFactory.SelectScpi(_source1, state.Source1);
                UiFactory.SelectScpi(_source2, state.Source2);
                UiFactory.SelectScpi(_window, state.Window);
                _center.SetValueSilently(state.Center);
                _span.SetValueSilently(state.Span);
                UpdateEnabledState(state.Operation);
            }
            finally { _updating = false; }
        }

        public void SetInteractive(bool enabled)
        {
            foreach (Control c in Controls) c.Enabled = enabled;
            if (enabled && _operation.SelectedItem != null)
                UpdateEnabledState(((Choice)_operation.SelectedItem).Scpi);
        }
    }
}
