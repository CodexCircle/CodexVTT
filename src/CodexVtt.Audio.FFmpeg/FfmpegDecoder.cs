using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;

namespace CodexVtt.Audio.FFmpeg;

/// <summary>
/// Decodifica con i programmi <c>ffprobe</c> e <c>ffmpeg</c> installati nel sistema (versione server su Linux, ADR-0007).
/// Non si distribuisce nessuna libreria: l'immagine Docker installa il pacchetto della distribuzione e i file
/// non lasciano mai il computer. Su Windows si usa Media Foundation (ADR-0004).
/// </summary>
public sealed class FfmpegDecoder : IAudioDecoder
{
    /// <summary>Stessi formati dichiarati dall'app Windows (specifica #2).</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".opus" };

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private readonly string _ffmpeg;
    private readonly string _ffprobe;

    public FfmpegDecoder(string ffmpeg = "ffmpeg", string ffprobe = "ffprobe")
    {
        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
    }

    /// <summary>True se i due programmi partono: il server sceglie questo decoder solo allora.</summary>
    public static bool IsAvailable(string ffmpeg = "ffmpeg", string ffprobe = "ffprobe") =>
        Runs(ffmpeg) && Runs(ffprobe);

    public bool CanDecode(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public AudioInfo Inspect(string path)
    {
        if (!File.Exists(path))
            throw new CodexVttException(ErrorCodes.AudioNotFound, "File audio non trovato.");

        var start = new ProcessStartInfo(_ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=sample_rate,channels:format=duration", "-of", "json", "--", path })
            start.ArgumentList.Add(argument);

        using var process = Start(start);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(ProbeTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw Corrupted("Lettura del file audio troppo lenta.");
        }

        if (process.ExitCode != 0)
            throw Corrupted("Impossibile leggere il file audio.", error.GetAwaiter().GetResult());

        using var json = JsonDocument.Parse(output.GetAwaiter().GetResult());
        if (!json.RootElement.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw Corrupted("Il file non contiene una traccia audio.");
        var stream = streams[0];
        var duration = json.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d)
            ? double.Parse(d.GetString() ?? "0", CultureInfo.InvariantCulture)
            : 0;
        return new AudioInfo(
            Path.GetFileName(path),
            Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
            TimeSpan.FromSeconds(duration),
            int.Parse(stream.GetProperty("sample_rate").GetString() ?? "0", CultureInfo.InvariantCulture),
            stream.GetProperty("channels").GetInt32(),
            new FileInfo(path).Length);
    }

    public IPcmReader OpenPcm16kMono(string path)
    {
        if (!File.Exists(path))
            throw new CodexVttException(ErrorCodes.AudioNotFound, "File audio non trovato.");

        var start = new ProcessStartInfo(_ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // PCM a 16 bit little-endian, mono, 16 kHz su stdout: ffmpeg fa anche il ricampionamento.
        foreach (var argument in new[] { "-nostdin", "-v", "error", "-i", path, "-vn", "-map", "0:a:0", "-ac", "1", "-ar", "16000", "-f", "s16le", "-" })
            start.ArgumentList.Add(argument);
        return new FfmpegPcmReader(Start(start));
    }

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new CodexVttException(ErrorCodes.AudioCodecMissing, "ffmpeg non si avvia.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CodexVttException(ErrorCodes.AudioCodecMissing, "ffmpeg non è installato.", inner: ex);
        }
    }

    private static bool Runs(string program)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;
            process.StandardOutput.ReadToEnd();
            return process.WaitForExit(ProbeTimeout) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static CodexVttException Corrupted(string message, string? detail = null) =>
        new(ErrorCodes.AudioCorrupted, message, inner: detail is { Length: > 0 } ? new InvalidOperationException(detail.Trim()) : null);

    private sealed class FfmpegPcmReader : IPcmReader
    {
        private readonly Process _process;
        private readonly Stream _output;
        private readonly Task<string> _error;
        private byte[] _bytes = [];
        private bool _finished;

        public FfmpegPcmReader(Process process)
        {
            _process = process;
            _output = process.StandardOutput.BaseStream;
            // stderr va svuotato mentre ffmpeg lavora, altrimenti con molti avvisi si blocca.
            _error = process.StandardError.ReadToEndAsync();
        }

        public int Read(Span<float> buffer)
        {
            if (_finished)
                return 0;
            var needed = buffer.Length * 2;
            if (_bytes.Length < needed)
                _bytes = new byte[needed];

            var total = 0;
            while (total < needed)
            {
                var read = _output.Read(_bytes, total, needed - total);
                if (read == 0)
                    break;
                total += read;
            }

            if (total < needed)
            {
                _finished = true;
                _process.WaitForExit();
                if (_process.ExitCode != 0)
                    throw Corrupted("Il file audio è danneggiato o non è del formato indicato dall'estensione.", _error.GetAwaiter().GetResult());
            }

            var samples = total / 2;
            for (var i = 0; i < samples; i++)
                buffer[i] = BitConverter.ToInt16(_bytes, i * 2) / 32768f;
            return samples;
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Terminato nel frattempo.
                }
            }

            _process.Dispose();
        }
    }
}
