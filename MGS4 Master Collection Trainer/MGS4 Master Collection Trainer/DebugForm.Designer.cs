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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DebugForm));
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
            this.layout.SuspendLayout();
            this.tabs.SuspendLayout();
            this.footer.SuspendLayout();
            this.SuspendLayout();
            // 
            // layout
            // 
            this.layout.ColumnCount = 1;
            this.layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layout.Controls.Add(this.instructions, 0, 0);
            this.layout.Controls.Add(this.tabs, 0, 1);
            this.layout.Controls.Add(this.footer, 0, 2);
            this.layout.Controls.Add(this.operationResult, 0, 3);
            this.layout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layout.Location = new System.Drawing.Point(0, 0);
            this.layout.Name = "layout";
            this.layout.Padding = new System.Windows.Forms.Padding(10);
            this.layout.RowCount = 4;
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 72F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 46F));
            this.layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 70F));
            this.layout.Size = new System.Drawing.Size(1180, 820);
            this.layout.TabIndex = 0;
            // 
            // instructions
            // 
            this.instructions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.instructions.Location = new System.Drawing.Point(13, 10);
            this.instructions.Name = "instructions";
            this.instructions.Size = new System.Drawing.Size(1154, 72);
            this.instructions.TabIndex = 0;
            this.instructions.Text = resources.GetString("instructions.Text");
            this.instructions.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // tabs
            // 
            this.tabs.Controls.Add(this.effectsTab);
            this.tabs.Controls.Add(this.valuesTab);
            this.tabs.Controls.Add(this.memoryTab);
            this.tabs.Controls.Add(this.signaturesTab);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Location = new System.Drawing.Point(13, 85);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(1154, 606);
            this.tabs.TabIndex = 1;
            // 
            // effectsTab
            // 
            this.effectsTab.Location = new System.Drawing.Point(4, 24);
            this.effectsTab.Name = "effectsTab";
            this.effectsTab.Size = new System.Drawing.Size(1146, 578);
            this.effectsTab.TabIndex = 0;
            this.effectsTab.Text = "Gameplay effects";
            this.effectsTab.UseVisualStyleBackColor = true;
            // 
            // valuesTab
            // 
            this.valuesTab.Location = new System.Drawing.Point(4, 24);
            this.valuesTab.Name = "valuesTab";
            this.valuesTab.Size = new System.Drawing.Size(166, 0);
            this.valuesTab.TabIndex = 1;
            this.valuesTab.Text = "Values";
            this.valuesTab.UseVisualStyleBackColor = true;
            // 
            // memoryTab
            // 
            this.memoryTab.Location = new System.Drawing.Point(4, 24);
            this.memoryTab.Name = "memoryTab";
            this.memoryTab.Size = new System.Drawing.Size(166, 0);
            this.memoryTab.TabIndex = 2;
            this.memoryTab.Text = "Memory comparison";
            this.memoryTab.UseVisualStyleBackColor = true;
            // 
            // signaturesTab
            // 
            this.signaturesTab.Location = new System.Drawing.Point(4, 24);
            this.signaturesTab.Name = "signaturesTab";
            this.signaturesTab.Size = new System.Drawing.Size(166, 0);
            this.signaturesTab.TabIndex = 3;
            this.signaturesTab.Text = "AOB scans";
            this.signaturesTab.UseVisualStyleBackColor = true;
            // 
            // footer
            // 
            this.footer.Controls.Add(this.disableAllButton);
            this.footer.Controls.Add(this.refreshStatusButton);
            this.footer.Controls.Add(this.prepareSessionButton);
            this.footer.Controls.Add(this.repeatFreezes);
            this.footer.Controls.Add(this.freezeCount);
            this.footer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.footer.Location = new System.Drawing.Point(13, 697);
            this.footer.Name = "footer";
            this.footer.Padding = new System.Windows.Forms.Padding(0, 5, 0, 0);
            this.footer.Size = new System.Drawing.Size(1154, 40);
            this.footer.TabIndex = 2;
            this.footer.WrapContents = false;
            // 
            // disableAllButton
            // 
            this.disableAllButton.AutoSize = true;
            this.disableAllButton.Location = new System.Drawing.Point(3, 8);
            this.disableAllButton.Name = "disableAllButton";
            this.disableAllButton.Size = new System.Drawing.Size(145, 30);
            this.disableAllButton.TabIndex = 0;
            this.disableAllButton.Text = "Disable all / clear freezes";
            this.disableAllButton.Click += new System.EventHandler(this.DisableAll_Click);
            // 
            // refreshStatusButton
            // 
            this.refreshStatusButton.AutoSize = true;
            this.refreshStatusButton.Location = new System.Drawing.Point(154, 8);
            this.refreshStatusButton.Name = "refreshStatusButton";
            this.refreshStatusButton.Size = new System.Drawing.Size(123, 30);
            this.refreshStatusButton.TabIndex = 1;
            this.refreshStatusButton.Text = "Refresh effect status";
            this.refreshStatusButton.Click += new System.EventHandler(this.RefreshStatus_Click);
            // 
            // prepareSessionButton
            // 
            this.prepareSessionButton.AutoSize = true;
            this.prepareSessionButton.Location = new System.Drawing.Point(283, 8);
            this.prepareSessionButton.Name = "prepareSessionButton";
            this.prepareSessionButton.Size = new System.Drawing.Size(140, 30);
            this.prepareSessionButton.TabIndex = 2;
            this.prepareSessionButton.Text = "Prepare captures / retry";
            this.prepareSessionButton.Click += new System.EventHandler(this.PrepareSession_Click);
            // 
            // repeatFreezes
            // 
            this.repeatFreezes.AutoSize = true;
            this.repeatFreezes.Location = new System.Drawing.Point(442, 13);
            this.repeatFreezes.Margin = new System.Windows.Forms.Padding(16, 8, 3, 3);
            this.repeatFreezes.Name = "repeatFreezes";
            this.repeatFreezes.Size = new System.Drawing.Size(180, 19);
            this.repeatFreezes.TabIndex = 3;
            this.repeatFreezes.Text = "Repeat frozen writes (250 ms)";
            this.repeatFreezes.CheckedChanged += new System.EventHandler(this.RepeatFreezes_Changed);
            // 
            // freezeCount
            // 
            this.freezeCount.AutoSize = true;
            this.freezeCount.Location = new System.Drawing.Point(641, 13);
            this.freezeCount.Margin = new System.Windows.Forms.Padding(16, 8, 3, 3);
            this.freezeCount.Name = "freezeCount";
            this.freezeCount.Size = new System.Drawing.Size(90, 15);
            this.freezeCount.TabIndex = 4;
            this.freezeCount.Text = "Frozen values: 0";
            // 
            // operationResult
            // 
            this.operationResult.Dock = System.Windows.Forms.DockStyle.Fill;
            this.operationResult.Location = new System.Drawing.Point(13, 743);
            this.operationResult.Multiline = true;
            this.operationResult.Name = "operationResult";
            this.operationResult.ReadOnly = true;
            this.operationResult.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.operationResult.Size = new System.Drawing.Size(1154, 64);
            this.operationResult.TabIndex = 3;
            this.operationResult.Text = "Capture hooks will be prepared when the debugger opens.";
            // 
            // statusTimer
            // 
            this.statusTimer.Interval = 1000;
            this.statusTimer.Tick += new System.EventHandler(this.StatusTimer_Tick);
            // 
            // freezeTimer
            // 
            this.freezeTimer.Interval = 250;
            this.freezeTimer.Tick += new System.EventHandler(this.FreezeTimer_Tick);
            // 
            // DebugForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1180, 820);
            this.Controls.Add(this.layout);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.MinimumSize = new System.Drawing.Size(1020, 680);
            this.Name = "DebugForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "MGS4 Trainer - Debugger - Testing Purposes Only Effects in Here May or May not Wo" +
    "rk";
            this.layout.ResumeLayout(false);
            this.layout.PerformLayout();
            this.tabs.ResumeLayout(false);
            this.footer.ResumeLayout(false);
            this.footer.PerformLayout();
            this.ResumeLayout(false);

        }
    }
}
