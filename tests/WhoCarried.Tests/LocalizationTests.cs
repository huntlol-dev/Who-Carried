using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhoCarried.Localization;

namespace WhoCarried.Tests;

public static class LocalizationTests
{
    [Test]
    public static void CatalogsHaveMatchingKeysAndPlaceholders()
    {
        var english = Loc.Read("eng");
        var chinese = Loc.Read("zhs");
        Check.True(english.Count > 100, "embedded English catalog");
        // Simplified Chinese is kept complete: new English text comes with its Chinese in the same change.
        Check.Equal("", string.Join(", ", english.Keys.Except(chinese.Keys).Order()), "in eng.json but not zhs.json");
        Check.Equal("", string.Join(", ", chinese.Keys.Except(english.Keys).Order()), "in zhs.json but not eng.json");
        foreach (var (key, text) in english)
        {
            Check.True(key.StartsWith("WHO_CARRIED."), key);
            Check.True(!string.IsNullOrWhiteSpace(chinese[key]), key);
            string Args(string template) => string.Join(",", Regex.Matches(template, @"\{(\d+)(?:[^{}]*)\}")
                .Select(m => m.Groups[1].Value).Order());
            Check.Equal(Args(text), Args(chinese[key]), key + " placeholders");
            object[] values = Enumerable.Range(0, 12).Select(n => (object)n).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, text, values);
            _ = string.Format(CultureInfo.GetCultureInfo("zh-CN"), chinese[key], values);
        }
    }

    /// <summary>A key the code asks for but the catalog lacks shows players "[WHO_CARRIED.…]"; one nothing asks for is dead weight.</summary>
    [Test]
    public static void EveryKeyTheCodeUsesExistsAndNoneAreLeftOver()
    {
        string src = Path.Combine(RepoRoot(), "src", "WhoCarried");
        HashSet<string> used = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(src, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"(WHO_CARRIED\\.[a-z0-9_.]+)\"").Select(m => m.Groups[1].Value))
            .ToHashSet();
        HashSet<string> english = Loc.Read("eng").Keys.ToHashSet();
        Check.Equal("", string.Join(", ", used.Except(english).Order()), "used but missing from eng.json");
        Check.Equal("", string.Join(", ", english.Except(used).Order()), "in eng.json but never used");
    }

    [Test]
    public static void NoCatalogListsAKeyTwice()
    {
        // Loading keeps one of the two quietly, so a duplicate would hide a stale translation.
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "WhoCarried", "Localization"), "*.json"))
        {
            using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(file));
            IEnumerable<string> twice = catalog.RootElement.EnumerateObject().GroupBy(p => p.Name).Where(g => g.Count() > 1).Select(g => g.Key);
            Check.Equal("", string.Join(", ", twice), Path.GetFileName(file));
        }
    }

    [Test]
    public static void TheSavedImagesDateReadsNaturallyInBothLanguages()
    {
        var day = new DateTime(2026, 9, 23);
        try
        {
            Check.Equal("23 September 2026", Loc.Text("WHO_CARRIED.summary.date", day), "English");
            var chinese = Loc.Read("zhs");
            Loc.Lookup = key => chinese.GetValueOrDefault(key);
            Check.Equal("2026年9月23日", Loc.Text("WHO_CARRIED.summary.date", day), "Chinese");
        }
        finally { Loc.Lookup = null; }
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WhoCarried.sln"))) return dir.FullName;
        throw new InvalidOperationException("WhoCarried.sln isn't above " + AppContext.BaseDirectory);
    }

    [Test]
    public static void DebuffCostAmountsUseCompleteTemplates()
    {
        try
        {
            foreach (string language in new[] { "eng", "zhs" })
            {
                var catalog = Loc.Read(language);
                Loc.Lookup = key => catalog.GetValueOrDefault(key);
                foreach (string suffix in new[] { "extra_taken", "less_dealt", "less_block" })
                {
                    string key = "WHO_CARRIED.debuffs." + suffix;
                    Check.True(catalog[key].Contains("{0}"), key + " owns amount placement");
                    Check.Equal(string.Format(CultureInfo.InvariantCulture, catalog[key], 42), Loc.Text(key, 42));
                }
            }
            Loc.Lookup = _ => "loss: {0}";
            Check.Equal("loss: 42", Loc.Text("WHO_CARRIED.debuffs.less_dealt", 42));
        }
        finally { Loc.Lookup = null; }
    }

    [Test]
    public static void MissingMalformedAndThrowingTranslationsFallBackSafely()
    {
        try
        {
            Loc.Lookup = _ => null;
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Loc.Lookup = _ => "";
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Loc.Lookup = _ => "broken {";
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Loc.Lookup = _ => throw new InvalidOperationException();
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
            Check.Equal("[WHO_CARRIED.missing]", Loc.Text("WHO_CARRIED.missing"));
            Check.Equal("[WHO_CARRIED.summary.players]", Loc.Text("WHO_CARRIED.summary.players"));
            Check.Equal(0, Loc.Read("missing_language").Count);
        }
        finally { Loc.Lookup = null; }
    }

    [Test]
    public static void ChineseTemplatesMayReorderArgumentsAndSwitchBackToEnglish()
    {
        try
        {
            var chinese = Loc.Read("zhs");
            Loc.Lookup = key => chinese.GetValueOrDefault(key);
            Check.Equal("关闭", Loc.Text("WHO_CARRIED.action.close"));
            Check.Equal("在第 43 层击败首领，15 场战斗共造成 100 点伤害。",
                Loc.Text("WHO_CARRIED.summary.won", "首领", 43, 100, "15 场战斗"));
            Loc.Lookup = null;
            Check.Equal("Close", Loc.Text("WHO_CARRIED.action.close"));
        }
        finally { Loc.Lookup = null; }
    }
}
