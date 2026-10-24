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
    long FreeDiskBytes,
    IReadOnlyList<string>? Gpus = null,
    IReadOnlyList<GpuDevice>? GpuDetails = null,
    bool GpuAcceleration = false)
{
    /// <summary>
    /// Memoria video della scheda dedicata più grande, se il driver la riporta. Null se non c'è una scheda dedicata
    /// o il valore non è affidabile: in quel caso la UI non mostra numeri.
    /// </summary>
    public long? GpuVramBytes => GpuDetails?.Where(g => g.VramBytes is > 0).Select(g => g.VramBytes).Max();

    /// <summary>
    /// Stesse condizioni con cui Whisper sceglie il runtime Vulkan: impostazione attiva, una scheda trovata e AVX2.
    /// Quando è vero i tempi dipendono dalla scheda video e il limite dei modelli dalla sua memoria.
    /// </summary>
    public HardwareProfile WithGpuAcceleration(bool useGpu) =>
        this with { GpuAcceleration = useGpu && GpuDetails is { Count: > 0 } && CpuFeatures.HasFlag(CpuFeatures.Avx2) };

    /// <summary>Il runtime whisper.cpp standard richiede AVX, AVX2, FMA e F16C su x86/x64 (ADR-0002).</summary>
    public bool SupportsStandardRuntime =>
        CpuFeatures.HasFlag(CpuFeatures.ArmSimd) ||
        CpuFeatures.HasFlag(CpuFeatures.Avx | CpuFeatures.Avx2 | CpuFeatures.Fma | CpuFeatures.F16C);
}

public interface IHardwareProbe
{
    HardwareProfile Probe(string modelsDirectory);
}
