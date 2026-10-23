namespace CodexVtt.Core.Hardware;

[Flags]
public enum CpuFeatures
{
    None = 0,
    Avx = 1,
    Avx2 = 2,
    Fma = 4,
    F16C = 8,
    Avx512 = 16,
    ArmSimd = 32,
}

/// <summary>Capacità del PC rilevate per la selezione del modello (#4).</summary>
public sealed record HardwareProfile(
    string OperatingSystem,
    string Architecture,
    int LogicalCores,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    CpuFeatures CpuFeatures,
    long FreeDiskBytes)
{
    /// <summary>Il runtime whisper.cpp standard richiede AVX, AVX2, FMA e F16C su x86/x64 (ADR-0002).</summary>
    public bool SupportsStandardRuntime =>
        CpuFeatures.HasFlag(CpuFeatures.ArmSimd) ||
        CpuFeatures.HasFlag(CpuFeatures.Avx | CpuFeatures.Avx2 | CpuFeatures.Fma | CpuFeatures.F16C);
}

public interface IHardwareProbe
{
    HardwareProfile Probe(string modelsDirectory);
}
