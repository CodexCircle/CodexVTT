using System.Globalization;
using System.Text;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Core.Export;

public enum ExportFormat
{
    Txt,
    Srt,
    Vtt,
}

/// <param name="Language">Lingua dell'intestazione: "it" (default) o "en".</param>
public sealed record ExportOptions(bool IncludeHeader = false, string Language = "it");

/// <summary>Export locale della trascrizione in TXT, SRT e WebVTT (UTF-8).</summary>
public static class TranscriptExporter
{
    public static string FileExtension(ExportFormat format) => format switch
    {
        ExportFormat.Txt => ".txt",
        ExportFormat.Srt => ".srt",
        ExportFormat.Vtt => ".vtt",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string Render(TranscriptionResult result, ExportFormat format, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        return format switch
        {
            ExportFormat.Txt => RenderTxt(result, options),
            ExportFormat.Srt => RenderSrt(result),
            ExportFormat.Vtt => RenderVtt(result, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public static async Task WriteAsync(TranscriptionResult result, ExportFormat format, string path, ExportOptions? options = null, CancellationToken ct = default)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Render(result, format, options), new UTF8Encoding(false), ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private static string RenderTxt(TranscriptionResult result, ExportOptions options)
    {
        var sb = new StringBuilder();
        if (options.IncludeHeader)
        {
            foreach (var line in HeaderLines(result, options.Language))
                sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        sb.Append(result.Text).Append('\n');
        return sb.ToString();
    }

    private static string RenderSrt(TranscriptionResult result)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var segment in Cues(result))
        {
            sb.Append(index++).Append('\n');
            sb.Append(Timestamp(segment.Start, ',')).Append(" --> ").Append(Timestamp(segment.End, ',')).Append('\n');
            sb.Append(segment.Text).Append("\n\n");
        }

        return sb.ToString();
    }

    private static string RenderVtt(TranscriptionResult result, ExportOptions options)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        if (options.IncludeHeader)
        {
            sb.Append("NOTE\n");
            foreach (var line in HeaderLines(result, options.Language))
                sb.Append(line.Replace("-->", "->", StringComparison.Ordinal)).Append('\n');
            sb.Append('\n');
        }

        foreach (var segment in Cues(result))
        {
            sb.Append(Timestamp(segment.Start, '.')).Append(" --> ").Append(Timestamp(segment.End, '.')).Append('\n');
            sb.Append(segment.Text).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>Segmenti non vuoti con fine sempre successiva all'inizio.</summary>
    private static IEnumerable<TranscriptSegment> Cues(TranscriptionResult result) =>
        result.Segments
            .Select(s => s with { Text = s.Text.Trim().Replace("\r", "", StringComparison.Ordinal) })
            .Where(s => s.Text.Length > 0)
            .Select(s => s.End > s.Start ? s : s with { End = s.Start + TimeSpan.FromMilliseconds(500) });

    private static IEnumerable<string> HeaderLines(TranscriptionResult result, string language)
    {
        var en = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        yield return $"File: {result.AudioFileName}";
        yield return $"{(en ? "Model" : "Modello")}: {result.Model.DisplayName} ({result.Model.File}, sha256 {result.Model.Sha256})";
        if (result.DetectedLanguage is not null)
            yield return $"{(en ? "Language" : "Lingua")}: {result.DetectedLanguage}";
        yield return $"{(en ? "Date" : "Data")}: {result.CompletedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
        yield return en ? "Transcribed locally with CodexVTT." : "Trascrizione eseguita localmente con CodexVTT.";
    }

    internal static string Timestamp(TimeSpan time, char separator)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        var hours = (int)time.TotalHours;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{time.Minutes:00}:{time.Seconds:00}{separator}{time.Milliseconds:000}");
    }
}
