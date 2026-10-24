using System.Text.Json;
using CodexVtt.Core.Hardware;
using CodexVtt.Core.Models;

namespace CodexVtt.Core.Benchmark;

/// <summary>
/// Misure di velocità in benchmark.json nella cartella dati, accanto a settings.json: sono dati misurati, non preferenze.
/// Non escono mai dal PC (ADR-0006). Le misure si invalidano da sole se cambia l'hardware, il modello o il runtime.
/// </summary>
public sealed class BenchmarkStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _gate = new();

    public BenchmarkStore(string path) => _path = path;

    /// <summary>Impronta dell'hardware: se cambia (nuovo PC, nuova scheda, GPU attivata o no) le misure vecchie non valgono.</summary>
    public static string Fingerprint(HardwareProfile hardware) =>
        string.Join('|',
            hardware.Architecture,
            hardware.LogicalCores,
            (int)hardware.CpuFeatures,
            hardware.TotalMemoryBytes / (1L << 30),
            hardware.GpuAcceleration ? "gpu" : "cpu",
            string.Join(',', (hardware.GpuDetails?.Select(g => g.Name) ?? hardware.Gpus ?? []).Order(StringComparer.Ordinal)));

    /// <summary>Identità del file del modello: l'hash fissato dal catalogo, altrimenti nome e dimensione.</summary>
    public static string ModelIdentity(ModelDescriptor model) => model.Sha256 ?? $"{model.File}:{model.SizeBytes}";

    public BenchmarkFile Read()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                    return BenchmarkFile.Empty;
                var file = JsonSerializer.Deserialize<BenchmarkFile>(File.ReadAllText(_path), Options);
                return file is { Version: BenchmarkFile.CurrentVersion } ? file : BenchmarkFile.Empty;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // File illeggibile: le misure si rifanno, nessun errore per l'utente.
                return BenchmarkFile.Empty;
            }
        }
    }

    /// <summary>
    /// Salva una misura. Se l'impronta dell'hardware è diversa da quella del file le misure precedenti vengono scartate;
    /// per lo stesso modello, runtime e brano la nuova misura sostituisce la vecchia.
    /// </summary>
    public void Record(string fingerprint, BenchmarkEntry entry)
    {
        lock (_gate)
        {
            var current = Read();
            var kept = current.Fingerprint == fingerprint
                ? current.Entries.Where(e => !(e.ModelId == entry.ModelId && e.Runtime == entry.Runtime && e.ClipId == entry.ClipId)).ToList()
                : [];
            kept.Add(entry);
            Write(new BenchmarkFile(BenchmarkFile.CurrentVersion, fingerprint, kept));
        }
    }

    /// <summary>Cancella tutte le misure: un nuovo test sostituisce quelli precedenti, anche di altri runtime o versioni.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    /// <summary>Tutte le misure con il loro stato rispetto all'hardware, ai modelli installati e alla versione dell'app.</summary>
    public IReadOnlyList<BenchmarkView> Views(string fingerprint, Func<string, string?> identityOf, string appVersion)
    {
        var file = Read();
        return file.Entries.Select(e => new BenchmarkView(e, Classify(file, fingerprint, identityOf, appVersion, e))).ToList();
    }

    /// <summary>
    /// La misura valida per un modello, se c'è: con più brani o più runtime vale il runtime della misura più recente e i numeri
    /// sono combinati sui suoi brani (<see cref="Combine"/>).
    /// </summary>
    public BenchmarkEntry? Lookup(string fingerprint, string modelId, Func<string, string?> identityOf, string appVersion) =>
        Combine(Views(fingerprint, identityOf, appVersion).Where(v => v.IsValid && v.Entry.ModelId == modelId).Select(v => v.Entry).ToList());

    /// <summary>
    /// Riassume le misure di un modello in una sola: stesso runtime della più recente, velocità sul totale dell'audio e tempo
    /// fisso medio. L'affidabilità non entra qui (dipende dalla lingua, vedi <see cref="BenchmarkReliability"/>).
    /// </summary>
    public static BenchmarkEntry? Combine(IReadOnlyList<BenchmarkEntry> entries)
    {
        if (entries.Count == 0)
            return null;
        var newest = entries.OrderByDescending(e => e.MeasuredAt).First();
        var same = entries.Where(e => e.Runtime == newest.Runtime && e.Speed > 0).ToList();
        if (same.Count == 0)
            return newest;
        var audio = same.Sum(e => e.AudioSeconds);
        var work = same.Sum(e => e.AudioSeconds / e.Speed);
        return newest with
        {
            AudioSeconds = audio,
            LoadSeconds = same.Average(e => e.LoadSeconds),
            Speed = work > 0 ? audio / work : newest.Speed,
            Accuracy = null,
            ClipId = null,
            Language = null,
            Scenario = null,
        };
    }

    private static BenchmarkStaleReason Classify(BenchmarkFile file, string fingerprint, Func<string, string?> identityOf, string appVersion, BenchmarkEntry entry)
    {
        if (file.Fingerprint != fingerprint)
            return BenchmarkStaleReason.HardwareChanged;
        if (identityOf(entry.ModelId) != entry.ModelIdentity)
            return BenchmarkStaleReason.ModelChanged;
        return entry.AppVersion == appVersion ? BenchmarkStaleReason.None : BenchmarkStaleReason.RuntimeChanged;
    }

    private void Write(BenchmarkFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
