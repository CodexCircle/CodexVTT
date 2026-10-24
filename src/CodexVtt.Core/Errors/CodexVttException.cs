namespace CodexVtt.Core.Errors;

/// <summary>
/// Errore di dominio con un codice stabile, esposto alla UI attraverso il bridge.
/// I testi per l'utente sono nella UI (catalogo errori della specifica V1), non qui.
/// </summary>
public class CodexVttException : Exception
{
    public CodexVttException(string code, string message, IReadOnlyDictionary<string, string>? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Details = details ?? new Dictionary<string, string>();
    }

    public string Code { get; }

    public IReadOnlyDictionary<string, string> Details { get; }
}

/// <summary>Codici d'errore stabili (snake_case) condivisi con la UI.</summary>
public static class ErrorCodes
{
    public const string AudioNotFound = "audio_not_found";
    public const string AudioUnsupported = "audio_unsupported";
    public const string AudioCorrupted = "audio_corrupted";
    public const string AudioEmpty = "audio_empty";
    public const string AudioTooLong = "audio_too_long";
    public const string AudioCodecMissing = "audio_codec_missing";

    public const string ModelUnknown = "model_unknown";
    public const string ModelNotInstalled = "model_not_installed";
    public const string ModelNotVerified = "model_not_verified";
    public const string ModelHashMismatch = "model_hash_mismatch";
    public const string ModelLoadFailed = "model_load_failed";
    public const string ModelImportUnknown = "model_import_unknown";

    public const string InsufficientDisk = "insufficient_disk";
    public const string InsufficientMemory = "insufficient_memory";

    public const string DownloadFailed = "download_failed";
    public const string DownloadHostNotAllowed = "download_host_not_allowed";
    public const string DownloadInProgress = "download_in_progress";

    public const string RuntimeUnavailable = "runtime_unavailable";
    public const string TranscriptionFailed = "transcription_failed";
    public const string TranscriptionBusy = "transcription_busy";
    public const string TranscriptionNotFound = "transcription_not_found";
    public const string Cancelled = "cancelled";

    public const string ExportFailed = "export_failed";
    public const string HistoryNotFound = "history_not_found";
    public const string HistorySaveFailed = "history_save_failed";
    public const string HistoryFolderBlocked = "history_folder_blocked";
    public const string BenchmarkAudioInvalid = "benchmark_audio_invalid";
    public const string BenchmarkNothingSelected = "benchmark_nothing_selected";
    public const string FolderTestFailed = "folder_test_failed";
    public const string NotAvailableOnServer = "not_available_on_server";
    public const string LanguageRequired = "language_required";
    public const string EngineUnavailable = "engine_unavailable";
    public const string UpdateUnavailable = "update_unavailable";
    public const string UpdateFailed = "update_failed";
    public const string InvalidRequest = "invalid_request";
    public const string Internal = "internal_error";
}
