using System.Text;

namespace WhoCarried.Core;

/// <summary>Where a line may wrap inside text that has to stay whole.</summary>
public static class LineBreaks
{
    private const char NoBreakSpace = ' ', WordJoiner = '⁠';

    /// <summary>
    /// The same text, but a line can't wrap inside it: spaces become no-break spaces, and an invisible word joiner goes
    /// wherever an East Asian character meets another, since those scripts may wrap between any two characters. For a
    /// name set inside a sentence, like the Export as image button's in the copy failure message.
    /// </summary>
    public static string KeepTogether(string text)
    {
        var kept = new StringBuilder(text.Length * 2);
        Rune? before = null;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (before is Rune previous && !IsSpace(previous) && !IsSpace(rune) && (Wide(previous) || Wide(rune)))
                kept.Append(WordJoiner);
            if (IsSpace(rune)) kept.Append(NoBreakSpace);
            else kept.Append(rune.ToString());
            before = rune;
        }
        return kept.ToString();
    }

    private static bool IsSpace(Rune rune) => rune.Value == ' ';

    /// <summary>From the CJK radicals up: roughly the scripts written without spaces between words.</summary>
    private static bool Wide(Rune rune) => rune.Value >= 0x2E80;
}
