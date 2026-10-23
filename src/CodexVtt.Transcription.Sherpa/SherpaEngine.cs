using System.Runtime.CompilerServices;
using System.Text;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Models;
using CodexVtt.Core.Transcription;
using SherpaOnnx;

namespace CodexVtt.Transcription.Sherpa;

/// <summary>
/// Motore basato su sherpa-onnx (ADR-0009) per Parakeet TDT (<c>sherpa-transducer</c>), Canary
/// (<c>sherpa-canary</c>) e Cohere Transcribe (<c>sherpa-cohere</c>). Gira sulla CPU con ONNX Runtime;
/// nessuna funzione di rete di sherpa-onnx viene usata.
/// </summary>
public sealed class SherpaEngine : ITranscriptionEngine
{
    public const string Transducer = "sherpa-transducer";
    public const string Canary = "sherpa-canary";
    public const string Cohere = "sherpa-cohere";

    public Task<ITranscriptionSession> LoadAsync(ModelDescriptor model, string modelPath, CancellationToken ct)
    {
        if (!Directory.Exists(modelPath))
            throw new CodexVttException(ErrorCodes.ModelNotInstalled, $"Cartella del modello non trovata: {model.Id}.");

        // Il riconoscitore si crea alla prima trascrizione, quando la lingua è nota (Canary e Cohere la vogliono nella configurazione).
        ITranscriptionSession session = new SherpaSession(model, modelPath);
        return Task.FromResult(session);
    }

    /// <summary>Configurazione del riconoscitore per un tipo di modello e una lingua.</summary>
    internal static OfflineRecognizerConfig BuildConfig(string engine, string folder, string language, int threads)
    {
        string File(string name) => Path.Combine(folder, name);
        var config = new OfflineRecognizerConfig
        {
            DecodingMethod = "greedy_search",
        };
        config.ModelConfig.Tokens = File("tokens.txt");
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.Debug = 0;

        switch (engine)
        {
            case Transducer:
                config.ModelConfig.Transducer.Encoder = File("encoder.int8.onnx");
                config.ModelConfig.Transducer.Decoder = File("decoder.int8.onnx");
                config.ModelConfig.Transducer.Joiner = File("joiner.int8.onnx");
                config.ModelConfig.ModelType = "nemo_transducer";
                break;
            case Canary:
                config.ModelConfig.Canary.Encoder = File("encoder.int8.onnx");
                config.ModelConfig.Canary.Decoder = File("decoder.int8.onnx");
                // Origine e destinazione uguali: trascrive nella lingua parlata invece di tradurre.
                config.ModelConfig.Canary.SrcLang = language;
                config.ModelConfig.Canary.TgtLang = language;
                config.ModelConfig.Canary.UsePnc = 1;
                break;
            case Cohere:
                config.ModelConfig.CohereTranscribe.Encoder = File("encoder.int8.onnx");
                config.ModelConfig.CohereTranscribe.Decoder = File("decoder.int8.onnx");
                config.ModelConfig.CohereTranscribe.Language = language;
                config.ModelConfig.CohereTranscribe.UsePunct = 1;
                config.ModelConfig.CohereTranscribe.UseItn = 1;
                break;
            default:
                throw new CodexVttException(ErrorCodes.EngineUnavailable, $"Motore sconosciuto: {engine}");
        }

        return config;
    }

    private sealed class SherpaSession(ModelDescriptor model, string folder) : ITranscriptionSession
    {
        // Finestra massima per volta: Canary regge circa 40 s, Cohere e Parakeet rendono meglio con pezzi brevi.
        private static readonly AudioChunker Windows = new(TimeSpan.FromSeconds(28), TimeSpan.FromSeconds(4));
        private OfflineRecognizer? _recognizer;
        private string? _language;

        public string Runtime => "sherpa-onnx (Cpu)";

        public string? DetectedLanguage { get; private set; }

        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            ReadOnlyMemory<float> samples,
            TranscriptionOptions options,
            IProgress<int>? progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var language = ResolveLanguage(options.Language);
            var recognizer = await Task.Run(() => Recognizer(language, options.Threads), ct).ConfigureAwait(false);
            DetectedLanguage = language == "auto" ? null : language;

            var total = Math.Max(1, samples.Length);
            var done = 0;
            using var reader = new MemoryPcmReader(samples);
            foreach (var window in Windows.Split(reader))
            {
                ct.ThrowIfCancellationRequested();
                var offset = window.Offset;
                var segments = await Task.Run(() => Decode(recognizer, window.Samples, offset), ct).ConfigureAwait(false);
                done += window.Samples.Length;
                progress?.Report((int)Math.Min(99L, (long)done * 100 / total));
                foreach (var segment in segments)
                    yield return segment;
            }
        }

