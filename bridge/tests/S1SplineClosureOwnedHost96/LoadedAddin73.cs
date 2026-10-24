using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion73
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public UIntPtr RegionSize;
        public uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(SafeProcessHandle process, IntPtr address, out MemoryRegion73 region, UIntPtr size);
    [DllImport("psapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetMappedFileNameW(SafeProcessHandle process, IntPtr address, StringBuilder name, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string name, StringBuilder target, int capacity);

    private static JObject VerifyLoadedAddin73()
    {
        GuardProcess73();
        string? method = null;
        using (var process = Process.GetProcessById(OwnedPid))
        {
            try
            {
                foreach (ProcessModule module in process.Modules)
                    if (SamePath73(module.FileName, Settings.ExpectedAddinPath)) { method = "owned_process_module_path"; break; }
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        if (method == null && HasMappedAddin73()) method = "owned_process_exact_mapped_file_path";
        if (method == null) throw new HostFailure73("Expected managed add-in mapping is unobservable; refusing fixture preparation.");
        string hash = HashFile73(Settings.ExpectedAddinPath);
        if (hash != Settings.ExpectedAddinSha) throw new HostFailure73("Mapped add-in file SHA differs from the expected stage.");
        GuardProcess73();
        return new JObject { ["path"] = Settings.ExpectedAddinPath, ["sha256"] = hash, ["pid"] = OwnedPid,
            ["verification"] = method + "_plus_disk_sha256", ["memory_bytes_hashed"] = false };
    }

    private static bool HasMappedAddin73()
    {
        // Read-only, bounded address-region metadata. No ReadProcessMemory, raw memory,
        // unknown module list, other process or guessed fallback path is exposed.
        // Win32 layouts/signatures: VirtualQueryEx / MEMORY_BASIC_INFORMATION /
        // GetMappedFileNameW / QueryDosDeviceW on learn.microsoft.com/windows/win32/api.
        GuardProcess73();
        string expected = Settings.ExpectedAddinPath;
        string root = Path.GetPathRoot(expected) ?? "";
        if (root.Length != 3 || root[1] != ':') return false; // Stage must have an exact local drive mapping for this fallback.
        var device = new StringBuilder(32768);
        if (QueryDosDeviceW(root[..2], device, device.Capacity) == 0) return false;
        string expectedDevice = device.ToString().Split('\0')[0].TrimEnd('\\') + "\\" + expected[root.Length..];
        using var process = OpenProcess(0x0400 | 0x0010, false, OwnedPid); // QUERY_INFORMATION | VM_READ required by GetMappedFileName.
        if (process.IsInvalid) return false;
        var watch = Stopwatch.StartNew(); ulong address = 0;
        var name = new StringBuilder(32768);
        int size = Marshal.SizeOf<MemoryRegion73>();
        if (IntPtr.Size != 8 || size != 48) throw new HostFailure73("Unexpected x64 memory metadata layout.");
        for (int count = 0; count < 100000 && watch.Elapsed < TimeSpan.FromSeconds(10); count++)
        {
            var read = VirtualQueryEx(process, new IntPtr(unchecked((long)address)), out var region, (UIntPtr)size);
            if (read == UIntPtr.Zero) return false;
            ulong start = unchecked((ulong)region.BaseAddress.ToInt64()), length = region.RegionSize.ToUInt64();
            if (length == 0 || start > ulong.MaxValue - length || start + length <= address) return false;
            if (region.State == 0x1000 && region.Type is 0x1000000 or 0x40000)
            {
                name.Clear();
                uint chars = GetMappedFileNameW(process, region.BaseAddress, name, (uint)name.Capacity);
                if (chars > 0 && chars < name.Capacity && string.Equals(name.ToString(), expectedDevice, StringComparison.OrdinalIgnoreCase)) return true;
            }
            address = start + length;
        }
        return false;
    }
}
