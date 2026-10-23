using CodexVtt.Bridge;
using CodexVtt.Core.Errors;

namespace CodexVtt.Server;

/// <summary>
/// Shell della versione server: niente dialog nativi, perché il browser può essere su un altro PC.
/// I file audio arrivano con un caricamento HTTP e gli export si scaricano (vedi BridgeEndpoints);
/// la UI nasconde le azioni che qui non hanno senso.
/// </summary>
public sealed class ServerShellServices(ClientHub hub) : IShellServices
{
    public string Host => "server";

    public Task<string?> PickAudioFileAsync(CancellationToken ct) => throw Unavailable();

    public Task<IReadOnlyList<string>> PickAudioFilesAsync(CancellationToken ct) => throw Unavailable();

    public Task<string?> PickModelFileAsync(CancellationToken ct) => throw Unavailable();

    public Task<string?> PickSaveFileAsync(string suggestedName, string extension, CancellationToken ct) => throw Unavailable();

    public Task<string?> PickFolderAsync(CancellationToken ct) => throw Unavailable();

    public void OpenFolder(string path) => throw Unavailable();

    public void PostToUi(string json) => hub.Broadcast(json);

    private static CodexVttException Unavailable() =>
        new(ErrorCodes.NotAvailableOnServer, "Operazione disponibile solo nell'app desktop.");
}
