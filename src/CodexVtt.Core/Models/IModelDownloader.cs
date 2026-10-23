namespace CodexVtt.Core.Models;

public sealed record DownloadProgress(string ModelId, long ReceivedBytes, long TotalBytes, string Host);

/// <summary>
/// Download di un modello del catalogo. L'implementazione (CodexVtt.Models.Download) è l'unico
/// componente con accesso alla rete; il resto dell'app dipende solo da questa interfaccia.
/// </summary>
public interface IModelDownloader
{
    Task<InstalledModelStatus> DownloadAsync(string modelId, IProgress<DownloadProgress>? progress, CancellationToken ct);
}
