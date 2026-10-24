using System.Runtime.CompilerServices;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Hardware;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace CodexVtt.Transcription.Whisper;

/// <summary>
/// Motore di trascrizione locale basato su whisper.cpp tramite Whisper.net (ADR-0002).
/// Nessuna funzione di rete di Whisper.net (es. WhisperGgmlDownloader) viene usata.
/// </summary>
public sealed class WhisperEngine : ITranscriptionEngine
{
    private static readonly Lock RuntimeGate = new();
    private static bool _runtimeConfigured;
    private static bool _gpuInUse;
    private readonly bool _useGpu;

    /// <param name="useGpu">
    /// Prova il runtime GPU (Vulkan) se il PC ha un driver Vulkan e una CPU con AVX2; altrimenti, o se il runtime
    /// non si carica, usa la CPU. Il runtime nativo si sceglie una volta per processo: cambiare scelta richiede di riavviare.
    /// </param>
    public WhisperEngine(bool useGpu = false) => _useGpu = useGpu;

    /// <summary>Il PC ha almeno una scheda video utilizzabile con Vulkan? (Il solo caricatore non basta: spesso c'è senza scheda.)</summary>
    public static bool IsVulkanGpuPresent() => GpuProbe.Devices.Count > 0;

    public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct)
    {
        ConfigureRuntimeOrder(_useGpu);
        return Task.Run<ITranscriptionSession>(() =>
        {
            ct.ThrowIfCancellationRequested();
            // I messaggi di whisper.cpp dicono quale backend ha scelto davvero (GPU o CPU).
            var messages = new List<string>();
            using var capture = global::Whisper.net.Logger.LogProvider.AddLogger((_, message) =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    lock (messages)
                        messages.Add(message);
                }
            });
            try
            {
                var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = _gpuInUse });
                return new WhisperSession(factory, RuntimeLabel(RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown", _gpuInUse, messages));
            }
            catch (WhisperModelLoadException ex)
            {
                throw new CodexVttException(ErrorCodes.ModelLoadFailed, "Impossibile caricare il modello.", inner: ex);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException)
            {
                throw new CodexVttException(ErrorCodes.RuntimeUnavailable,
                    "Runtime nativo di Whisper non disponibile (VC++ Redistributable mancante o CPU non supportata).", inner: ex);
            }
            catch (OutOfMemoryException ex)
            {
                throw new CodexVttException(ErrorCodes.InsufficientMemory, "Memoria insufficiente per caricare il modello.", inner: ex);
            }
        }, ct);
    }

    /// <summary>
    /// Nome del runtime mostrato all'utente. Con il runtime Vulkan caricato e la scheda richiesta vale «Vulkan (GPU)», a meno che
    /// whisper.cpp dica di non aver trovato o inizializzato nessuna scheda (allora il lavoro gira sulla CPU e lo diciamo).
    /// Non si pretende una riga di log precisa per dichiarare la GPU: il testo dei messaggi cambia tra le versioni di
    /// whisper.cpp e un riscontro mancante faceva comparire «Cpu» anche con la scheda in uso (RTX 3060 su 2.1.1).
    /// </summary>
    public static string RuntimeLabel(string loaded, bool gpuRequested, IReadOnlyList<string> messages)
    {
        if (!string.Equals(loaded, "Vulkan", StringComparison.OrdinalIgnoreCase))
            return loaded;
        if (!gpuRequested)
            return "Cpu";
        string[] copy;
        lock (messages)
            copy = [.. messages];
        var failed = copy.Any(m => m.Contains("no GPU found", StringComparison.OrdinalIgnoreCase)
                                   || m.Contains("failed to initialize GPU", StringComparison.OrdinalIgnoreCase)
                                   || m.Contains("failed to init GPU", StringComparison.OrdinalIgnoreCase));
        var usedGpu = copy.Any(m => m.Contains("whisper_backend_init_gpu", StringComparison.Ordinal)
                                   && m.Contains("using ", StringComparison.Ordinal) && m.Contains(" backend", StringComparison.Ordinal));
        return usedGpu || !failed ? "Vulkan (GPU)" : "Cpu";
    }

    /// <summary>Ordine esplicito dei runtime: GPU (se richiesta e possibile), CPU standard, poi CPU senza AVX.</summary>
    private static void ConfigureRuntimeOrder(bool useGpu)
    {
        lock (RuntimeGate)
        {
            if (_runtimeConfigured)
                return;
            // Diagnostica: i messaggi di whisper.cpp/ggml (dispositivi trovati, backend scelto) sulla console.
            if (Environment.GetEnvironmentVariable("CODEXVTT_WHISPER_LOG") == "1")
                global::Whisper.net.Logger.LogProvider.AddConsoleLogging(global::Whisper.net.Logger.WhisperLogLevel.Debug);
            _gpuInUse = useGpu && IsVulkanGpuPresent() && (HardwareProbe.DetectCpuFeatures() & CpuFeatures.Avx2) != 0;
            RuntimeOptions.RuntimeLibraryOrder = _gpuInUse
                ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
                : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            _runtimeConfigured = true;
        }
    }

    private sealed class WhisperSession(WhisperFactory factory, string runtime) : ITranscriptionSession
    {
        public string Runtime { get; } = runtime;

        public string? DetectedLanguage { get; private set; }

        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            ReadOnlyMemory<float> samples,
            TranscriptionOptions options,
            IProgress<int>? progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var builder = factory.CreateBuilder()
                .WithThreads(options.Threads ?? Math.Max(1, Environment.ProcessorCount - 1))
                .WithProbabilities();
            builder = string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase)
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(options.Language);
            if (options.Translate)
                builder = builder.WithTranslate();
            if (progress is not null)
                builder = builder.WithProgressHandler(progress.Report);

            await using var processor = builder.Build();
            var enumerator = processor.ProcessAsync(samples, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    SegmentData segment;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                            break;
                        segment = enumerator.Current;
                    }
                    catch (WhisperProcessingException ex)
                    {
                        throw new CodexVttException(ErrorCodes.TranscriptionFailed, "Errore del runtime durante la trascrizione.", inner: ex);
                    }

                    DetectedLanguage ??= segment.Language;
                    yield return new TranscriptSegment(segment.Start, segment.End, segment.Text, segment.Probability);
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync()
        {
            factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
