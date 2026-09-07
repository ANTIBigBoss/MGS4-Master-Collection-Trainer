using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MGS4_Master_Collection_Trainer
{
    public partial class MainForm
    {
        private sealed class InventoryEditor
        {
            internal InventoryItemDefinition Item;
            internal TextBox Current;
            internal TextBox Maximum;
            internal Button Apply;
            internal bool Dirty;
            internal bool Populated;
        }

        private readonly List<InventoryEditor> inventoryEditors = new List<InventoryEditor>();
        private Control[] recoveryControls;
        private Control[] gadgetControls;
        private Button[] ownershipButtons;
        private InventorySnapshot inventorySnapshot;
        private bool renderingInventory;
        private bool inventoryInitialized;
        private bool? showingRecoveryItems;

        private void InitializeInventoryControls()
        {
            if (inventoryInitialized) return;
            inventoryInitialized = true;
            recoveryControls = new Control[]
            {
                CigsPictureBox, AddCigsButton, RemoveCigsButton,
                MunaPictureBox, AddMunaButton, RemoveMunaButton,
                SyringePictureBox, AddSyringeButton, RemoveSyringeButton,
                RationPictureBox, CurrentRationLabel, MaxRationLabel, CurrentRationTextBox, MaxRationTextBox, SetRationButton,
                NoodlesPictureBox, CurrentNoodlesLabel, MaxNoodlesLabel, CurrentNoodlesTextBox, MaxNoodlesTextBox, SetNoodlesButton,
                RegainPictureBox, CurrentRegainLabel, MaxRegainLabel, CurrentRegainTextBox, MaxRegainTextBox, SetRegainButton,
                PentazeminPictureBox, CurrentPentazeminLabel, MaxPentazeminLabel, CurrentPentazeminTextBox, MaxPentazeminTextBox, SetPentazeminButton,
                CompressPictureBox, CurrentCompressLabel, MaxCompressLabel, CurrentCompressTextBox, MaxCompressTextBox, SetCompressButton
            };
            gadgetControls = new Control[]
            {
                BandanaPictureBox, AddBandanaButton, RemoveBandanaButton,
                StealthPictureBox, AddStealthButton, RemoveStealthButton,
                CameraPictureBox, AddCameraButton, RemoveCameraButton,
                RadioPictureBox, AddRadioButton, RemoveRadioButton,
                SPlugPictureBox, AddSPlugButton, RemoveSPlugButton,
                DrumCanPictureBox, AddDrumCanButton, RemoveDrumCanButton,
                CBoxPictureBox, CBoxDurabilityLabel, CBoxDurabilityTextBox, SetCBoxDurabilityButton
            };
            ownershipButtons = new[]
            {
                AddCigsButton, RemoveCigsButton, AddMunaButton, RemoveMunaButton, AddSyringeButton, RemoveSyringeButton,
                AddBandanaButton, RemoveBandanaButton, AddStealthButton, RemoveStealthButton,
                AddCameraButton, RemoveCameraButton, AddRadioButton, RemoveRadioButton,
                AddSPlugButton, RemoveSPlugButton, AddDrumCanButton, RemoveDrumCanButton
            };
            AddInventoryEditor(0, CurrentRationTextBox, MaxRationTextBox, SetRationButton, RationPictureBox, CurrentRationLabel, MaxRationLabel);
            AddInventoryEditor(1, CurrentNoodlesTextBox, MaxNoodlesTextBox, SetNoodlesButton, NoodlesPictureBox, CurrentNoodlesLabel, MaxNoodlesLabel);
            AddInventoryEditor(2, CurrentRegainTextBox, MaxRegainTextBox, SetRegainButton, RegainPictureBox, CurrentRegainLabel, MaxRegainLabel);
            AddInventoryEditor(3, CurrentPentazeminTextBox, MaxPentazeminTextBox, SetPentazeminButton, PentazeminPictureBox, CurrentPentazeminLabel, MaxPentazeminLabel);
            AddInventoryEditor(4, CurrentCompressTextBox, MaxCompressTextBox, SetCompressButton, CompressPictureBox, CurrentCompressLabel, MaxCompressLabel);
            AddInventoryEditor(14, CBoxDurabilityTextBox, null, SetCBoxDurabilityButton, CBoxPictureBox, CBoxDurabilityLabel, null);
            BindOwnership(5, AddCigsButton, RemoveCigsButton, CigsPictureBox);
            BindOwnership(6, AddMunaButton, RemoveMunaButton, MunaPictureBox);
            BindOwnership(7, AddSyringeButton, RemoveSyringeButton, SyringePictureBox);
            BindOwnership(8, AddBandanaButton, RemoveBandanaButton, BandanaPictureBox);
            BindOwnership(9, AddStealthButton, RemoveStealthButton, StealthPictureBox);
            BindOwnership(10, AddCameraButton, RemoveCameraButton, CameraPictureBox);
            BindOwnership(11, AddRadioButton, RemoveRadioButton, RadioPictureBox);
            BindOwnership(12, AddSPlugButton, RemoveSPlugButton, SPlugPictureBox);
            BindOwnership(13, AddDrumCanButton, RemoveDrumCanButton, DrumCanPictureBox);
            RecoveryItemsButton.Click += (sender, args) => ShowRecoveryItems(true);
            GadgetsButton.Click += (sender, args) => ShowRecoveryItems(false);
            BindHover(RecoveryItemsButton, "Show recovery items and their inventory controls.");
            BindHover(GadgetsButton, "Show gadgets and hide recovery items.");
            ShowRecoveryItems(true);
            ClearInventory();
        }

        private void AddInventoryEditor(int index, TextBox current, TextBox maximum, Button apply, PictureBox picture, Label currentLabel, Label maximumLabel)
        {
            var editor = new InventoryEditor { Item = InventoryManager.Items[index], Current = current, Maximum = maximum, Apply = apply };
            inventoryEditors.Add(editor);
            current.TextChanged += (sender, args) => { if (!renderingInventory) editor.Dirty = true; };
            current.Leave += (sender, args) => RefreshInventoryEditor(editor);
            apply.Click += async (sender, args) => await ApplyInventoryEditorAsync(editor);
            if (maximum == null)
            {
                BindHover(current, "C. Box durability: enter -1 to remove it, or 0 to 25 to set durability.");
                BindHover(apply, "Apply and verify C. Box durability (-1 to 25).");
                BindHover(picture, "C. Box: edit durability (-1 to 25), then press Set. -1 removes it from inventory.");
                BindHover(currentLabel, "C. Box durability from the inventory capture. Valid inputs are -1 to 25.");
                return;
            }
            maximum.TextChanged += (sender, args) => { if (!renderingInventory) editor.Dirty = true; };
            maximum.Leave += (sender, args) => RefreshInventoryEditor(editor);
            BindHover(current, "Current " + editor.Item.Name + " quantity. Set applies Current and Max together; -1 removes it from inventory.");
            BindHover(maximum, editor.Item.Name + " capacity. Enter a quantity or -1.");
            BindHover(apply, "Apply and verify both " + editor.Item.Name + " quantities.");
            BindHover(picture, editor.Item.Name + ": edit Current and Max, then press Set.");
            BindHover(currentLabel, "Current " + editor.Item.Name + " quantity from the inventory capture.");
            BindHover(maximumLabel, "Maximum " + editor.Item.Name + " quantity.");
        }

        private void BindOwnership(int index, Button add, Button remove, PictureBox picture)
        {
            InventoryItemDefinition item = InventoryManager.Items[index];
            add.Click += async (sender, args) => await SetInventoryOwnershipAsync(item, true);
            remove.Click += async (sender, args) => await SetInventoryOwnershipAsync(item, false);
            BindHover(add, "Add " + item.Name + " to inventory.");
            BindHover(remove, "Remove " + item.Name + " from inventory by setting its current quantity to -1.");
            BindHover(picture, "Use Add or Remove to change " + item.Name + " ownership.");
        }

        internal void ShowRecoveryItems(bool visible)
        {
            if (recoveryControls == null || showingRecoveryItems == visible) return;
            UpdateCategoryControls(() =>
            {
                foreach (Control control in recoveryControls) control.Visible = visible;
                foreach (Control control in gadgetControls) control.Visible = !visible;
                // The full-width item pictures overlap their editors. Initially hidden
                // controls create native windows on first display, so restore their
                // foreground order after showing them, not just their Visible flags.
                foreach (Control control in visible ? recoveryControls : gadgetControls)
                    if (!(control is PictureBox)) control.BringToFront();
                showingRecoveryItems = visible;
            });
        }

        private void UpdateCategoryControls(Action update)
        {
            // Only pause a natively visible window: WM_SETREDRAW(true) can otherwise
            // show a form that was intentionally hidden (including the designer).
            IntPtr redrawHandle = IsHandleCreated && Visible && !IsDisposed && !Disposing ? Handle : IntPtr.Zero;
            if (redrawHandle != IntPtr.Zero && !CategoryPainting.IsWindowVisible(redrawHandle)) redrawHandle = IntPtr.Zero;
            SuspendLayout();
            try
            {
                if (redrawHandle != IntPtr.Zero)
                    CategoryPainting.SendMessage(redrawHandle, CategoryPainting.SetRedraw, IntPtr.Zero, IntPtr.Zero);
                update();
            }
            finally
            {
                try { ResumeLayout(false); }
                finally
                {
                    if (redrawHandle != IntPtr.Zero && !IsDisposed && IsHandleCreated && Handle == redrawHandle)
                    {
                        CategoryPainting.SendMessage(redrawHandle, CategoryPainting.SetRedraw, new IntPtr(1), IntPtr.Zero);
                        CategoryPainting.RedrawWindow(redrawHandle, IntPtr.Zero, IntPtr.Zero, CategoryPainting.RepaintAll);
                    }
                }
            }
        }

        private static class CategoryPainting
        {
            internal const int SetRedraw = 0x000B;
            // RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN.
            // Queue the completed page once, including native controls and borders.
            // https://learn.microsoft.com/en-us/windows/win32/gdi/wm-setredraw
            internal const uint RepaintAll = 0x0001 | 0x0004 | 0x0400 | 0x0080;

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWindowVisible(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);
        }

        private InventorySnapshot ReadInventorySnapshot()
        {
            return manager.WithSession((process, handle) =>
            {
                var memory = MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress);
                if (!manager.IsEnabled(TableEffect.InventoryPointer))
                    return new InventorySnapshot(memory.SessionIdentity, IntPtr.Zero, false,
                        "Waiting for the inventory hook to become available.");
                return InventoryManager.Read(memory);
            });
        }

        internal void ApplyInventorySnapshot(InventorySnapshot snapshot)
        {
            if (snapshot == null) { ClearInventory(); return; }
            if (snapshot.GameIdentity != gameIdentity) return;
            bool changed = inventorySnapshot == null || inventorySnapshot.GameIdentity != snapshot.GameIdentity ||
                inventorySnapshot.InventoryAddress != snapshot.InventoryAddress;
            if (changed) ClearInventory();
            inventorySnapshot = snapshot;
            if (snapshot.IsReady)
                foreach (InventoryEditor editor in inventoryEditors) RefreshInventoryEditor(editor);
            SetInventoryInteractive(CanUpdate);
        }

        private void RefreshInventoryEditor(InventoryEditor editor)
        {
            if (inventorySnapshot == null || !inventorySnapshot.IsReady || editor.Dirty ||
                (editor.Populated && (editor.Current.Focused || (editor.Maximum != null && editor.Maximum.Focused)))) return;
            if (!inventorySnapshot.Items.TryGetValue(editor.Item.Current, out InventoryItemValue value)) return;
            renderingInventory = true;
            try
            {
                editor.Current.Text = InventoryManager.FormatQuantity(value.Current);
                if (editor.Maximum != null) editor.Maximum.Text = InventoryManager.FormatQuantity(value.Maximum);
                editor.Populated = true;
            }
            finally { renderingInventory = false; }
        }

        private void SetInventoryInteractive(bool available)
        {
            bool ready = available && !operationInProgress && inventorySnapshot != null && inventorySnapshot.IsReady &&
                inventorySnapshot.GameIdentity == gameIdentity;
            foreach (InventoryEditor editor in inventoryEditors)
            {
                editor.Current.Enabled = ready;
                if (editor.Maximum != null) editor.Maximum.Enabled = ready;
                editor.Apply.Enabled = ready && editor.Populated;
            }
            if (ownershipButtons != null)
                foreach (Button button in ownershipButtons) button.Enabled = ready;
        }

        internal void ClearInventory()
        {
            inventorySnapshot = null;
            renderingInventory = true;
            try
            {
                foreach (InventoryEditor editor in inventoryEditors)
                {
                    editor.Dirty = false;
                    editor.Populated = false;
                    editor.Current.Clear();
                    if (editor.Maximum != null) editor.Maximum.Clear();
                }
            }
            finally { renderingInventory = false; }
            SetInventoryInteractive(false);
        }

        private async Task ApplyInventoryEditorAsync(InventoryEditor editor)
        {
            if (!CanUpdate || operationInProgress || inventorySnapshot == null || !inventorySnapshot.IsReady || !editor.Populated) return;
            ushort current;
            ushort maximum = 0;
            string error;
            bool valid = editor.Maximum == null
                ? InventoryManager.TryParseBoxDurability(editor.Current.Text, out current, out error)
                : InventoryManager.TryParsePair(editor.Current.Text, editor.Maximum.Text, out current, out maximum, out error);
            if (!valid)
            { SetStatus(editor.Item.Name + ": " + error); return; }
            InventorySnapshot expected = inventorySnapshot;
            bool success = await RunOperationAsync("Applying " + editor.Item.Name + "...", () => manager.WithSession((process, handle) =>
            {
                if (!manager.IsEnabled(TableEffect.InventoryPointer)) throw new InvalidOperationException("The inventory hook is no longer active.");
                var memory = MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress);
                if (editor.Maximum == null) InventoryManager.SetBoxDurability(memory, expected, current);
                else InventoryManager.Set(memory, expected, editor.Item, current, maximum);
                return editor.Item.Name + " applied and verified.";
            }));
            if (!success || !CanUpdate) return;
            editor.Dirty = false;
            editor.Populated = false;
            RefreshInventoryEditor(editor);
        }

        private async Task SetInventoryOwnershipAsync(InventoryItemDefinition item, bool owned)
        {
            if (!CanUpdate || operationInProgress || inventorySnapshot == null || !inventorySnapshot.IsReady) return;
            InventorySnapshot expected = inventorySnapshot;
            await RunOperationAsync((owned ? "Adding " : "Removing ") + item.Name + "...", () => manager.WithSession((process, handle) =>
            {
                if (!manager.IsEnabled(TableEffect.InventoryPointer)) throw new InvalidOperationException("The inventory hook is no longer active.");
                InventoryManager.SetOwned(MemoryTransactionManager.ForProcess(process, handle, manager.ResolveSymbolAddress), expected, item, owned);
                return item.Name + (owned ? " added" : " removed") + " and verified.";
            }));
        }
    }
}
