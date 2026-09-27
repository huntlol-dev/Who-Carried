using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>A name set inside a sentence, like the Export as image button's in the copy failure message, never wraps in its middle.</summary>
public static class LineBreaksTests
{
    /// <summary>The text as code points, so an invisible joiner or a no-break space shows up in a failure.</summary>
    private static string Codes(string text) => string.Join(" ", text.EnumerateRunes().Select(r => r.Value.ToString("X4")));

    [Test]
    public static void SpacesInANameStopBeingPlacesToBreak()
    {
        Check.Equal(Codes("Export as image"), Codes(LineBreaks.KeepTogether("Export as image")), "no-break spaces, the letters untouched");
    }

    [Test]
    public static void ChineseCharactersAreJoinedSoTheNameStaysOnOneLine()
    {
        Check.Equal(Codes("导⁠出⁠战⁠绩⁠图"), Codes(LineBreaks.KeepTogether("导出战绩图")), "a word joiner between each two");
    }

    [Test]
    public static void ACharacterOutsideTheBasicPlaneIsNeverSplit()
    {
        // Two ideographs past U+FFFF, each stored as a surrogate pair: the joiner goes between them, never inside one.
        string kept = LineBreaks.KeepTogether("\U00020000\U00020001");
        Check.Equal(Codes("\U00020000⁠\U00020001"), Codes(kept), "the pairs intact");
        Check.Equal(5, kept.Length, "two pairs and one joiner");
    }

    [Test]
    public static void MixedTextIsJoinedOnlyWhereItCouldBreak()
    {
        Check.Equal(Codes("Steam 截⁠图"), Codes(LineBreaks.KeepTogether("Steam 截图")), "a space and two Chinese characters");
        Check.Equal(Codes("A⁠图"), Codes(LineBreaks.KeepTogether("A图")), "a Latin letter against a Chinese one");
        Check.Equal("", LineBreaks.KeepTogether(""), "nothing in, nothing out");
    }
}
