using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>An explicitly known patched byte range, relative to the original AOB anchor.</summary>
    public sealed class AobRecoverySpan
    {
        public AobRecoverySpan(long offset, int length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (offset > long.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(offset));
            Offset = offset;
            Length = length;
        }
        public long Offset { get; }
        public int Length { get; }
    }

    /// <summary>
    /// Read-only recovery of original instruction locations from the loaded executable's PE file.
    /// A mapped address does not authorize a write: callers still validate the live patch and cave.
    /// </summary>
    public static class AobRecoveryManager
    {
        /// <summary>
        /// Scans only committed, readable executable pages inside the caller's exact range.
        /// Adjacent executable regions are merged so signatures spanning RX/RWX boundaries survive.
        /// </summary>
        public static List<IntPtr> ScanExecutable(IntPtr processHandle, IntPtr start, long size, byte[] pattern, string mask)
        {
            // Use the existing scanner's pattern validation before any native queries.
            MemoryManager.Instance.IsMatch(pattern, 0, pattern, mask);
            if (start.ToInt64() < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            long cursor = start.ToInt64(), end = checked(cursor + size);
            var matches = new List<IntPtr>();
            if (processHandle == IntPtr.Zero || size < pattern.Length) return matches;
            long? runStart = null;
            long runEnd = 0;
            Action flush = () =>
            {
                if (!runStart.HasValue) return;
                matches.AddRange(MemoryManager.Instance.ScanForAllAobInstances(processHandle, new IntPtr(runStart.Value),
                    runEnd - runStart.Value, pattern, mask));
                runStart = null;
            };
            UIntPtr informationSize = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryManager.NativeMethods.MemoryBasicInformation)));
            while (cursor < end)
            {
                if (MemoryManager.NativeMethods.VirtualQueryEx(processHandle, new IntPtr(cursor), out var region, informationSize) == UIntPtr.Zero)
                    throw new InvalidOperationException("Could not inspect the executable scan range.");
                if (region.BaseAddress.ToInt64() < 0)
                    throw new InvalidOperationException("An executable scan region has an invalid base address.");
                ulong nativeEnd = checked((ulong)region.BaseAddress.ToInt64() + region.RegionSize.ToUInt64());
                long regionEnd = (long)Math.Min((ulong)end, nativeEnd);
                if (regionEnd <= cursor) throw new InvalidOperationException("An executable scan region has invalid bounds.");
                // PAGE_EXECUTE alone is not readable under the existing scanner's contract.
                bool executable = region.State == 0x1000 && (region.Protect & (0x100 | 0x01)) == 0 &&
                    (region.Protect & (0x20 | 0x40 | 0x80)) != 0;
                if (executable)
                {
                    if (!runStart.HasValue) runStart = cursor;
                    runEnd = regionEnd;
                }
                else flush();
                cursor = regionEnd;
            }
            flush();
            return matches;
        }

        /// <summary>
        /// Cache the result in the owning process session. Non-module scan ranges return null.
        /// Main-module identity mismatches fail rather than using an unrelated executable on disk.
        /// </summary>
        public static AobRecoveryModule OpenModule(Process process, IntPtr processHandle, IntPtr scanStart, long scanSize)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (processHandle == IntPtr.Zero || MemoryManager.NativeMethods.GetProcessId(processHandle) != (uint)process.Id)
                throw new InvalidOperationException("The recovery handle does not identify the requested process.");
            ProcessModule module = process.MainModule;
            if (module == null) throw new InvalidOperationException("The loaded main module is unavailable.");
            if (module.BaseAddress != scanStart || module.ModuleMemorySize != scanSize) return null;
            Func<IntPtr, int, byte[]> reader = (address, count) => MemoryManager.ReadMemoryBytes(processHandle, address, count);
            // Deny concurrent writers while copying the headers, executable sections, and relocation table.
            using (var file = new FileStream(module.FileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                return AobRecoveryModule.Load(file, module.BaseAddress, module.ModuleMemorySize, reader);
        }

    }

    /// <summary>An immutable executable-section snapshot plus checked RVA-to-live-address mapping.</summary>
    public sealed class AobRecoveryModule
    {
        private readonly Func<IntPtr, int, byte[]> read;
        private readonly byte[] headers;
        private readonly Section[] executable;
        private readonly RvaSpan[] relocations;
        private readonly long[] byteFrequency = new long[256];
        private readonly Dictionary<string, IReadOnlyList<IntPtr>> locations = new Dictionary<string, IReadOnlyList<IntPtr>>(StringComparer.Ordinal);
        private readonly object gate = new object();

        private AobRecoveryModule(IntPtr loadedBase, uint imageSize, byte[] headers, Section[] executable,
            RvaSpan[] relocations, Func<IntPtr, int, byte[]> read)
        {
            BaseAddress = loadedBase;
            ImageSize = imageSize;
            this.headers = headers;
            this.executable = executable;
            this.relocations = relocations;
            this.read = read;
            foreach (Section section in executable)
                foreach (byte value in section.Bytes) byteFrequency[value]++;
        }

        public IntPtr BaseAddress { get; }
        public long ImageSize { get; }

        /// <summary>
        /// Searches the original executable sections once per pattern. This works even when a live
        /// jump or NOP patch has overwritten every fixed byte of the signature.
        /// </summary>
        public IReadOnlyList<IntPtr> FindOriginal(byte[] pattern, string mask)
        {
            string normalized = Normalize(pattern, mask);
            string key = BitConverter.ToString(pattern) + ":" + normalized;
            lock (gate)
            {
                if (locations.TryGetValue(key, out IReadOnlyList<IntPtr> cached)) return cached;
                var found = new List<IntPtr>();
                int anchor = normalized.IndexOf('x');
                if (anchor < 0) throw new ArgumentException("Recovery needs at least one fixed signature byte.", nameof(mask));
                // Reuse the snapshot histogram so common x64 prefixes do not become millions
                // of unnecessary full-pattern comparisons for every effect.
                for (int index = anchor + 1; index < pattern.Length; index++)
                    if (normalized[index] == 'x' && byteFrequency[pattern[index]] < byteFrequency[pattern[anchor]]) anchor = index;
                foreach (Section section in executable)
                {
                    int maximum = section.Bytes.Length - pattern.Length;
                    int cursor = anchor;
                    while (cursor <= maximum + anchor)
                    {
                        int match = Array.IndexOf(section.Bytes, pattern[anchor], cursor, maximum + anchor - cursor + 1);
                        if (match < 0) break;
                        int offset = match - anchor;
                        bool equal = true;
                        for (int index = 0; index < pattern.Length; index++)
                            if (normalized[index] == 'x' && section.Bytes[offset + index] != pattern[index]) { equal = false; break; }
                        if (equal) found.Add(Add(BaseAddress, section.Rva + (long)offset));
                        cursor = match + 1;
                    }
                }
                cached = Array.AsReadOnly(found.Distinct().OrderBy(address => address.ToInt64()).ToArray());
                locations.Add(key, cached);
                return cached;
            }
        }

        /// <summary>
        /// Compares surrounding live code to the same file image, excluding only caller-listed patches
        /// and PE loader relocations. The patch bytes themselves remain the caller's responsibility.
        /// </summary>
        public bool ValidateContext(IntPtr anchor, IReadOnlyList<AobRecoverySpan> patches, out string error, int radius = 64)
        {
            try
            {
                if (radius < 32 || radius > 4096) throw new ArgumentOutOfRangeException(nameof(radius));
                VerifyHeaders();
                long rva = checked(anchor.ToInt64() - BaseAddress.ToInt64());
                Section section = executable.SingleOrDefault(candidate => rva >= candidate.Rva && rva < candidate.Rva + candidate.Bytes.LongLength);
                if (section == null) throw new InvalidOperationException("The recovery anchor is outside a file-backed executable section.");
                long start = Math.Max(section.Rva, rva - radius);
                long end = Math.Min(section.Rva + section.Bytes.LongLength, checked(rva + radius));
                byte[] current = ReadExact(Add(BaseAddress, start), checked((int)(end - start)));
                RvaSpan[] nearbyRelocations = relocations.Where(relocation => relocation.Start < end &&
                    relocation.Start + relocation.Length > start).ToArray();
                int compared = 0;
                for (int index = 0; index < current.Length; index++)
                {
                    long at = start + index;
                    long relative = at - rva;
                    if (patches != null && patches.Any(patch => relative >= patch.Offset && relative < patch.Offset + patch.Length)) continue;
                    if (nearbyRelocations.Any(relocation => at >= relocation.Start && at - relocation.Start < relocation.Length)) continue;
                    if (current[index] != section.Bytes[checked((int)(at - section.Rva))])
                        throw new InvalidOperationException("Live executable context differs from the original file at RVA 0x" + at.ToString("X") + ".");
                    compared++;
                }
                if (compared < 32) throw new InvalidOperationException("Too little unchanged context remains to identify the original instruction location.");
                error = string.Empty;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private void VerifyHeaders()
        {
            VerifyHeaderIdentity(headers, ReadExact(BaseAddress, headers.Length), BaseAddress);
        }

        private static void VerifyHeaderIdentity(byte[] fileHeaders, byte[] loadedHeaders, IntPtr loadedBase)
        {
            if (loadedHeaders == null || loadedHeaders.Length != fileHeaders.Length)
                throw new InvalidOperationException("Could not read the complete loaded PE headers for recovery.");
            // The running MGS4 image rewrites OptionalHeader.ImageBase to its ASLR load address.
            // Accept only that known normalization or the original preferred base. Every other
            // header byte (including all data directories and section descriptors) still matches.
            int imageBaseOffset = checked((int)U32(fileHeaders, 0x3C) + 24 + 24);
            ulong originalBase = BitConverter.ToUInt64(fileHeaders, imageBaseOffset);
            ulong declaredLoadedBase = BitConverter.ToUInt64(loadedHeaders, imageBaseOffset);
            if (declaredLoadedBase != originalBase && declaredLoadedBase != (ulong)loadedBase.ToInt64())
                throw new InvalidOperationException("The loaded PE ImageBase is neither the original preferred base nor the actual module base.");
            for (int index = 0; index < fileHeaders.Length; index++)
            {
                if (index >= imageBaseOffset && index < imageBaseOffset + 8) continue;
                if (fileHeaders[index] != loadedHeaders[index])
                    throw new InvalidOperationException("The on-disk executable header differs from the loaded module at offset 0x" + index.ToString("X") + ".");
            }
        }

        private byte[] ReadExact(IntPtr address, int count)
        {
            byte[] bytes = read(address, count);
            if (bytes == null || bytes.Length != count) throw new InvalidOperationException("Could not read the loaded executable for recovery validation.");
            return bytes;
        }

        internal static AobRecoveryModule Load(Stream file, IntPtr loadedBase, long? expectedSize, Func<IntPtr, int, byte[]> reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (loadedBase.ToInt64() <= 0) throw new ArgumentOutOfRangeException(nameof(loadedBase));
            byte[] dos = ReadFile(file, 0, 64);
            if (U16(dos, 0) != 0x5A4D) throw new InvalidOperationException("The module file is not a PE executable.");
            uint pe = U32(dos, 0x3C);
            if (pe < 64 || pe > 1024 * 1024) throw new InvalidOperationException("The PE header offset is invalid.");
            byte[] coff = ReadFile(file, pe, 24);
            if (U32(coff, 0) != 0x00004550 || U16(coff, 4) != 0x8664)
                throw new InvalidOperationException("Recovery requires an x64 PE executable.");
            int count = U16(coff, 6), optionalLength = U16(coff, 20);
            if (count <= 0 || count > 96 || optionalLength < 240 || optionalLength > 4096)
                throw new InvalidOperationException("The PE section/optional-header layout is invalid.");
            byte[] optional = ReadFile(file, pe + 24L, optionalLength);
            if (U16(optional, 0) != 0x20B) throw new InvalidOperationException("Recovery requires PE32+ headers.");
            uint imageSize = U32(optional, 56), headerSize = U32(optional, 60);
            long sectionTable = pe + 24L + optionalLength;
            if (imageSize == 0 || headerSize < sectionTable + count * 40L || headerSize > 1024 * 1024 ||
                expectedSize.HasValue && expectedSize.Value != imageSize)
                throw new InvalidOperationException("The file image size or headers do not match the loaded module.");
            if (loadedBase.ToInt64() > long.MaxValue - imageSize) throw new InvalidOperationException("The loaded image address range is invalid.");
            byte[] identity = ReadFile(file, 0, checked((int)headerSize));
            byte[] remoteIdentity = reader(loadedBase, identity.Length);
            VerifyHeaderIdentity(identity, remoteIdentity, loadedBase);

            byte[] table = ReadFile(file, sectionTable, checked(count * 40));
            var sections = new List<Section>();
            for (int index = 0; index < count; index++)
            {
                int offset = index * 40;
                uint virtualSize = U32(table, offset + 8), rva = U32(table, offset + 12);
                uint rawSize = U32(table, offset + 16), raw = U32(table, offset + 20), flags = U32(table, offset + 36);
                long mappedSize = Math.Max((long)virtualSize, rawSize);
                if ((long)rva + mappedSize > imageSize || (long)raw + rawSize > file.Length || rawSize > int.MaxValue)
                    throw new InvalidOperationException("A PE section extends outside its image or file.");
                if (mappedSize != 0 && sections.Any(previous => rva < previous.Rva + previous.MappedSize && previous.Rva < (long)rva + mappedSize))
                    throw new InvalidOperationException("Overlapping PE sections cannot be used for recovery.");
                sections.Add(new Section { Rva = rva, Raw = raw, RawSize = rawSize, MappedSize = mappedSize,
                    Bytes = (flags & 0x20000000) != 0 && rawSize != 0 ? ReadFile(file, raw, (int)rawSize) : null });
            }
            var fixups = new List<RvaSpan>();
            if (U32(optional, 108) > 5)
            {
                uint relocationRva = U32(optional, 112 + 5 * 8), relocationSize = U32(optional, 116 + 5 * 8);
                if (relocationRva != 0 && relocationSize != 0)
                {
                    byte[] directory = ReadRva(file, sections, relocationRva, relocationSize);
                    int offset = 0;
                    while (offset < directory.Length)
                    {
                        if (directory.Length - offset < 8) throw new InvalidOperationException("Truncated PE relocation block.");
                        uint page = U32(directory, offset), blockSize = U32(directory, offset + 4);
                        if (blockSize < 8 || blockSize > directory.Length - offset || (blockSize & 1) != 0)
                            throw new InvalidOperationException("Invalid PE relocation block size.");
                        for (int item = offset + 8; item < offset + blockSize; item += 2)
                        {
                            ushort entry = U16(directory, item);
                            int type = entry >> 12;
                            if (type == 0) continue;
                            int width = type == 10 ? 8 : type == 3 ? 4 : 0;
                            long location = page + (long)(entry & 0xFFF);
                            if (width == 0 || location + width > imageSize)
                                throw new InvalidOperationException("Unsupported or out-of-range PE loader relocation.");
                            fixups.Add(new RvaSpan { Start = location, Length = width });
                        }
                        offset += checked((int)blockSize);
                    }
                }
            }
            var result = new AobRecoveryModule(loadedBase, imageSize, identity,
                sections.Where(section => section.Bytes != null).ToArray(), fixups.ToArray(), reader);
            if (result.executable.Length == 0) throw new InvalidOperationException("The PE file has no file-backed executable sections.");
            return result;
        }

        private static byte[] ReadRva(Stream file, List<Section> sections, uint rva, uint size)
        {
            Section section = sections.SingleOrDefault(candidate => rva >= candidate.Rva && (long)rva + size <= candidate.Rva + candidate.RawSize);
            if (section == null || size > int.MaxValue) throw new InvalidOperationException("The PE relocation directory is not file-backed.");
            return ReadFile(file, section.Raw + ((long)rva - section.Rva), checked((int)size));
        }

        private static byte[] ReadFile(Stream file, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset > file.Length - count) throw new InvalidOperationException("The PE file is truncated or malformed.");
            file.Position = offset;
            var bytes = new byte[count];
            int copied = 0;
            while (copied < count)
            {
                int read = file.Read(bytes, copied, count - copied);
                if (read == 0) throw new InvalidOperationException("The PE file changed or ended while being read.");
                copied += read;
            }
            return bytes;
        }

        private static string Normalize(byte[] pattern, string mask)
        {
            if (pattern == null || pattern.Length == 0) throw new ArgumentException("The recovery signature must not be empty.", nameof(pattern));
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            string normalized = new string(mask.Where(character => !char.IsWhiteSpace(character)).Select(char.ToLowerInvariant).ToArray());
            if (normalized.Length != pattern.Length || normalized.Any(character => character != 'x' && character != '?'))
                throw new ArgumentException("Use one x or ? per recovery signature byte.", nameof(mask));
            return normalized;
        }

        private static IntPtr Add(IntPtr address, long offset) => new IntPtr(checked(address.ToInt64() + offset));
        private static ushort U16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);
        private static uint U32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
        private sealed class Section
        {
            internal long Rva, Raw, RawSize, MappedSize;
            internal byte[] Bytes;
        }
        private sealed class RvaSpan
        {
            internal long Start;
            internal int Length;
        }
    }
}
