using System;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        internal enum MainCategory { Cheats, Stats, Filters }

        private Control[] cheatControls;
        private Control[] statsControls;
        private Control[] filterControls;
        private MainCategory? showingMainCategory;

        private void InitializeMainMenus()
        {
            cheatControls = new Control[]
            {
                InfiniteLifeCheckBox, InfiniteAmmoCheckBox, InfiniteSuppressorCheckBox,
                NeverReloadCheckBox, NoStressCheckBox, InfiniteCamoCheckBox
            };
            filterControls = new Control[]
            {
                DisablePissFilterCheckBox, DisableMotionBlurCheckBox, DisableResolutionScalingCheckBox
            };
            statsControls = new Control[]
            {
                DifficultyLabelEdit, DifficultyComboBox,
                PlayTimeLabelEdit, PlayTimeTextBoxEdit,
                AlertsLabelEdit, AlertsTextBoxEdit,
                ContinuesLabelEdit, ContinuesTextBoxEdit,
                RecoveryItemsLabelEdit, RecoveryItemsTextBoxEdit,
                CqcUsesLabel, CqcUsesTextBox,
                HeadshotsLabel, HeadshotsTextbox,
                KnifeKillsLabel, KnifeKillsTextBox,
                KnockoutsLabel, KnockoutsTextBox,
                CombatHighsLabel, CombatHighsTextBox,
                HoldUpsLabel, HoldUpsTextBox,
                BodySearchesLabel, BodySearchesTextBox,
                PraisesLabel, PraisesTextBox,
                ItemsDonatedLabel, ItemsDonatedTextBox,
                WeaponPickupsLabel, WeaponPickupsTextBox,
                ItemPickupsLabel, ItemPickupsTextBox,
                SyringeUsesLabel, SyringeUsesTextBox,
                ScanningPlugUsesLabel, ScanningPlugUsesTextBox,
                PlayboyPagesLabel, PlayboyPagesTextBox,
                EmotionPagesLabel, EmotionPagesTextBox,
                CrouchTimeLabel, CrouchTimeTextBox,
                CrawlTimeLabel, CrawlTimeTextBox,
                ProneSideRollsLabel, ProneSideRollsTextBox,
                ForwardRollsLabel, ForwardRollsTextBox,
                WallPressTimeLabel, WallPressTimeTextBox,
                BoxDrumTimeLabel, BoxDrumTimeTextBox,
                FlashbacksLabel, FlashbacksTextBox,
                ChangeAllStatsValuesButton, RefreshStatsButton, DisableEditingAndTrackLiveStats
            };

            CheatsMenuButton.Click += (sender, args) => ShowMainCategory(MainCategory.Cheats);
            StatsMenuButton.Click += (sender, args) => ShowMainCategory(MainCategory.Stats);
            FilterMenuButton.Click += (sender, args) => ShowMainCategory(MainCategory.Filters);
            BindHover(CheatsMenuButton, "Show gameplay cheats. Switching menus keeps active cheats running.");
            BindHover(StatsMenuButton, "Show run statistics and their editing and live tracking controls.");
            BindHover(FilterMenuButton, "Show screen filter, motion blur and resolution scaling controls. Area change required for changes to take effect.");
            ShowMainCategory(MainCategory.Cheats);
        }

        internal void ShowMainCategory(MainCategory category)
        {
            if (cheatControls == null || showingMainCategory == category) return;
            Control[] active;
            switch (category)
            {
                case MainCategory.Cheats: active = cheatControls; break;
                case MainCategory.Stats: active = statsControls; break;
                case MainCategory.Filters: active = filterControls; break;
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
            UpdateCategoryControls(() =>
            {
                foreach (Control control in cheatControls) control.Visible = category == MainCategory.Cheats;
                foreach (Control control in statsControls) control.Visible = category == MainCategory.Stats;
                foreach (Control control in filterControls) control.Visible = category == MainCategory.Filters;
                // Restore the native foreground order after initially hidden controls
                // create their windows, just as the inventory categories do.
                foreach (Control control in active)
                    if (!(control is PictureBox)) control.BringToFront();
                showingMainCategory = category;
            });
        }
    }
}
