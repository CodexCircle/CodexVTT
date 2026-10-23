using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace CodexVtt.Core.Hardware;

/// <summary>Rilevazione locale delle capacità del PC. Non usa la rete.</summary>
public sealed partial class HardwareProbe : IHardwareProbe
{
    public HardwareProfile Probe(string modelsDirectory)
    {
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return new HardwareProfile(
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            total,
            AvailableMemory() ?? total,
            DetectCpuFeatures(),
            FreeDisk(modelsDirectory),
            GpuProbe.Devices.Select(g => g.Name).ToList());
    }

    public static CpuFeatures DetectCpuFeatures()
    {
        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
            return CpuFeatures.ArmSimd;
        if (!X86Base.IsSupported)
            return CpuFeatures.None;

        var features = CpuFeatures.None;
        if (Avx.IsSupported) features |= CpuFeatures.Avx;
        if (Avx2.IsSupported) features |= CpuFeatures.Avx2;
        if (Fma.IsSupported) features |= CpuFeatures.Fma;
        if (Avx512F.IsSupported) features |= CpuFeatures.Avx512;

        // F16C non ha una classe di intrinsics in .NET: CPUID leaf 1, ECX bit 29.
        var (_, _, ecx, _) = X86Base.CpuId(1, 0);
        if ((ecx & (1 << 29)) != 0) features |= CpuFeatures.F16C;
        return features;
    }

    private static long FreeDisk(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            return root is null ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private static long? AvailableMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : null;
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return long.TryParse(parts[1], out var kb) ? kb * 1024 : null;
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
