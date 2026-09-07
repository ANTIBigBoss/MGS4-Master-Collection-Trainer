using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Stops the supplied stress-write instruction. Does not reset existing stress.</summary>
    public sealed class StressManager
    {
        public static StressManager Instance { get; } = new StressManager();

        private static readonly byte[] originalInstruction = { 0x66, 0x41, 0x89, 0x81, 0x50, 0x0B, 0x00, 0x00 };
        private static readonly byte[] installedPatch = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
        private readonly object gate = new object();
        private Process targetProcess;
        private IntPtr processHandle;
        private IntPtr patchAddress;
        private bool enabled;
        // Retain the original process/address when a failed rollback leaves uncertain code.
        private bool patchMayBeInstalled;
        private string lastError = string.Empty;

        public bool IsEnabled { get { lock (gate) return SessionIsAlive() && enabled && VerifyOwnedPatch(); } }
        internal int AttachedProcessId { get { lock (gate) return SessionIsAlive() ? targetProcess.Id : 0; } }
        public string LastError { get { lock (gate) return lastError; } }
        public IntPtr PatchAddress { get { lock (gate) return SessionIsAlive() ? patchAddress : IntPtr.Zero; } }

        /// <summary>Finds the unique stress instruction in mgs4.exe and replaces its eight bytes with NOPs.</summary>
        public bool Enable()
        {
            lock (gate) return EnableCore(null, IntPtr.Zero, 0);
        }

        /// <summary>Reads game memory and adopts a verified existing stress patch without writing code.</summary>
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
                        return Fail("Disable the existing stress patch before attaching to a different process.");
                    if (enabled && BytesEqual(ReadInstruction(), installedPatch))
                    {
                        lastError = string.Empty;
                        return true;
                    }
                    enabled = false;
                    return Fail("The existing stress patch could not be verified. Disable it before enabling again.");
                }

                if (IntPtr.Size != 8) return Fail("The stress patch requires a 64-bit trainer.");
                targetProcess = suppliedProcess == null ? MemoryManager.GetGameProcess() : Process.GetProcessById(suppliedProcess.Id);
                if (targetProcess == null) return Fail("Waiting for mgs4.exe.");
                using (Process current = Process.GetCurrentProcess())
                    if (targetProcess.Id == current.Id)
                        throw new InvalidOperationException("The trainer cannot suspend its own process.");
                if (targetProcess.HasExited) throw new InvalidOperationException("The target process has exited.");
                processHandle = MemoryManager.OpenGameProcess(targetProcess);
                if (processHandle == IntPtr.Zero) throw new InvalidOperationException("Could not open the target process.");
                if (targetProcess.HasExited) throw new InvalidOperationException("The target process has exited.");

                var aob = AobManager.AOBs[Constants.StressAobKey];
                if (suppliedProcess == null)
                {
                    ProcessModule module = targetProcess.MainModule;
                    if (module == null) throw new InvalidOperationException("The game module is unavailable.");
                    long start = aob.StartOffset?.ToInt64() ?? 0;
                    long end = aob.EndOffset?.ToInt64() ?? module.ModuleMemorySize;
                    if (start < 0 || end <= start || end > module.ModuleMemorySize)
                        throw new InvalidOperationException("Stress AOB bounds must lie within mgs4.exe.");
                    scanStart = MemoryManager.AddOffset(module.BaseAddress, start);
                    scanSize = end - start;
                }

                var matches = AobRecoveryManager.ScanExecutable(processHandle, scanStart, scanSize, aob.Pattern, aob.Mask);
                if (matches.Count == 0 && TryAdoptExisting(scanStart, scanSize, aob.Pattern, aob.Mask)) return true;
                if (matches.Count != 1)
                    throw new InvalidOperationException("Stress AOB must match exactly once; found " + matches.Count + ".");
                if (attachOnly) { ForgetSession(); lastError = string.Empty; return false; }
                patchAddress = matches[0];
                if (!BytesEqual(ReadInstruction(), originalInstruction))
                    throw new InvalidOperationException("The stress instruction differs from the expected bytes.");

                // SuspendThreads also validates that the target is native x64.
                using (var paused = RemoteCodeManager.SuspendThreads(targetProcess))
                {
                    if (paused.HasInstructionPointerInRange(MemoryManager.AddOffset(patchAddress, 1), originalInstruction.Length - 1))
                        throw new InvalidOperationException("A game thread is inside the stress instruction; try enabling again.");
                    try
                    {
                        RemoteCodeManager.ReplaceCode(processHandle, patchAddress, originalInstruction, installedPatch);
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
                LoggingManager.Instance.Log($"Stress write disabled at 0x{patchAddress.ToInt64():X}.");
                return true;
            }
            catch (Exception ex)
            {
                if (!patchMayBeInstalled) ForgetSession();
                return Fail("Stress patch enable failed: " + ex.Message);
            }
        }

        private bool TryAdoptExisting(IntPtr scanStart, long scanSize, byte[] pattern, string mask)
        {
            AobRecoveryModule image = null;
            ProcessModule module = targetProcess.MainModule;
            if (module != null && scanStart.ToInt64() >= module.BaseAddress.ToInt64() &&
                checked(scanStart.ToInt64() + scanSize) <= checked(module.BaseAddress.ToInt64() + module.ModuleMemorySize))
                image = AobRecoveryManager.OpenModule(targetProcess, processHandle, module.BaseAddress, module.ModuleMemorySize);
            if (image == null)
                throw new InvalidOperationException("The stress signature is missing. Existing NOPs cannot be identified without a verified original module image.");
            var originalSites = image.FindOriginal(pattern, mask);
            if (originalSites.Count != 1)
                throw new InvalidOperationException("Stress recovery requires exactly one original module-image signature; found " + originalSites.Count + ".");
            IntPtr site = originalSites[0];
            if (site.ToInt64() < scanStart.ToInt64() || site.ToInt64() > checked(scanStart.ToInt64() + scanSize - originalInstruction.Length))
                throw new InvalidOperationException("The original stress site is outside the requested scan range.");
            if (!BytesEqual(MemoryManager.ReadMemoryBytes(processHandle, site, originalInstruction.Length), installedPatch))
                throw new InvalidOperationException("The original stress site contains unknown bytes; no patch was adopted or overwritten.");

            var spans = new List<AobRecoverySpan> { new AobRecoverySpan(0, originalInstruction.Length) };
            // These player stores can share the surrounding routine. Ignore only exact verified
            // neighboring trainer jumps, rather than rejecting stress when another cheat is on.
            foreach (var neighbor in new[] { new { Key = "aPlayer", Length = 7 }, new { Key = "aLife", Length = 8 },
                new { Key = "aStam", Length = 8 }, new { Key = "aBatt", Length = 7 } })
            {
                SignatureDefinition definition = EffectDefinitionManager.Signatures.Single(signature => signature.Key == neighbor.Key);
                foreach (IntPtr address in image.FindOriginal(definition.Pattern, definition.Mask))
                {
                    long relative = address.ToInt64() - site.ToInt64();
                    if (relative < -64 || relative > originalInstruction.Length + 64) continue;
                    byte[] bytes = MemoryManager.ReadMemoryBytes(processHandle, address, neighbor.Length);
                    if (bytes == null || bytes[0] != 0xE9 || bytes.Skip(5).Any(value => value != 0x90)) continue;
                    // A changed neighbor is ignored only when its destination lies in private executable memory.
                    IntPtr destination = new IntPtr(checked(address.ToInt64() + 5L + BitConverter.ToInt32(bytes, 1)));
                    UIntPtr infoSize = new UIntPtr((uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
                    if (MemoryManager.NativeMethods.VirtualQueryEx(processHandle, destination, out var page, infoSize) == UIntPtr.Zero ||
                        page.AllocationBase != destination || page.Type != 0x20000 || page.State != 0x1000 || (page.Protect & (0x40 | 0x80)) == 0) continue;
                    byte[] expected;
                    if (neighbor.Key == "aPlayer") expected = PlayerHookManager.BuildCaptureCode(address, destination).Take(Constants.PlayerHookPointerOffset).ToArray();
                    else
                    {
                        TableEffect effect = neighbor.Key == "aLife" ? TableEffect.InfiniteLife : neighbor.Key == "aStam" ? TableEffect.InfiniteStamina : TableEffect.InfiniteBattery;
                        EffectPlan plan = EffectDefinitionManager.All[effect].Build(new EffectBuildContext(destination,
                            new Dictionary<string, IntPtr> { { neighbor.Key, address } }, symbol => IntPtr.Zero,
                            (readAddress, count) => MemoryManager.ReadMemoryBytes(processHandle, readAddress, count)));
                        expected = plan.AllocationBytes.Take(EffectDefinitionManager.DataOffset).ToArray();
                    }
                    if (BytesEqual(MemoryManager.ReadMemoryBytes(processHandle, destination, expected.Length), expected))
                        spans.Add(new AobRecoverySpan(relative, neighbor.Length));
                }
            }
            if (!image.ValidateContext(site, spans, out string error))
                throw new InvalidOperationException("Stress recovery context could not be verified: " + error);
            patchAddress = site;
            patchMayBeInstalled = true;
            enabled = true;
            lastError = string.Empty;
            LoggingManager.Instance.Log("Reattached existing stress patch at 0x" + site.ToInt64().ToString("X") + ".");
            return true;
        }

        private bool VerifyOwnedPatch()
        {
            if (patchAddress != IntPtr.Zero && BytesEqual(ReadInstruction(), installedPatch)) return true;
            lastError = "Stress instruction bytes changed or became unreadable. Existing state was retained for checked cleanup.";
            return false;
        }

        /// <summary>Restores the original stress write in the process that was patched. Retry on failure.</summary>
        public bool Disable()
        {
            lock (gate)
            {
                if (!SessionIsAlive()) { lastError = string.Empty; return true; }
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        bool busy = false;
                        using (var paused = RemoteCodeManager.SuspendThreads(targetProcess))
                        {
                            byte[] current = ReadInstruction();
                            if (BytesEqual(current, installedPatch))
                            {
                                // Each NOP is an instruction. Do not resume a thread in the middle
                                // of the eight-byte instruction after restoring it over those NOPs.
                                busy = paused.HasInstructionPointerInRange(
                                    MemoryManager.AddOffset(patchAddress, 1), originalInstruction.Length - 1);
                                if (!busy)
                                {
                                    try
                                    {
                                        RemoteCodeManager.ReplaceCode(processHandle, patchAddress, installedPatch, originalInstruction);
                                    }
                                    catch (RemoteCodePatchException ex)
                                    {
                                        enabled = ex.OriginalBytesRestored;
                                        patchMayBeInstalled = true;
                                        throw;
                                    }
                                }
                            }
                            else if (!BytesEqual(current, originalInstruction))
                            {
                                enabled = false;
                                throw new InvalidOperationException("Stress instruction bytes were changed externally; no bytes were overwritten. Restore the known bytes and retry Disable.");
                            }

                            if (!busy)
                            {
                                enabled = false;
                                patchMayBeInstalled = false;
                            }
                        }

                        if (!busy)
                        {
                            ForgetSession();
                            lastError = string.Empty;
                            LoggingManager.Instance.Log("Stress write restored.");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!SessionIsAlive()) { lastError = string.Empty; return true; }
                        return Fail("Stress patch disable failed: " + ex.Message);
                    }
                    // Resume all threads before allowing the NOP sequence to finish.
                    Thread.Sleep(10);
                }
                return Fail("A game thread is still inside the stress NOPs. The patch remains installed; retry Disable.");
            }
        }

        private byte[] ReadInstruction()
        {
            return MemoryManager.ReadMemoryBytes(processHandle, patchAddress, originalInstruction.Length);
        }

        private bool SessionIsAlive()
        {
            if (targetProcess == null) return false;
            if (!targetProcess.HasExited) return true;
            // An exited game's addresses must never be reused for a later process.
            ForgetSession();
            return false;
        }

        private void ForgetSession()
        {
            MemoryManager.CloseGameProcess(processHandle);
            targetProcess?.Dispose();
            targetProcess = null;
            processHandle = IntPtr.Zero;
            patchAddress = IntPtr.Zero;
            enabled = false;
            patchMayBeInstalled = false;
        }

        private bool Fail(string message)
        {
            if (message != lastError) LoggingManager.Instance.Log(message);
            lastError = message;
            return false;
        }

        private static bool BytesEqual(byte[] first, byte[] second)
        {
            return first != null && first.SequenceEqual(second);
        }
    }
}
