using System.Runtime.InteropServices;

namespace CodexVtt.App.WinUI;

/// <summary>Punto di ingresso WinUI: istanza singola e controllo del WebView2 Runtime.</summary>
public partial class App : MauiWinUIApplication
{
    private static Mutex? _singleInstance;

    public App()
    {
        // Hook di installazione/aggiornamento/disinstallazione di Velopack (ADR-0005). Non usa la rete.
        // Procedura guidata alla disinstallazione: chiede se eliminare anche modelli, log e impostazioni.
        Velopack.VelopackApp.Build().OnBeforeUninstallFastCallback(_ => UninstallWizard.Run()).Run();

        // Modalità senza GUI (issue #38): niente istanza singola né finestra; esce con il codice della modalità.
        var args = Environment.GetCommandLineArgs();
        if (CodexVtt.App.Headless.HeadlessCommands.IsHeadless(args))
            Environment.Exit(Task.Run(() => CodexVtt.App.Headless.HeadlessCommands.RunAsync(args)).GetAwaiter().GetResult());

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\CodexVtt.Desktop.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox(IntPtr.Zero, NativeText.Get("alreadyRunning", LoadSettings()), "CodexVTT", 0x40);
            Environment.Exit(0);
        }

        ReduceWebViewTraffic();

        if (!IsWebView2Available())
        {
            MessageBox(IntPtr.Zero, NativeText.Get("webView2Missing", LoadSettings()), "CodexVTT", 0x10);
            Environment.Exit(1);
        }

        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static CodexVtt.Core.Settings.AppSettings LoadSettings() =>
        new CodexVtt.Core.Settings.SettingsStore(Path.Combine(CodexVtt.Core.AppPaths.DefaultRoot, "settings.json")).Load();

    /// <summary>
    /// Il runtime WebView2 (Edge) fa di suo traffico di servizio (SmartScreen, servizi di configurazione,
    /// report di rete). Si spegne quello che si può dal processo dell'app; la navigazione resta limitata
    /// all'origine locale da <see cref="CodexVtt.App.WebViewSecurity"/> (ADR-0006).
    /// </summary>
    private static void ReduceWebViewTraffic()
    {
        const string name = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
        const string flags = "--disable-background-networking --no-pings --disable-component-update --disable-domain-reliability --disable-sync --disable-client-side-phishing-detection " +
                             "--disable-features=msSmartScreenProtection,msEdgeConfigService,NetworkErrorLogging,OptimizationHints,DnsOverHttps,AsyncDns " +
                             "--host-resolver-rules=\"MAP * ~NOTFOUND\"";
        var current = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(current) ? flags : current + " " + flags);
    }

    private static bool IsWebView2Available()
    {
        try
        {
            return !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
