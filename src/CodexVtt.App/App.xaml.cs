using Microsoft.Extensions.Logging;
using CodexVtt.Core.Logging;
using CodexVtt.Core.Transcription;

namespace CodexVtt.App;

public partial class App : Application
{
    private readonly MainPage _page;
    private readonly TranscriptionService _transcription;
    private readonly ILogger<App> _logger;

    public App(MainPage page, TranscriptionService transcription, DiagnosticOptions diagnostics, ILogger<App> logger)
    {
        InitializeComponent();
        _page = page;
        _transcription = transcription;
        _logger = logger;
        _logger.LogInformation("Avvio CodexVTT {Version}; livello log {Level}; modalità debug {Debug}",
            typeof(App).Assembly.GetName().Version, diagnostics.MinimumLevel, diagnostics.DebugMode);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_page)
        {
            Title = "CodexVTT",
            MinimumWidth = 720,
            MinimumHeight = 560,
            Width = 1100,
            Height = 820,
        };

        // Chiusura con una trascrizione in corso: si annulla il job e si liberano le risorse native.
        window.Destroying += (_, _) => _transcription.Dispose();

        // Eventi di ciclo di vita della finestra: utili per diagnosticare schermate vuote o bloccate (issue #38).
        window.Activated += (_, _) => { _logger.LogDebug("Finestra attivata"); _page.LogLayout("attivazione"); };
        window.Deactivated += (_, _) => _logger.LogDebug("Finestra disattivata");
        window.Stopped += (_, _) => { _logger.LogInformation("Finestra fermata"); _page.LogLayout("fermata"); };
        window.Resumed += (_, _) => { _logger.LogInformation("Finestra ripresa"); _page.LogLayout("ripresa"); };
        window.Destroying += (_, _) => _logger.LogInformation("Finestra in chiusura");
        return window;
    }
}
