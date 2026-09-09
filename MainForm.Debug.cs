using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        private DebugForm debugForm;
        // The debugger window prepares its own full set of captures. Its explicit Disable
        // all action must not be undone by the main window's periodic preparation.
        private volatile bool debugSessionOwnsPreparation;

        private void InitializeDebugControls()
        {
            OpenDebugFormButton.Click += (sender, args) => OpenDebugForm();
            BindHover(OpenDebugFormButton,
                "Opens the value tables, effects, and memory comparisons. Closing the tool stops repeated value freezes; enabled cheats remain active.");
        }

        private void SetDebugInteractive()
        {
            OpenDebugFormButton.Enabled = CanUpdate && !operationInProgress;
        }

        private void OpenDebugForm()
        {
            if (!CanUpdate || operationInProgress) return;
            if (debugForm != null && !debugForm.IsDisposed)
            {
                if (!debugForm.IsClosing)
                {
                    if (debugForm.WindowState == FormWindowState.Minimized)
                        debugForm.WindowState = FormWindowState.Normal;
                    debugForm.Activate();
                }
                return;
            }

            var form = new DebugForm(preserveSessionOnClose: true);
            debugForm = form;
            debugSessionOwnsPreparation = true;
            form.Disposed += (sender, args) =>
            {
                if (!ReferenceEquals(debugForm, form)) return;
                debugForm = null;
                debugSessionOwnsPreparation = false;
                nextPreparation = DateTime.MinValue;
            };
            try { form.Show(this); }
            catch (Exception ex)
            {
                form.Dispose();
                SetStatus("Could not open the debugger tool: " + ex.Message);
            }
        }

        private async Task CloseDebugFormAsync()
        {
            DebugForm form = debugForm;
            if (form != null && !form.IsDisposed) await form.CloseForOwnerAsync();
        }
    }
}
