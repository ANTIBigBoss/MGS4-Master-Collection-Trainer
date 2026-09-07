using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Captures the last RDX observed at the player update instruction in mgs4.exe.</summary>
    public sealed class PlayerHookManager
    {
        public static PlayerHookManager Instance { get; } = new PlayerHookManager();

        private static readonly byte[] originalInstruction = { 0x48, 0x63, 0x82, 0x60, 0x01, 0x00, 0x00 };
        private readonly object gate = new object();
        private Process targetProcess;
        private IntPtr processHandle;
        private IntPtr hookAddress;
        private IntPtr allocationAddress;
        private byte[] installedPatch;
        private bool enabled;
        // Also true after an unsuccessful rollback: never free potentially referenced code.
        private bool patchMayBeInstalled;
        private string lastError = string.Empty;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(IntPtr handle, [MarshalAs(UnmanagedType.Bool)] out bool isWow64);

        public bool IsEnabled { get { lock (gate) return SessionIsAlive() && enabled && VerifyOwnedPatch(); } }
        internal int AttachedProcessId { get { lock (gate) return SessionIsAlive() ? targetProcess.Id : 0; } }
        internal byte[] InstalledPatch { get { lock (gate) return installedPatch == null ? null : (byte[])installedPatch.Clone(); } }
        public string LastError { get { lock (gate) return lastError; } }
        public IntPtr HookAddress { get { lock (gate) return SessionIsAlive() ? hookAddress : IntPtr.Zero; } }
        public IntPtr PointerStorageAddress
        {
            get
            {
                lock (gate)
                    return SessionIsAlive() && allocationAddress != IntPtr.Zero
                        ? MemoryManager.AddOffset(allocationAddress, Constants.PlayerHookPointerOffset) : IntPtr.Zero;
            }
        }

        /// <summary>Installs the hook in mgs4's main module. False means LastError contains the reason.</summary>
        public bool Enable()
        {
            lock (gate) return EnableCore(null, IntPtr.Zero, 0);
        }

        /// <summary>Reads game memory and adopts an exact existing trainer hook, without patching or allocating.</summary>
        public bool AttachExisting()
        {
            lock (gate) return EnableCore(null, IntPtr.Zero, 0, attachOnly: true);
        }

        internal bool AttachExistingForProcess(Process process, IntPtr scanStart, long scanSize)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            lock (gate) return EnableCore(process, scanStart, scanSize, attachOnly: true);
        }

        // Shares the effect manager's selected process and module range. The supplied Process remains caller-owned.
        internal bool EnableForProcess(Process process, IntPtr scanStart, long scanSize)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            lock (gate) return EnableCore(process, scanStart, scanSize);
        }

        private bool EnableCore(Process suppliedProcess, IntPtr scanStart, long scanSize, bool attachOnly = false)
        {
            try
            {
                if (SessionIsAlive())
                {
                    if (suppliedProcess != null && suppliedProcess.Id != targetProcess.Id)
                        return Fail("Disable the existing player hook before attaching to a different process.");
                    if (enabled && VerifyOwnedPatch()) { lastError = string.Empty; return true; }
                    return Fail("The previous hook still owns remote memory. Disable it before trying again.");
                }

                if (IntPtr.Size != 8) return Fail("The player hook requires a 64-bit trainer.");
                targetProcess = suppliedProcess == null ? MemoryManager.GetGameProcess() : Process.GetProcessById(suppliedProcess.Id);
                if (targetProcess == null) return Fail("Waiting for mgs4.exe.");
                using (Process currentProcess = Process.GetCurrentProcess())
                    if (targetProcess.Id == currentProcess.Id)
                        throw new InvalidOperationException("The player hook cannot suspend its own process.");
                if (targetProcess.HasExited) throw new InvalidOperationException("The target process has exited.");
                processHandle = MemoryManager.OpenGameProcess(targetProcess);
                if (processHandle == IntPtr.Zero) throw new InvalidOperationException("Could not open the target process.");
                if (!IsWow64Process(processHandle, out bool wow64) || wow64)
                    throw new InvalidOperationException("The target must be a native 64-bit process.");

                var aob = AobManager.AOBs[Constants.PlayerHookAobKey];
                if (suppliedProcess == null)
                {
                    ProcessModule module = targetProcess.MainModule;
                    if (module == null) throw new InvalidOperationException("The game module is unavailable.");
                    long start = aob.StartOffset?.ToInt64() ?? 0;
                    long end = aob.EndOffset?.ToInt64() ?? module.ModuleMemorySize;
                    if (start < 0 || end <= start || end > module.ModuleMemorySize)
                        throw new InvalidOperationException("Player AOB bounds must lie within mgs4.exe.");
                    scanStart = MemoryManager.AddOffset(module.BaseAddress, start);
                    scanSize = end - start;
                }

                var matches = AobRecoveryManager.ScanExecutable(processHandle, scanStart, scanSize, aob.Pattern, aob.Mask);
                var existing = FindExistingHooks(scanStart, scanSize);
                if (existing.Count > 1 || existing.Count == 1 && matches.Count != 0)
                    throw new InvalidOperationException("Player hook recovery is ambiguous; multiple original or installed sites were found.");
                if (existing.Count == 1)
                {
                    hookAddress = existing[0].Item1;
                    allocationAddress = existing[0].Item2;
                    installedPatch = BuildJump(hookAddress, allocationAddress, Constants.PlayerHookOverwriteLength);
                    patchMayBeInstalled = true;
                    enabled = true;
                    lastError = string.Empty;
                    LoggingManager.Instance.Log("Reattached existing player hook at 0x" + hookAddress.ToInt64().ToString("X") +
                        "; retained pointer slot 0x" + PointerStorageAddress.ToInt64().ToString("X") + ".");
                    return true;
                }
                if (matches.Count != 1)
                    throw new InvalidOperationException("Player AOB must match exactly once; found " + matches.Count + ".");
                if (attachOnly) { ForgetSession(); lastError = string.Empty; return false; }
                hookAddress = matches[0];
                if (!BytesEqual(MemoryManager.ReadMemoryBytes(processHandle, hookAddress, originalInstruction.Length), originalInstruction))
                    throw new InvalidOperationException("The player update instruction differs from the expected bytes.");

                allocationAddress = RemoteCodeManager.AllocateNear(processHandle, hookAddress, Constants.PlayerHookAllocationSize);
                if (allocationAddress == IntPtr.Zero) throw new InvalidOperationException("No reachable memory is available for the player hook.");
                byte[] cave = BuildCaptureCode(hookAddress, allocationAddress);
                if (!MemoryManager.WriteMemory(processHandle, allocationAddress, cave))
                    throw new InvalidOperationException("Could not write the player capture code.");
                RemoteCodeManager.MakeExecutable(processHandle, allocationAddress, cave.Length);
                RemoteCodeManager.Flush(processHandle, allocationAddress, cave.Length);
                installedPatch = BuildJump(hookAddress, allocationAddress, Constants.PlayerHookOverwriteLength);

                using (var paused = RemoteCodeManager.SuspendThreads(targetProcess))
                {
                    if (paused.HasInstructionPointerInRange(MemoryManager.AddOffset(hookAddress, 1), originalInstruction.Length - 1))
                        throw new InvalidOperationException("A thread is inside the instruction being replaced; try enabling again.");
                    try
                    {
                        RemoteCodeManager.ReplaceCode(processHandle, hookAddress, originalInstruction, installedPatch);
                        patchMayBeInstalled = true;
                        enabled = true;
                    }
                    catch (RemoteCodePatchException ex)
                    {
                        patchMayBeInstalled = !ex.OriginalBytesRestored;
                        throw;
                    }
                }

                lastError = string.Empty;
                LoggingManager.Instance.Log($"Player hook enabled at 0x{hookAddress.ToInt64():X}; pointer slot 0x{PointerStorageAddress.ToInt64():X}.");
                return true;
            }
            catch (Exception ex)
            {
                SetError("Player hook enable failed: " + ex.Message);
                // A failed patch rollback may leave a jump to the cave: retain it for recovery.
                if (!patchMayBeInstalled) ReleaseUnusedAllocation();
                return false;
            }
        }

        private List<Tuple<IntPtr, IntPtr>> FindExistingHooks(IntPtr scanStart, long scanSize)
        {
            byte[] pattern = { 0xE9, 0, 0, 0, 0, 0x90, 0x90, 0x49 };
            var found = new List<Tuple<IntPtr, IntPtr>>();
            foreach (IntPtr site in AobRecoveryManager.ScanExecutable(processHandle, scanStart, scanSize, pattern, "x????xxx"))
            {
                try
                {
                    byte[] patch = MemoryManager.ReadMemoryBytes(processHandle, site, Constants.PlayerHookOverwriteLength);
                    if (patch == null) continue;
                    IntPtr cave = new IntPtr(checked(site.ToInt64() + 5L + BitConverter.ToInt32(patch, 1)));
                    if (!IsPrivateCaptureAllocation(cave)) continue;
                    byte[] expected = BuildCaptureCode(site, cave);
                    byte[] actual = MemoryManager.ReadMemoryBytes(processHandle, cave, Constants.PlayerHookPointerOffset);
                    if (actual == null || !actual.SequenceEqual(expected.Take(Constants.PlayerHookPointerOffset))) continue;
                    if (!BytesEqual(patch, BuildJump(site, cave, Constants.PlayerHookOverwriteLength))) continue;
                    found.Add(Tuple.Create(site, cave));
                }
                catch (OverflowException) { }
                catch (ArgumentException) { }
            }
            return found;
        }

        private bool IsPrivateCaptureAllocation(IntPtr cave)
        {
            if (cave.ToInt64() <= 0) return false;
            UIntPtr size = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            if (MemoryManager.NativeMethods.VirtualQueryEx(processHandle, cave, out var page, size) == UIntPtr.Zero) return false;
            return page.AllocationBase == cave && page.BaseAddress == cave && page.State == 0x1000 && page.Type == 0x20000 &&
                (page.Protect & (0x100 | 0x01)) == 0 && (page.Protect & (0x40 | 0x80)) != 0 &&
                page.RegionSize.ToUInt64() >= (ulong)Constants.PlayerHookAllocationSize &&
                page.RegionSize.ToUInt64() == (ulong)Environment.SystemPageSize;
        }

        private bool VerifyOwnedPatch()
        {
            if (hookAddress == IntPtr.Zero || allocationAddress == IntPtr.Zero || installedPatch == null) return false;
            if (!BytesEqual(MemoryManager.ReadMemoryBytes(processHandle, hookAddress, installedPatch.Length), installedPatch))
            {
                SetError("Player hook instruction bytes changed. Existing state was retained for checked cleanup.");
                return false;
            }
            byte[] code = MemoryManager.ReadMemoryBytes(processHandle, allocationAddress, Constants.PlayerHookPointerOffset);
            if (code == null || !code.SequenceEqual(BuildCaptureCode(hookAddress, allocationAddress).Take(Constants.PlayerHookPointerOffset)))
            {
                SetError("Player capture code changed or became unreadable. Existing state was retained for checked cleanup.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Restores the original instruction. On failure, retains memory
        /// that might still be executing; inspect LastError and retry instead of abandoning the manager.
        /// </summary>
        public bool Disable()
        {
            lock (gate)
            {
                if (!SessionIsAlive()) { lastError = string.Empty; return true; }
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        bool busy;
                        using (var paused = RemoteCodeManager.SuspendThreads(targetProcess))
                        {
                            busy = allocationAddress != IntPtr.Zero &&
                                paused.HasInstructionPointerInRange(allocationAddress, Constants.PlayerHookAllocationSize);
                            busy |= patchMayBeInstalled && paused.HasInstructionPointerInRange(
                                MemoryManager.AddOffset(hookAddress, 1), originalInstruction.Length - 1);
                            if (!busy)
                            {
                                if (patchMayBeInstalled)
                                {
                                    byte[] current = MemoryManager.ReadMemoryBytes(processHandle, hookAddress, originalInstruction.Length);
                                    if (BytesEqual(current, installedPatch))
                                    {
                                        RemoteCodeManager.ReplaceCode(processHandle, hookAddress, installedPatch, originalInstruction);
                                    }
                                    else if (!BytesEqual(current, originalInstruction))
                                    {
                                        enabled = false;
                                        throw new InvalidOperationException("Player hook bytes were changed externally; no bytes were overwritten and the allocation was retained.");
                                    }
                                    patchMayBeInstalled = false;
                                    enabled = false;
                                }
                                if (allocationAddress != IntPtr.Zero && !RemoteCodeManager.Free(processHandle, allocationAddress))
                                    throw new InvalidOperationException("Player instruction is restored, but its allocation could not be released. Retry Disable.");
                                allocationAddress = IntPtr.Zero;
                            }
                        }
                        if (!busy)
                        {
                            ForgetSession();
                            lastError = string.Empty;
                            LoggingManager.Instance.Log("Player hook disabled.");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!SessionIsAlive()) { lastError = string.Empty; return true; }
                        if (ex is RemoteCodePatchException patchError && !patchError.OriginalBytesRestored) enabled = false;
                        return Fail("Player hook disable failed: " + ex.Message);
                    }
                    // Always resume the game before waiting for a thread to leave the cave.
                    Thread.Sleep(10);
                }
                return Fail("A game thread is still inside the player capture code. The hook remains allocated; retry Disable.");
            }
        }

        /// <summary>Returns the last captured RDX, or zero before the hook runs / after the process exits.</summary>
        public IntPtr GetPlayerAddress()
        {
            lock (gate) return GetPlayerAddressCore();
        }

        private IntPtr GetPlayerAddressCore()
        {
            if (!SessionIsAlive() || !enabled || !VerifyOwnedPatch()) return IntPtr.Zero;
            IntPtr pointer = MemoryManager.Instance.ReadIntPtr(processHandle,
                MemoryManager.AddOffset(allocationAddress, Constants.PlayerHookPointerOffset));
            return pointer.ToInt64() > 0 ? pointer : IntPtr.Zero;
        }

        /// <summary>Reads from the captured object plus a signed field offset using this hook's process handle.</summary>
        public byte[] ReadPlayerBytes(int fieldOffset, int count)
        {
            lock (gate)
            {
                if (count <= 0) return null;
                try
                {
                    IntPtr address = PlayerFieldAddress(fieldOffset);
                    return address == IntPtr.Zero ? null : MemoryManager.ReadMemoryBytes(processHandle, address, count);
                }
                catch (Exception ex) { SetError("Player read failed: " + ex.Message); return null; }
            }
        }

        public bool WritePlayerValue<T>(int fieldOffset, T value)
        {
            lock (gate)
            {
                try
                {
                    IntPtr address = PlayerFieldAddress(fieldOffset);
                    return address != IntPtr.Zero && MemoryManager.WriteMemory(processHandle, address, value);
                }
                catch (Exception ex) { return Fail("Player write failed: " + ex.Message); }
            }
        }

        private IntPtr PlayerFieldAddress(int fieldOffset)
        {
            IntPtr player = GetPlayerAddressCore();
            if (player == IntPtr.Zero) return IntPtr.Zero;
            IntPtr address = MemoryManager.AddOffset(player, fieldOffset);
            return address.ToInt64() > 0 ? address : IntPtr.Zero;
        }

        private bool SessionIsAlive()
        {
            if (targetProcess == null) return false;
            if (!targetProcess.HasExited) return true;
            // Windows has already reclaimed the exited process's allocations. Never use them in a new process.
            ForgetSession();
            return false;
        }

        private void ReleaseUnusedAllocation()
        {
            if (allocationAddress != IntPtr.Zero && SessionIsAlive() && !RemoteCodeManager.Free(processHandle, allocationAddress))
            {
                SetError(lastError + " Unused allocation could not be released; retry Disable.");
                return;
            }
            ForgetSession();
        }

        private void ForgetSession()
        {
            MemoryManager.CloseGameProcess(processHandle);
            targetProcess?.Dispose();
            targetProcess = null;
            processHandle = IntPtr.Zero;
            hookAddress = IntPtr.Zero;
            allocationAddress = IntPtr.Zero;
            installedPatch = null;
            enabled = false;
            patchMayBeInstalled = false;
        }

        private bool Fail(string message) { SetError(message); return false; }

        private void SetError(string message)
        {
            if (message != lastError) LoggingManager.Instance.Log(message);
            lastError = message;
        }

        private static bool BytesEqual(byte[] first, byte[] second)
        {
            return first != null && second != null && first.SequenceEqual(second);
        }

        internal static byte[] BuildCaptureCode(IntPtr sourceAddress, IntPtr caveAddress)
        {
            byte[] code = new byte[Constants.PlayerHookAllocationSize];
            // mov [rip+disp32],rdx (7 bytes). The eight-byte slot at +64 starts zeroed.
            code[0] = 0x48; code[1] = 0x89; code[2] = 0x15;
            Array.Copy(RelativeDisplacement(MemoryManager.AddOffset(caveAddress, 7),
                MemoryManager.AddOffset(caveAddress, Constants.PlayerHookPointerOffset)), 0, code, 3, 4);
            Array.Copy(originalInstruction, 0, code, 7, originalInstruction.Length);
            Array.Copy(BuildJump(MemoryManager.AddOffset(caveAddress, 14),
                MemoryManager.AddOffset(sourceAddress, originalInstruction.Length), 5), 0, code, 14, 5);
            return code;
        }

        internal static byte[] BuildJump(IntPtr sourceAddress, IntPtr destination, int length)
        {
            if (length < 5) throw new ArgumentOutOfRangeException(nameof(length));
            byte[] bytes = Enumerable.Repeat((byte)0x90, length).ToArray();
            bytes[0] = 0xE9;
            Array.Copy(RelativeDisplacement(MemoryManager.AddOffset(sourceAddress, 5), destination), 0, bytes, 1, 4);
            return bytes;
        }

        private static byte[] RelativeDisplacement(IntPtr nextInstruction, IntPtr destination)
        {
            long displacement = checked(destination.ToInt64() - nextInstruction.ToInt64());
            return BitConverter.GetBytes(checked((int)displacement));
        }
    }
}
