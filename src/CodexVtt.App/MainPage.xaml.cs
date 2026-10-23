using Microsoft.Extensions.Logging;
using CodexVtt.Bridge;

namespace CodexVtt.App;

/// <summary>Ospita la UI React in HybridWebView e inoltra i messaggi al bridge (ADR-0001).</summary>
public partial class MainPage : ContentPage
{
    private readonly BridgeDispatcher _dispatcher;
    private readonly ILogger<MainPage> _logger;

    public MainPage(BridgeDispatcher dispatcher, WindowsShellServices shell, ILogger<MainPage> logger)
    {
        InitializeComponent();
        _dispatcher = dispatcher;
        _logger = logger;
        shell.Attach(WebView);
        WebView.SizeChanged += (_, _) => LogLayout("dimensione");
    }

    /// <summary>
    /// Registra dimensioni MAUI e del controllo nativo della WebView (issue #40). Livello Debug:
    /// serve a trovare il momento in cui la WebView collassa a 0×0.
    /// </summary>
    public void LogLayout(string reason)
    {
#if WINDOWS
        var native = WebView.Handler?.PlatformView as Microsoft.UI.Xaml.FrameworkElement;
        _logger.LogDebug("Layout WebView ({Reason}): MAUI {Width}x{Height}; nativo {NativeWidth}x{NativeHeight}; visibilità {Visibility}",
            reason, WebView.Width, WebView.Height, native?.ActualWidth ?? -1, native?.ActualHeight ?? -1, native?.Visibility);
#else
        _logger.LogDebug("Layout WebView ({Reason}): MAUI {Width}x{Height}", reason, WebView.Width, WebView.Height);
#endif
    }

    private async void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Message))
            return;
        _logger.LogDebug("Messaggio dalla UI ricevuto ({Length} caratteri)", e.Message.Length);

        try
        {
            var response = await _dispatcher.HandleAsync(e.Message);
            await MainThread.InvokeOnMainThreadAsync(() => WebView.SendRawMessage(response));
        }
        catch (Exception ex)
        {
            // HandleAsync converte già gli errori in risposte; qui arrivano solo errori del canale.
            _logger.LogError(ex, "Errore nel canale HybridWebView");
        }
    }
}
