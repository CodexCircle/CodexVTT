using CodexVtt.Audio.Windows;
using CodexVtt.Bridge;
using Microsoft.Extensions.Logging;
using CodexVtt.Core.Settings;

namespace CodexVtt.App;

/// <summary>Dialog nativi di Windows e canale degli eventi verso la UI.</summary>
public sealed class WindowsShellServices(SettingsStore settings, ILogger<WindowsShellServices> logger) : IShellServices
{
    private HybridWebView? _webView;

    public void Attach(HybridWebView webView) => _webView = webView;

    public async Task<string?> PickAudioFileAsync(CancellationToken ct)
    {
        // Diagnostica del selettore audio (issue #40): senza percorso file nel log.
        logger.LogDebug("Selettore audio: apertura");
        var picked = await PickAsync(NativeText.Get("pickAudio", settings.Load()), MediaFoundationDecoder.SupportedExtensions);
        logger.LogDebug("Selettore audio: chiuso ({Result})", picked is null ? "annullato" : "file scelto");
        return picked;
    }

    public async Task<IReadOnlyList<string>> PickAudioFilesAsync(CancellationToken ct)
    {
        logger.LogDebug("Selettore audio multiplo: apertura");
        var picked = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = NativeText.Get("pickAudio", settings.Load()),
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = MediaFoundationDecoder.SupportedExtensions,
                }),
            });
            return (IReadOnlyList<string>)(results?.Select(r => r.FullPath).ToList() ?? new List<string>());
        });
        logger.LogDebug("Selettore audio multiplo: chiuso ({Count} file)", picked.Count);
        return picked;
    }

    public Task<string?> PickModelFileAsync(CancellationToken ct) =>
        PickAsync(NativeText.Get("pickModel", settings.Load()), new HashSet<string> { ".bin" });

    public Task<string?> PickSaveFileAsync(string suggestedName, string extension, CancellationToken ct) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
#if WINDOWS
            var picker = new global::Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
                SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), [extension]);
            AttachToWindow(picker);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
#else
            await Task.CompletedTask;
            return (string?)null;
#endif
        });

    public Task<string?> PickFolderAsync(CancellationToken ct) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
#if WINDOWS
            var picker = new global::Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add("*");
            AttachToWindow(picker);
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
#else
            await Task.CompletedTask;
            return (string?)null;
#endif
        });

    /// <summary>Apre la cartella in Esplora file (processo locale, nessuna rete).</summary>
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            return;
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { path },
            UseShellExecute = false,
        });
    }

#if WINDOWS
    private static void AttachToWindow(object picker)
    {
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is not null)
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
    }
#endif

    public void PostToUi(string json)
    {
        var webView = _webView;
        if (webView is not null)
            MainThread.BeginInvokeOnMainThread(() => webView.SendRawMessage(json));
    }

    private static Task<string?> PickAsync(string title, IReadOnlySet<string> extensions) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = title,
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = extensions,
                }),
            });
            return result?.FullPath;
        });
}