        public ValueTask DisposeAsync()
        {
            _recognizer?.Dispose();
            _recognizer = null;
            return ValueTask.CompletedTask;
        }

        /// <summary>Parakeet rileva la lingua da solo; Canary e Cohere vogliono un codice ("it", "en", …).</summary>
        private string ResolveLanguage(string requested)
        {
            if (!string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
                return requested.ToLowerInvariant();
            if (model.RequiresLanguage)
            {
                throw new CodexVttException(ErrorCodes.LanguageRequired, $"Il modello {model.DisplayName} richiede di scegliere la lingua.",
                    new Dictionary<string, string> { ["model"] = model.DisplayName });
            }

            return "auto";
        }

        private OfflineRecognizer Recognizer(string language, int? threads)
        {
            if (_recognizer is not null && _language == language)
                return _recognizer;
            _recognizer?.Dispose();
            _recognizer = null;

            try
            {
                var config = BuildConfig(model.Engine, folder, language, threads ?? Math.Clamp(Environment.ProcessorCount - 1, 1, 8));
                _recognizer = new OfflineRecognizer(config);
                _language = language;
                return _recognizer;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException)
            {
                throw new CodexVttException(ErrorCodes.RuntimeUnavailable,
                    "Runtime nativo di sherpa-onnx non disponibile (VC++ Redistributable mancante o CPU non supportata).", inner: ex);
            }
            catch (OutOfMemoryException ex)
            {
                throw new CodexVttException(ErrorCodes.InsufficientMemory, "Memoria insufficiente per caricare il modello.", inner: ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not CodexVttException)
            {
                throw new CodexVttException(ErrorCodes.ModelLoadFailed, "Impossibile caricare il modello.", inner: ex);
            }
        }

        private static List<TranscriptSegment> Decode(OfflineRecognizer recognizer, float[] samples, TimeSpan offset)
        {
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(AudioConstants.WhisperSampleRate, samples);
            recognizer.Decode(stream);
            var result = stream.Result;
            var duration = TimeSpan.FromSeconds((double)samples.Length / AudioConstants.WhisperSampleRate);
            return Segmenter.Build(result.Text, result.Tokens, result.Timestamps, result.Durations, offset, duration);
        }
    }
}

/// <summary>
/// Trasforma il risultato di sherpa-onnx in segmenti di sottotitoli. Con i timestamp dei token (Parakeet) si
/// taglia a fine frase o dopo una pausa; senza (Canary, Cohere) il testo di una finestra è un segmento unico.
/// </summary>
public static class Segmenter
{
    private const double PauseSeconds = 1.2;
    private const double MaxSegmentSeconds = 20;

    public static List<TranscriptSegment> Build(
        string? text,
        string[]? tokens,
        float[]? timestamps,
        float[]? durations,
        TimeSpan offset,
        TimeSpan windowDuration)
    {
        var whole = (text ?? "").Trim();
        if (whole.Length == 0)
            return [];

        var hasTimes = tokens is { Length: > 0 } && timestamps is not null && timestamps.Length == tokens.Length;
        if (!hasTimes)
            return [new TranscriptSegment(offset, offset + windowDuration, whole, null)];

        var segments = new List<TranscriptSegment>();
        var builder = new StringBuilder();
        double start = 0;
        double last = 0;
        for (var i = 0; i < tokens!.Length; i++)
        {
            var piece = tokens[i].Replace('▁', ' ');
            var time = timestamps![i];
            var duration = durations is not null && durations.Length == tokens.Length ? durations[i] : 0f;
            var gap = builder.Length > 0 && time - last > PauseSeconds;
            var tooLong = builder.Length > 0 && time - start > MaxSegmentSeconds;
            if (builder.Length > 0 && (gap || tooLong || EndsSentence(builder) && piece.StartsWith(' ')))
            {
                segments.Add(Make(builder, start, last, offset));
                builder.Clear();
            }

            if (builder.Length == 0)
                start = time;
            builder.Append(piece);
            last = time + duration;
        }

        if (builder.Length > 0)
            segments.Add(Make(builder, start, Math.Max(last, start), offset));
        return segments.Where(s => s.Text.Length > 0).ToList();
    }

    private static bool EndsSentence(StringBuilder text)
    {
        var end = text.ToString().TrimEnd();
        return end.Length > 0 && end[^1] is '.' or '?' or '!' or '…';
    }

    private static TranscriptSegment Make(StringBuilder builder, double start, double end, TimeSpan offset) =>
        new(offset + TimeSpan.FromSeconds(start), offset + TimeSpan.FromSeconds(Math.Max(end, start + 0.1)), builder.ToString().Trim(), null);
}
