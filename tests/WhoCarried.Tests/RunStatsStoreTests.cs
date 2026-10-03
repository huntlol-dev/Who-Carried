using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunStatsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "current_run.json");

    private static RunStats Sample(string key = "SEED:123")
    {
        var s = new RunStats { RunKey = key };
        s.BeginFight(1, 2, "Cultist");
        s.RecordDamage(1, new SourceRef(SourceKind.Power, "POISON_POWER", "Poison"), 9);
        s.EndFight();
        s.RecordBlocked(1, 3);
        s.RecordPetTanked(1, 6);
        return s;
    }

    [Test]
    public static void RoundTripKeepsEverything()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample(), path);
        RunStats? loaded = RunStatsStore.Load(path);
        Check.True(loaded != null, "loaded");
        Check.Equal(9, loaded!.Get(1)!.DamageDealt, "damage");
        Check.Equal(3, loaded.Get(1)!.Blocked, "blocked");
        Check.Equal(6, loaded.Get(1)!.PetTanked, "pet tanked");
        SourceTotal poison = loaded.Get(1)!.Sources["Power:POISON_POWER"];
        Check.Equal(SourceKind.Power, poison.Kind, "kind");
        Check.Equal("Poison", poison.Label, "label");
        Check.Equal(1, loaded.Fights.Count, "fights");
        Check.Equal("Cultist", loaded.Fights[0].Label, "fight label");
        Check.Equal(9, loaded.Fights[0].DamageByPlayer["1"], "fight damage");
    }

    [Test]
    public static void EnumsAreWrittenAsNames()
    {
        string path = TempFile();
        RunStatsStore.Save(Sample(), path);
        Check.True(File.ReadAllText(path).Contains("\"Power\""), "kind written as \"Power\"");
    }

    [Test]
    public static void CorruptOrMissingFileIsIgnored()
    {
        string path = TempFile();
        Check.True(RunStatsStore.Load(path) == null, "missing file");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Check.True(RunStatsStore.Load(path) == null, "corrupt file");
    }
}
