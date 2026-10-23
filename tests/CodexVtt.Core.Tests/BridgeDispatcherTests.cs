using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using CodexVtt.Bridge;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;
using CodexVtt.Core.History;
using CodexVtt.Core.Models;
using CodexVtt.Core.Settings;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Tests;

public sealed class BridgeDispatcherTests : IDisposable
{
    private static readonly byte[] ModelContent = TestData.Bytes("bridge-model", 50);
    private readonly TempDir _dir = new();
    private readonly FakeShell _shell = new();
    private readonly ModelStore _store;
    private readonly TranscriptionService _transcription;
    private readonly BridgeDispatcher _dispatcher;
    private int _nextId;

    public BridgeDispatcherTests()
    {
        var paths = _dir.AppPaths();
        _store = new ModelStore(paths, TestData.Catalog(models: [("base", ModelContent, 2, 2, 0), ("small", TestData.Bytes("s"), 6, 3, 1)]));
        var audio = new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(3));
        _transcription = new TranscriptionService(_store, audio, new EchoEngine(), NullLogger<TranscriptionService>.Instance);
        _dispatcher = new BridgeDispatcher(paths, new SettingsStore(paths.SettingsFile), _store, new NoDownloads(),
            new FakeHardware(TestData.Hardware()), audio, _transcription, _shell, new HistoryLocation(HistoryDir), NullLogger<BridgeDispatcher>.Instance);
    }

    private string HistoryDir => Path.Combine(_dir.Path, "Documenti", "CodexVTT", "history");

    public void Dispose()
    {
        _dispatcher.Dispose();
        _transcription.Dispose();
        _dir.Dispose();
    }

    private async Task<JsonElement> Call(string type, object? payload = null, int version = BridgeProtocol.Version)
    {
        var id = $"c-{++_nextId}";
        var json = JsonSerializer.Serialize(new { v = version, id, type, payload }, BridgeProtocol.JsonOptions);
        var response = JsonDocument.Parse(await _dispatcher.HandleAsync(json)).RootElement;
        Assert.Equal(id, response.GetProperty("id").GetString());
        Assert.Equal("result", response.GetProperty("type").GetString());
        return response;
    }

    private static JsonElement Ok(JsonElement response)
    {
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        return response.TryGetProperty("payload", out var payload) ? payload : default;
    }

    private static string ErrorCode(JsonElement response)
    {
        Assert.False(response.GetProperty("ok").GetBoolean());
        return response.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task System_info_declares_local_processing_and_hardware()
    {
        var info = Ok(await Call("system.info"));

        Assert.Equal("local", info.GetProperty("processing").GetString());
        Assert.Equal(8, info.GetProperty("hardware").GetProperty("logicalCores").GetInt32());
        Assert.True(info.GetProperty("supportsStandardRuntime").GetBoolean());
    }

    [Fact]
    public async Task Invalid_requests_produce_typed_errors()
    {
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("does.not.exist")));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("system.info", version: 99)));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("models.remove")));
        Assert.Equal(ErrorCodes.ModelUnknown, ErrorCode(await Call("models.remove", new { modelId = "zzz" })));

        var garbage = JsonDocument.Parse(await _dispatcher.HandleAsync("{not json")).RootElement;
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(garbage));
    }

    [Fact]
    public async Task Models_list_reports_state_requirements_and_issues()
    {
        var models = Ok(await Call("models.list")).EnumerateArray().ToList();

        Assert.Equal(2, models.Count);
        var small = models.Single(m => m.GetProperty("id").GetString() == "small");
        Assert.Equal("notInstalled", small.GetProperty("state").GetString());
        Assert.True(small.GetProperty("downloadable").GetBoolean());
        Assert.Equal(1_000_000_000, small.GetProperty("ramRequiredBytes").GetInt64());
        Assert.Equal(6, small.GetProperty("relativeCost").GetInt32());
        Assert.Empty(small.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task Models_list_tells_the_ui_the_engine_and_whether_it_is_a_preview()
    {
        var models = Ok(await Call("models.list")).EnumerateArray().ToList();

        Assert.All(models, m =>
        {
            Assert.Equal("whisper", m.GetProperty("engine").GetString());
            Assert.False(m.GetProperty("preview").GetBoolean());
            Assert.False(m.GetProperty("requiresLanguage").GetBoolean());
            Assert.False(m.TryGetProperty("accuracy", out _));
        });
    }

    [Fact]
    public async Task Recommend_returns_model_and_reasons()
    {
        var recommendation = Ok(await Call("models.recommend"));

        Assert.Equal("small", recommendation.GetProperty("recommendedId").GetString());
        Assert.NotEmpty(recommendation.GetProperty("reasons").EnumerateArray());
    }

    [Fact]
    public async Task Cancelled_dialogs_return_null_payload()
    {
        Assert.Equal(JsonValueKind.Undefined, Ok(await Call("audio.pick")).ValueKind);
        Assert.Equal(JsonValueKind.Undefined, Ok(await Call("audio.pickMany")).ValueKind);
        Assert.Equal(JsonValueKind.Undefined, Ok(await Call("models.import")).ValueKind);
    }

    [Fact]
    public async Task Full_flow_import_pick_transcribe_export()
    {
        _shell.NextOpen = _dir.File("base.bin", ModelContent);
        var imported = Ok(await Call("models.import"));
        Assert.Equal("ready", imported.GetProperty("state").GetString());

        Ok(await Call("settings.set", new { selectedModelId = "base", language = "it", showMascot = true }));
        Assert.Equal("base", Ok(await Call("settings.get")).GetProperty("selectedModelId").GetString());

        _shell.NextOpen = _dir.File("riunione.wav", TestData.Wav(16000, 1, 1.5));
        var audio = Ok(await Call("audio.pick"));
        Assert.Equal("riunione.wav", audio.GetProperty("info").GetProperty("fileName").GetString());
        Assert.Equal(1.5, audio.GetProperty("info").GetProperty("duration").GetDouble(), 2);

        var started = Ok(await Call("transcription.start", new { audioId = audio.GetProperty("audioId").GetString() }));
        var jobId = started.GetProperty("jobId").GetString()!;
        Assert.Equal("base", started.GetProperty("modelId").GetString());

        var completed = await _shell.WaitForEventAsync("transcription.completed");
        var result = completed.GetProperty("payload").GetProperty("result");
        Assert.Equal("echo it", result.GetProperty("text").GetString());
        Assert.Equal("base", result.GetProperty("model").GetProperty("modelId").GetString());
        Assert.Contains(_shell.Events, e => e.GetProperty("event").GetString() == "transcription.progress");

        _shell.NextSave = Path.Combine(_dir.Path, "out.srt");
        var saved = Ok(await Call("export.save", new { jobId, format = "srt" }));
        Assert.Equal("out.srt", saved.GetProperty("fileName").GetString());
        Assert.Equal("riunione.srt", _shell.LastSuggestedName);
        Assert.StartsWith("1\n00:00:00,000 --> 00:00:01,500\necho it", await File.ReadAllTextAsync(_shell.NextSave), StringComparison.Ordinal);

        // Cronologia: salvata automaticamente in file propri nella cartella visibile.
        var historyId = completed.GetProperty("payload").GetProperty("historyId").GetString()!;
        Assert.True(File.Exists(Path.Combine(HistoryDir, historyId + ".txt")));
        Assert.True(File.Exists(Path.Combine(HistoryDir, historyId + ".json")));
        var history = Ok(await Call("history.list"));
        Assert.Equal(HistoryDir, history.GetProperty("directory").GetString());
        var entry = Assert.Single(history.GetProperty("entries").EnumerateArray());
        Assert.Equal("echo it", entry.GetProperty("preview").GetString());

        var reopened = Ok(await Call("history.get", new { id = historyId }));
        Assert.Equal("echo it", reopened.GetProperty("text").GetString());

        _shell.NextSave = Path.Combine(_dir.Path, "from-history.txt");
        Ok(await Call("export.save", new { historyId, format = "txt" }));
        Assert.Equal("echo it\n", await File.ReadAllTextAsync(_shell.NextSave));

        Ok(await Call("history.openFolder"));
        Assert.Equal(HistoryDir, _shell.OpenedFolder);

        // Nome e raccolte: in library.json accanto alle trascrizioni.
        var renamed = Ok(await Call("history.update", new { id = historyId, title = "  Lezione 1  " }));
        Assert.Equal("Lezione 1", renamed.GetProperty("title").GetString());
        var collection = Ok(await Call("collections.save", new { name = "Storia", color = "green" }));
        var collectionId = collection.GetProperty("id").GetString()!;
        var moved = Ok(await Call("history.update", new { id = historyId, collectionId }));
        Assert.Equal("Lezione 1", moved.GetProperty("title").GetString());
        Assert.Equal(collectionId, moved.GetProperty("collectionId").GetString());
        Ok(await Call("collections.reorder", new { id = collectionId, entryIds = new[] { historyId } }));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("collections.reorder", new { id = collectionId, entryIds = Array.Empty<string>() })));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("collections.save", new { name = "X", color = "blu" })));
        Assert.Equal(ErrorCodes.HistoryNotFound, ErrorCode(await Call("history.update", new { id = "assente", title = "x" })));
        var listed = Ok(await Call("history.list"));
        var listedEntry = Assert.Single(listed.GetProperty("entries").EnumerateArray());
        Assert.Equal("Lezione 1", listedEntry.GetProperty("title").GetString());
        var listedCollection = Assert.Single(listed.GetProperty("collections").EnumerateArray());
        Assert.Equal("Storia", listedCollection.GetProperty("name").GetString());
        Assert.Equal(historyId, Assert.Single(listedCollection.GetProperty("entryIds").EnumerateArray()).GetString());
        Ok(await Call("collections.delete", new { id = collectionId }));
        Assert.Empty(Ok(await Call("history.list")).GetProperty("collections").EnumerateArray());

        Ok(await Call("history.delete", new { id = historyId }));
        Assert.Equal(HistoryLibrary.FileName, Path.GetFileName(Assert.Single(Directory.GetFiles(HistoryDir))));
        Assert.Equal(ErrorCodes.HistoryNotFound, ErrorCode(await Call("history.get", new { id = historyId })));
    }

    [Fact]
    public async Task History_ids_cannot_escape_the_folder()
    {
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("history.get", new { id = "../settings" })));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("history.delete", new { id = "..\\x" })));
    }

    [Fact]
    public async Task History_folder_can_be_changed_and_ui_language_is_validated()
    {
        var custom = Path.Combine(_dir.Path, "Le mie trascrizioni");
        _shell.NextFolder = custom;

        var saved = Ok(await Call("history.pickFolder"));

        Assert.Equal(custom, saved.GetProperty("historyDirectory").GetString());
        Assert.Equal(custom, Ok(await Call("history.list")).GetProperty("directory").GetString());
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { uiLanguage = "de" })));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { historyDirectory = "relativa" })));
        Assert.Equal("en", Ok(await Call("settings.set", new { uiLanguage = "en" })).GetProperty("uiLanguage").GetString());
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { theme = "sepia" })));
        Assert.Equal("dark", Ok(await Call("settings.set", new { theme = "dark" })).GetProperty("theme").GetString());
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { palette = "viola" })));
        Assert.Equal("halloween", Ok(await Call("settings.set", new { palette = "halloween" })).GetProperty("palette").GetString());
        Assert.Equal("natale", Ok(await Call("settings.set", new { palette = "natale" })).GetProperty("palette").GetString());
        Assert.Equal("natale", Ok(await Call("settings.get")).GetProperty("palette").GetString());
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { mascot = "drago" })));
        Assert.Equal("giulio", Ok(await Call("settings.set", new { mascot = "giulio" })).GetProperty("mascot").GetString());
    }

    [Fact]
    public async Task Recommendation_reasons_are_codes_for_the_ui()
    {
        var reasons = Ok(await Call("models.recommend")).GetProperty("reasons").EnumerateArray().ToList();

        Assert.Contains(reasons, r => r.GetProperty("code").GetString() == "ram_limit");
        var recommended = reasons.Single(r => r.GetProperty("code").GetString() == "recommended");
        Assert.Equal("small", recommended.GetProperty("args").GetProperty("modelId").GetString());
    }

    [Fact]
    public async Task Download_of_unpinned_model_is_refused()
    {
        var store = new ModelStore(_dir.AppPaths(), TestData.Catalog(revision: null, models: ("base", ModelContent, 2, 2, 0)));
        using var dispatcher = new BridgeDispatcher(_dir.AppPaths(), new SettingsStore(Path.Combine(_dir.Path, "s2.json")), store, new NoDownloads(),
            new FakeHardware(TestData.Hardware()), new AudioLibrary([], TimeSpan.FromHours(1)), _transcription, _shell, new HistoryLocation(HistoryDir), NullLogger<BridgeDispatcher>.Instance);

        var json = JsonSerializer.Serialize(new { v = 1, id = "x", type = "models.download", payload = new { modelId = "base" } });
        var response = JsonDocument.Parse(await dispatcher.HandleAsync(json)).RootElement;

        Assert.Equal(ErrorCodes.ModelNotVerified, ErrorCode(response));
    }

    [Fact]
    public async Task Model_found_corrupted_during_transcription_refreshes_the_models_list()
    {
        _shell.NextOpen = _dir.File("base.bin", ModelContent);
        Ok(await Call("models.import"));
        var changedBefore = _shell.Events.Count(e => e.GetProperty("event").GetString() == "models.changed");
        // Stessa dimensione, contenuto diverso: la verifica completa trova l'hash sbagliato.
        var corrupted = (byte[])ModelContent.Clone();
        corrupted[0] ^= 0xFF;
        await File.WriteAllBytesAsync(_store.PathFor(_store.Catalog.Find("base")!), corrupted);
        _shell.NextOpen = _dir.File("voce2.wav", TestData.Wav(16000, 1, 1));
        var audioId = Ok(await Call("audio.pick")).GetProperty("audioId").GetString();
        Ok(await Call("transcription.start", new { audioId, modelId = "base" }));

        var failed = await _shell.WaitForEventAsync("transcription.failed");

        Assert.Equal(ErrorCodes.ModelHashMismatch, failed.GetProperty("payload").GetProperty("error").GetProperty("code").GetString());
        Assert.True(_shell.Events.Count(e => e.GetProperty("event").GetString() == "models.changed") > changedBefore);
        Assert.Equal("corrupted", Ok(await Call("models.list")).EnumerateArray().Single(m => m.GetProperty("id").GetString() == "base").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Blocked_history_folder_keeps_the_transcript_in_the_recovery_folder()
    {
        // Un file al posto della cartella: CreateDirectory fallisce come quando Defender blocca la scrittura.
        var blocker = _dir.File("CodexVTT-bloccato", [1]);
        var recovery = Path.Combine(_dir.Path, "recovery");
        using var dispatcher = new BridgeDispatcher(_dir.AppPaths(), new SettingsStore(Path.Combine(_dir.Path, "s3.json")), _store, new NoDownloads(),
            new FakeHardware(TestData.Hardware()), new AudioLibrary([new WavDecoder()], TimeSpan.FromHours(3)), _transcription, _shell,
            new HistoryLocation(Path.Combine(blocker, "history"), recovery), NullLogger<BridgeDispatcher>.Instance);

        // Il job parte dal dispatcher principale (stesso servizio e stessa libreria audio); questo riceve solo il Finished.
        _shell.NextOpen = _dir.File("base3.bin", ModelContent);
        Ok(await Call("models.import"));
        _shell.NextOpen = _dir.File("voce.wav", TestData.Wav(16000, 1, 1));
        var audioId = Ok(await Call("audio.pick")).GetProperty("audioId").GetString();
        Ok(await Call("transcription.start", new { audioId, modelId = "base" }));

        var failed = await _shell.WaitForEventAsync("history.saveFailed");

        Assert.Equal(ErrorCodes.HistoryFolderBlocked, failed.GetProperty("payload").GetProperty("error").GetProperty("code").GetString());
        Assert.NotEmpty(Directory.GetFiles(recovery, "*.json"));
    }

    [Fact]
    public async Task Pick_many_registers_every_file_and_reports_the_unreadable_ones()
    {
        _shell.NextOpenMany.Add(_dir.File("uno.wav", TestData.Wav(16000, 1, 1)));
        _shell.NextOpenMany.Add(_dir.File("rotto.wav", [1, 2, 3]));
        _shell.NextOpenMany.Add(_dir.File("due.wav", TestData.Wav(16000, 1, 2)));

        var batch = Ok(await Call("audio.pickMany"));

        var picked = batch.GetProperty("picked").EnumerateArray().ToList();
        Assert.Equal(new[] { "uno.wav", "due.wav" }, picked.Select(p => p.GetProperty("info").GetProperty("fileName").GetString()).ToArray());
        Assert.NotEqual(picked[0].GetProperty("audioId").GetString(), picked[1].GetProperty("audioId").GetString());
        var failed = Assert.Single(batch.GetProperty("failed").EnumerateArray());
        Assert.Equal("rotto.wav", failed.GetProperty("fileName").GetString());
        Assert.False(string.IsNullOrEmpty(failed.GetProperty("error").GetProperty("code").GetString()));
    }

    [Fact]
    public async Task The_cpu_limit_is_validated_saved_and_turned_into_engine_threads()
    {
        Assert.Equal(100, Ok(await Call("settings.get")).GetProperty("cpuLimitPercent").GetInt32());
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { language = "it", showMascot = true, cpuLimitPercent = 5 })));
        Assert.Equal(ErrorCodes.InvalidRequest, ErrorCode(await Call("settings.set", new { language = "it", showMascot = true, cpuLimitPercent = 150 })));
        Ok(await Call("settings.set", new { language = "it", showMascot = true, cpuLimitPercent = 50 }));
        Assert.Equal(50, Ok(await Call("settings.get")).GetProperty("cpuLimitPercent").GetInt32());

        _shell.NextOpen = _dir.File("base.bin", ModelContent);
        Ok(await Call("models.import"));
        _shell.NextOpen = _dir.File("limite.wav", TestData.Wav(16000, 1, 1));
        var audioId = Ok(await Call("audio.pick")).GetProperty("audioId").GetString();
        Ok(await Call("transcription.start", new { audioId, modelId = "base", language = "it" }));
        await _shell.WaitForEventAsync("transcription.completed");

        Assert.Equal(CodexVtt.Core.Transcription.CpuLimit.Threads(50, Environment.ProcessorCount), EchoEngine.LastThreads);
    }

    [Fact]
    public async Task Pausing_an_unknown_job_is_a_typed_error()
    {
        Assert.Equal(ErrorCodes.TranscriptionNotFound, ErrorCode(await Call("transcription.pause", new { jobId = "boh" })));
        Assert.Equal(ErrorCodes.TranscriptionNotFound, ErrorCode(await Call("transcription.resume", new { jobId = "boh" })));
    }

    [Fact]
    public async Task Queue_runs_every_file_in_the_core_without_the_ui()
    {
        _shell.NextOpen = _dir.File("base.bin", ModelContent);
        Ok(await Call("models.import"));
        _shell.NextOpenMany.Add(_dir.File("uno.wav", TestData.Wav(16000, 1, 1)));
        _shell.NextOpenMany.Add(_dir.File("rotto.wav", [1, 2, 3]));
        _shell.NextOpenMany.Add(_dir.File("due.wav", TestData.Wav(16000, 1, 2)));
        var batch = Ok(await Call("audio.pickMany"));
        var audioIds = batch.GetProperty("picked").EnumerateArray().Select(p => p.GetProperty("audioId").GetString()).ToArray();
        var failed = batch.GetProperty("failed").EnumerateArray().Select(f => JsonSerializer.Deserialize<object>(f.GetRawText())).ToArray();

        var added = Ok(await Call("queue.add", new { audioIds, failed }));
        Assert.Equal(new[] { "queued", "queued", "failed" }, added.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("status").GetString()).ToArray());
        Assert.Equal(ErrorCodes.AudioNotFound, ErrorCode(await Call("queue.add", new { audioIds = new[] { "sconosciuto" } })));

        Ok(await Call("queue.start", new { modelId = "base", language = "it" }));
        var finished = await _shell.WaitForEventAsync("queue.finished");

        Assert.Equal(2, finished.GetProperty("payload").GetProperty("done").GetInt32());
        Assert.Equal(1, finished.GetProperty("payload").GetProperty("failed").GetInt32());
        var state = Ok(await Call("queue.get"));
        Assert.False(state.GetProperty("running").GetBoolean());
        var items = state.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "done", "done", "failed" }, items.Select(i => i.GetProperty("status").GetString()).ToArray());
        // Ogni file finito è nella cronologia, anche se nessuna UI ha ascoltato.
        var historyId = items[0].GetProperty("historyId").GetString()!;
        Assert.Equal("echo it", Ok(await Call("history.get", new { id = historyId })).GetProperty("text").GetString());
        Assert.DoesNotContain(_shell.Events, e => e.GetProperty("event").GetString() == "transcription.completed");
        Assert.Equal(JsonValueKind.Undefined, Ok(await Call("transcription.active")).ValueKind);

        // Si tolgono solo i file che non stanno lavorando; "svuota" non tocca una coda in corso.
        Ok(await Call("queue.remove", new { key = items[2].GetProperty("key").GetString() }));
        Assert.Equal(2, Ok(await Call("queue.get")).GetProperty("items").GetArrayLength());
        Assert.Empty(Ok(await Call("queue.clear")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Queue_start_needs_a_known_model()
    {
        Assert.Equal(ErrorCodes.ModelNotInstalled, ErrorCode(await Call("queue.start")));
        Assert.Equal(ErrorCodes.ModelUnknown, ErrorCode(await Call("queue.start", new { modelId = "zzz" })));
    }

    [Fact]
    public async Task Export_can_be_rendered_for_download()
    {
        _shell.NextOpen = _dir.File("base.bin", ModelContent);
        Ok(await Call("models.import"));
        _shell.NextOpen = _dir.File("lezione.wav", TestData.Wav(16000, 1, 1));
        var audioId = Ok(await Call("audio.pick")).GetProperty("audioId").GetString();
        var jobId = Ok(await Call("transcription.start", new { audioId, modelId = "base", language = "it" })).GetProperty("jobId").GetString();
        await _shell.WaitForEventAsync("transcription.completed");

        var (fileName, content) = await _dispatcher.RenderExportAsync(new ExportRequest(jobId, null, CodexVtt.Core.Export.ExportFormat.Txt, false), default);

        Assert.Equal("lezione.txt", fileName);
        Assert.Equal("echo it\n", System.Text.Encoding.UTF8.GetString(content));
        Assert.Empty(Directory.GetFiles(_dir.AppPaths().Temp));
    }

    [Fact]
    public async Task Updates_are_unsupported_without_an_update_service()
    {
        var status = Ok(await Call("updates.status"));

        Assert.Equal("unsupported", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, Ok(await Call("updates.check")).GetProperty("state").ValueKind);
        Assert.Equal(ErrorCodes.UpdateUnavailable, ErrorCode(await Call("updates.apply")));
    }

    [Fact]
    public async Task Updates_check_reports_the_new_version_and_apply_downloads_it()
    {
        var updates = new FakeUpdates();
        using var dispatcher = new BridgeDispatcher(_dir.AppPaths(), new SettingsStore(Path.Combine(_dir.Path, "s4.json")), _store, new NoDownloads(),
            new FakeHardware(TestData.Hardware()), new AudioLibrary([], TimeSpan.FromHours(1)), _transcription, _shell,
            new HistoryLocation(HistoryDir), NullLogger<BridgeDispatcher>.Instance, updates);
        async Task<JsonElement> CallOn(string type) =>
            JsonDocument.Parse(await dispatcher.HandleAsync(JsonSerializer.Serialize(new { v = 1, id = type, type }))).RootElement;

        Assert.Equal(ErrorCodes.UpdateUnavailable, ErrorCode(await CallOn("updates.apply")));

        var checkedStatus = Ok(await CallOn("updates.check"));
        Assert.Equal("available", checkedStatus.GetProperty("state").GetString());
        Assert.Equal("0.2.0", checkedStatus.GetProperty("latestVersion").GetString());
        Assert.Contains(_shell.Events, e => e.GetProperty("event").GetString() == "updates.changed");

        Ok(await CallOn("updates.apply"));
        var progress = await _shell.WaitForEventAsync("updates.progress");
        Assert.Equal(100, progress.GetProperty("payload").GetProperty("percent").GetInt32());
        Assert.True(updates.Applied);
    }

    [Fact]
    public async Task Settings_keep_the_auto_update_choice()
    {
        Assert.False(Ok(await Call("settings.get")).GetProperty("autoCheckUpdates").GetBoolean());

        Ok(await Call("settings.set", new { language = "it", showMascot = true, autoCheckUpdates = true }));

        Assert.True(Ok(await Call("settings.get")).GetProperty("autoCheckUpdates").GetBoolean());
    }

    [Fact]
    public async Task Settings_reject_unknown_model()
    {
        Assert.Equal(ErrorCodes.ModelUnknown, ErrorCode(await Call("settings.set", new { selectedModelId = "nope" })));
    }

    private sealed class FakeShell : IShellServices
    {
        private readonly ConcurrentQueue<JsonElement> _events = new();

        public string? NextOpen { get; set; }

        public List<string> NextOpenMany { get; } = [];

        public string? NextSave { get; set; }

        public string? LastSuggestedName { get; private set; }

        public string? NextFolder { get; set; }

        public string? OpenedFolder { get; private set; }

        public Task<string?> PickFolderAsync(CancellationToken ct) => Task.FromResult(NextFolder);

        public void OpenFolder(string path) => OpenedFolder = path;

        public IEnumerable<JsonElement> Events => _events;

        public Task<string?> PickAudioFileAsync(CancellationToken ct) => Task.FromResult(Take());

        public Task<IReadOnlyList<string>> PickAudioFilesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. NextOpenMany]);

        public Task<string?> PickModelFileAsync(CancellationToken ct) => Task.FromResult(Take());

        public Task<string?> PickSaveFileAsync(string suggestedName, string extension, CancellationToken ct)
        {
            LastSuggestedName = suggestedName;
            return Task.FromResult(NextSave);
        }

        public void PostToUi(string json) => _events.Enqueue(JsonDocument.Parse(json).RootElement.Clone());

        public async Task<JsonElement> WaitForEventAsync(string name)
        {
            for (var i = 0; i < 200; i++)
            {
                var match = _events.FirstOrDefault(e => e.GetProperty("event").GetString() == name);
                if (match.ValueKind != JsonValueKind.Undefined)
                    return match;
                await Task.Delay(25);
            }

            throw new TimeoutException(name);
        }

        private string? Take()
        {
            var value = NextOpen;
            NextOpen = null;
            return value;
        }
    }

    private sealed class FakeUpdates : IUpdateService
    {
        private UpdateStatus _current = new(UpdateState.UpToDate, "0.1.0");

        public bool Applied { get; private set; }

        public UpdateStatus Current => _current;

        public Task<UpdateStatus> CheckAsync(CancellationToken ct) =>
            Task.FromResult(_current = new UpdateStatus(UpdateState.Available, "0.1.0", "0.2.0", "Novità"));

        public Task ApplyAsync(IProgress<int> progress, CancellationToken ct)
        {
            progress.Report(100);
            Applied = true;
            return Task.CompletedTask;
        }
    }

    private sealed class NoDownloads : IModelDownloader
    {
        public Task<InstalledModelStatus> DownloadAsync(string modelId, IProgress<DownloadProgress>? progress, CancellationToken ct) =>
            throw new InvalidOperationException("La rete non deve essere usata in questo test.");
    }

    private sealed class EchoEngine : ITranscriptionEngine
    {
        /// <summary>Thread chiesti dall'ultima trascrizione (null = nessun limite).</summary>
        public static int? LastThreads { get; set; }

        public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct) =>
            Task.FromResult<ITranscriptionSession>(new Session());

        private sealed class Session : ITranscriptionSession
        {
            public string Runtime => "Echo";

            public string? DetectedLanguage { get; private set; }

            public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
                ReadOnlyMemory<float> samples,
                TranscriptionOptions options,
                IProgress<int>? progress,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                LastThreads = options.Threads;
                progress?.Report(100);
                DetectedLanguage = options.Language;
                yield return new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0), $"echo {options.Language}", 1);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
