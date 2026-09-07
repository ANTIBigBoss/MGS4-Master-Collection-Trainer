namespace MGS4_Master_Collection_Trainer
{
    partial class DebugForm
    {
        private System.ComponentModel.IContainer components;
        private System.Windows.Forms.TableLayoutPanel layout;
        private System.Windows.Forms.Label instructions;
        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.TabPage effectsTab;
        private System.Windows.Forms.TabPage valuesTab;
        private System.Windows.Forms.TabPage memoryTab;
        private System.Windows.Forms.TabPage signaturesTab;
        private System.Windows.Forms.FlowLayoutPanel footer;
        private System.Windows.Forms.Button disableAllButton;
        private System.Windows.Forms.Button refreshStatusButton;
        private System.Windows.Forms.Button prepareSessionButton;
        private System.Windows.Forms.CheckBox repeatFreezes;
        private System.Windows.Forms.Label freezeCount;
        private System.Windows.Forms.TextBox operationResult;
        private System.Windows.Forms.Timer statusTimer;
        private System.Windows.Forms.Timer freezeTimer;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.layout = new System.Windows.Forms.TableLayoutPanel();
            this.instructions = new System.Windows.Forms.Label();
            this.tabs = new System.Windows.Forms.TabControl();
            this.effectsTab = new System.Windows.Forms.TabPage();
            this.valuesTab = new System.Windows.Forms.TabPage();
            this.memoryTab = new System.Windows.Forms.TabPage();
            this.signaturesTab = new System.Windows.Forms.TabPage();
            this.footer = new System.Windows.Forms.FlowLayoutPanel();
            this.disableAllButton = new System.Windows.Forms.Button();
            this.refreshStatusButton = new System.Windows.Forms.Button();
            this.prepareSessionButton = new System.Windows.Forms.Button();
            this.repeatFreezes = new System.Windows.Forms.CheckBox();
            this.freezeCount = new System.Windows.Forms.Label();
            this.operationResult = new System.Windows.Forms.TextBox();
            this.statusTimer = new System.Windows.Forms.Timer(this.components);
            this.freezeTimer = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            this.layout.SuspendLayout();
            this.tabs.SuspendLayout();
            this.footer.SuspendLayout();
            this.layout.ColumnCount = 1;
            this.layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layout.RowCount = 4;
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 72F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 46F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 70F));
            this.layout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layout.Padding = new System.Windows.Forms.Padding(10);
            this.layout.Controls.Add(this.instructions, 0, 0);
            this.layout.Controls.Add(this.tabs, 0, 1);
            this.layout.Controls.Add(this.footer, 0, 2);
            this.layout.Controls.Add(this.operationResult, 0, 3);
            this.instructions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.instructions.Text = "Capture hooks prepare automatically when this page opens. Gameplay cheats stay off until you enable them.\r\nLoad gameplay to let the hooks capture their pointers; see Capture status for readiness.\r\nDisable matching Cheat Engine scripts before using this tool. Closing this panel restores effects and clears freezes.";
            this.instructions.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Name = "debugTabs";
            this.tabs.Controls.Add(this.effectsTab);
            this.tabs.Controls.Add(this.valuesTab);
            this.tabs.Controls.Add(this.memoryTab);
            this.tabs.Controls.Add(this.signaturesTab);
            this.effectsTab.Text = "Gameplay effects";
            this.effectsTab.UseVisualStyleBackColor = true;
            this.valuesTab.Text = "Values";
            this.valuesTab.UseVisualStyleBackColor = true;
            this.memoryTab.Text = "Memory comparison";
            this.memoryTab.UseVisualStyleBackColor = true;
            this.signaturesTab.Text = "AOB scans";
            this.signaturesTab.UseVisualStyleBackColor = true;
            this.footer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.footer.WrapContents = false;
            this.footer.Padding = new System.Windows.Forms.Padding(0, 5, 0, 0);
            this.footer.Controls.Add(this.disableAllButton);
            this.footer.Controls.Add(this.refreshStatusButton);
            this.footer.Controls.Add(this.prepareSessionButton);
            this.footer.Controls.Add(this.repeatFreezes);
            this.footer.Controls.Add(this.freezeCount);
            this.disableAllButton.AutoSize = true;
            this.disableAllButton.Height = 30;
            this.disableAllButton.Name = "DisableAllEffects";
            this.disableAllButton.Text = "Disable all / clear freezes";
            this.disableAllButton.Click += new System.EventHandler(this.DisableAll_Click);
            this.refreshStatusButton.AutoSize = true;
            this.refreshStatusButton.Height = 30;
            this.refreshStatusButton.Text = "Refresh effect status";
            this.refreshStatusButton.Click += new System.EventHandler(this.RefreshStatus_Click);
            this.prepareSessionButton.AutoSize = true;
            this.prepareSessionButton.Height = 30;
            this.prepareSessionButton.Name = "PrepareDebugSession";
            this.prepareSessionButton.Text = "Prepare captures / retry";
            this.prepareSessionButton.Click += new System.EventHandler(this.PrepareSession_Click);
            this.repeatFreezes.AutoSize = true;
            this.repeatFreezes.Name = "RepeatFrozenWrites";
            this.repeatFreezes.Margin = new System.Windows.Forms.Padding(16, 8, 3, 3);
            this.repeatFreezes.Text = "Repeat frozen writes (250 ms)";
            this.repeatFreezes.CheckedChanged += new System.EventHandler(this.RepeatFreezes_Changed);
            this.freezeCount.AutoSize = true;
            this.freezeCount.Margin = new System.Windows.Forms.Padding(16, 8, 3, 3);
            this.freezeCount.Text = "Frozen values: 0";
            this.operationResult.Dock = System.Windows.Forms.DockStyle.Fill;
            this.operationResult.Multiline = true;
            this.operationResult.ReadOnly = true;
            this.operationResult.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.operationResult.Name = "OperationResult";
            this.operationResult.Text = "Capture hooks will be prepared when the debugger opens.";
            this.statusTimer.Interval = 1000;
            this.statusTimer.Tick += new System.EventHandler(this.StatusTimer_Tick);
            this.freezeTimer.Interval = 250;
            this.freezeTimer.Tick += new System.EventHandler(this.FreezeTimer_Tick);
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ClientSize = new System.Drawing.Size(1180, 820);
            this.MinimumSize = new System.Drawing.Size(1020, 680);
            this.Controls.Add(this.layout);
            this.Name = "DebugForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "MGS4 Trainer - Debugger";
            this.footer.ResumeLayout(false);
            this.footer.PerformLayout();
            this.tabs.ResumeLayout(false);
            this.layout.ResumeLayout(false);
            this.layout.PerformLayout();
            this.ResumeLayout(false);
        }
    }
}
