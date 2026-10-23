using Microsoft.Extensions.Logging;

namespace CodexVtt.Core.Logging;

/// <summary>
/// Opzioni diagnostiche (issue #38). Precedenza: argomenti da riga di comando, poi variabili d'ambiente,
/// poi <c>DiagnosticLogLevel</c> in settings.json, poi il default (Information).
/// </summary>
/// <param name="MinimumLevel">Livello minimo scritto nel log su file.</param>
/// <param name="DebugMode">Modalità debug: log dettagliati della WebView e dei comandi del bridge.</param>
public sealed record DiagnosticOptions(LogLevel MinimumLevel, bool DebugMode)
{
    public const string LogLevelVariable = "CODEXVTT_LOG_LEVEL";
    public const string DebugVariable = "CODEXVTT_DEBUG";
    public const string DebugArgument = "--debug";

    public static DiagnosticOptions Default { get; } = new(LogLevel.Information, DebugMode: false);

    /// <param name="args">Argomenti della riga di comando.</param>
    /// <param name="environment">Lettura delle variabili d'ambiente (iniettabile per i test).</param>
    /// <param name="settingsLevel">Valore di <c>DiagnosticLogLevel</c> in settings.json, se presente.</param>
    public static DiagnosticOptions Resolve(IEnumerable<string> args, Func<string, string?> environment, string? settingsLevel = null)
    {
        var settingsDebug = ParseLevel(settingsLevel) is LogLevel.Trace or LogLevel.Debug;
        var debug = args.Any(a => string.Equals(a, DebugArgument, StringComparison.OrdinalIgnoreCase))
                    || IsTrue(environment(DebugVariable))
                    || settingsDebug;

        var level = ParseLevel(environment(LogLevelVariable))
                    ?? ParseLevel(settingsLevel)
                    ?? (debug ? LogLevel.Debug : LogLevel.Information);

        return new DiagnosticOptions(level, debug);
    }

    /// <summary>Accetta i nomi di <see cref="LogLevel"/> senza distinzione di maiuscole; null se non valido.</summary>
    public static LogLevel? ParseLevel(string? value) =>
        Enum.TryParse<LogLevel>(value?.Trim(), ignoreCase: true, out var level) && level != LogLevel.None
            ? level
            : null;

    private static bool IsTrue(string? value) =>
        value?.Trim() is "1" or "true" or "TRUE" or "True";
}
