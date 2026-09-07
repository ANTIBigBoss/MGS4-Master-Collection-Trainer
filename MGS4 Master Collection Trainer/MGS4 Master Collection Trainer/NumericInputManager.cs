using System;
using System.Globalization;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    internal sealed class NumericValueCommit
    {
        internal VitalsSnapshot Source;
        internal GameValueId Id;
        internal ulong Value;
        internal int Revision;
    }

    // NumericUpDown raises ValueChanged before replacing its edit text. Coalesce
    // those events until the message finishes, including parse-then-spin changes.
    internal sealed class NumericInputManager
    {
        internal readonly NumericUpDown Control;
        internal readonly GameValueId Id;
        internal event Action<NumericValueCommit> CommitRequested;
        internal event Action<string> InvalidInput;
        private VitalsSnapshot latest, editSource;
        private bool rendering, editing, dirty, queued, valueChanged, resumeTracking, pending;
        private string draft;
        private int revision;

        internal NumericInputManager(NumericUpDown control, GameValueId id, decimal increment, decimal maximum)
        {
            Control = control; Id = id;
            rendering = true;
            control.Minimum = 0;
            control.Maximum = maximum;
            control.Increment = increment;
            control.DecimalPlaces = 0;
            control.Hexadecimal = false;
            control.ThousandsSeparator = false;
            control.Accelerations.Clear();
            rendering = false;
            control.Enter += (sender, args) => BeginEdit();
            control.TextChanged += (sender, args) =>
            {
                if (rendering || queued || !Ready) return;
                if (!editing) BeginEdit();
                dirty = true;
                draft = control.Text;
            };
            control.ValueChanged += (sender, args) => QueueCommit(false, true);
            control.Leave += (sender, args) => QueueCommit(true, false);
            control.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter) return;
                args.Handled = true;
                args.SuppressKeyPress = true;
                QueueCommit(true, false);
            };
            ApplySnapshot(null);
        }

        internal bool Ready => latest != null && latest.IsReady && latest.Values.ContainsKey(Id);

        private void BeginEdit()
        {
            if (rendering || !Ready || editing) return;
            editing = true;
            editSource = latest;
            draft = Control.Text;
            dirty = false;
        }

        internal void ApplySnapshot(VitalsSnapshot snapshot)
        {
            bool changed = latest == null || snapshot == null || latest.GameIdentity != snapshot.GameIdentity ||
                latest.PlayerAddress != snapshot.PlayerAddress;
            latest = snapshot;
            if (changed || !Ready)
            {
                ++revision;
                editing = dirty = queued = valueChanged = resumeTracking = pending = false;
                editSource = null;
            }
            if (!editing && !pending && !queued) Render();
        }

        internal void SetInteractive(bool available) { Control.Enabled = available && Ready; }

        private void Render()
        {
            rendering = true;
            try
            {
                if (!Ready) { Control.Text = string.Empty; Control.Enabled = false; return; }
                ulong value = latest.Values[Id];
                Control.Value = value;
                Control.Text = value.ToString(CultureInfo.InvariantCulture);
            }
            finally { rendering = false; }
        }

        private void QueueCommit(bool resume, bool changed)
        {
            if (rendering || !Ready) return;
            valueChanged |= changed;
            resumeTracking |= resume;
            if (queued) return;
            queued = true;
            if (Control.IsHandleCreated && !Control.IsDisposed)
                Control.BeginInvoke(new Action(FlushPendingCommit));
        }

        private void FlushPendingCommit()
        {
            if (!queued || Control.IsDisposed) return;
            bool resume = resumeTracking;
            bool changed = valueChanged || dirty;
            queued = valueChanged = resumeTracking = false;
            if (!Ready) return;
            if (!changed)
            {
                if (resume) { editing = false; editSource = null; if (!pending) Render(); }
                return;
            }

            // Preserve the user's raw input before NumericUpDown can silently clamp
            // or round it while validating focus loss, Enter, or an arrow press.
            if ((dirty && !TryParse(draft, out _)) || !TryParse(Control.Text, out ulong value))
            {
                editing = dirty = false;
                editSource = null;
                InvalidInput?.Invoke("Enter a whole number from 0 to " + Control.Maximum.ToString(CultureInfo.InvariantCulture) + ".");
                Render();
                return;
            }
            VitalsSnapshot source = editSource ?? latest;
            dirty = false;
            editing = !resume && Control.ContainsFocus;
            if (resume) editSource = null;
            rendering = true;
            try
            {
                Control.Value = value;
                Control.Text = value.ToString(CultureInfo.InvariantCulture);
            }
            finally { rendering = false; }
            draft = Control.Text;
            pending = true;
            var request = new NumericValueCommit { Source = source, Id = Id, Value = value, Revision = ++revision };
            CommitRequested?.Invoke(request);
        }

        private bool TryParse(string text, out ulong value) =>
            ulong.TryParse((text ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value <= (ulong)Control.Maximum;

        internal bool IsCurrent(NumericValueCommit request) => Ready && request.Revision == revision &&
            request.Source.GameIdentity == latest.GameIdentity && request.Source.PlayerAddress == latest.PlayerAddress;

        internal void Complete(NumericValueCommit request)
        {
            if (!IsCurrent(request)) return;
            pending = false;
            if (!editing && !queued) Render();
        }
    }
}
