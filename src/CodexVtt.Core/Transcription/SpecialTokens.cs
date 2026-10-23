using System.Text.RegularExpressions;

namespace CodexVtt.Core.Transcription;

/// <summary>Marcatori che Whisper emette al posto del parlato, come <c>[BLANK_AUDIO]</c> o <c>[MUSIC]</c>.</summary>
public static partial class SpecialTokens
{
    public static bool IsMarker(string text) => MarkerPattern().IsMatch(text.Trim());

    [GeneratedRegex(@"^\[[A-Za-z_ ]{2,30}\]$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();
}
