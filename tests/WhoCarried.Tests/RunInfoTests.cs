using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunInfoTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), RunFolders.InfoFile);

    [Test]
    public static void RoundTripKeepsEverything()
    {
        string path = TempFile();
        var info = new RunInfo
        {
            RunKey = "CEKDYSK8A9AB:1790705897",
            WhoCarried = "1.4.0",
            GameBuild = "v0.111.0",
            Language = "zhs",
            LocalPlayer = "76561198007503242",
            Profile = "3",
            Mods = { new RunInfo.ModVersion { Id = "IntoTheSpireverse", Version = "1.2.0" } },
            Players =
            {
                new RunInfo.PlayerEntry
                {
                    Id = "76561198291203680", Name = "CenturionVII", CharacterId = "NECROBINDER",
                    CharacterName = "The Necrobinder", Colour = "ee82ee",
                },
            },
            Written = "2026-10-03T19:00:00",
        };
        RunInfo.Save(info, path);
        RunInfo? loaded = RunInfo.Load(path);
        Check.True(loaded != null, "loaded");
        Check.Equal(RunInfo.CurrentFormat, loaded!.Format, "format");
        Check.Equal("CEKDYSK8A9AB:1790705897", loaded.RunKey, "run key");
        Check.Equal("v0.111.0", loaded.GameBuild, "build");
        Check.Equal("zhs", loaded.Language, "language");
        Check.Equal("3", loaded.Profile, "profile");
        Check.Equal("1.2.0", loaded.Mods[0].Version, "mod version");
        Check.Equal("CenturionVII", loaded.Players[0].Name, "player name");
        Check.Equal("ee82ee", loaded.Players[0].Colour, "colour");
    }

    [Test]
    public static void MissingFieldsReadAsUnknown()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"RunKey\": \"SEED:1\" }");
        RunInfo? loaded = RunInfo.Load(path);
        Check.Equal("SEED:1", loaded!.RunKey, "run key");
        Check.Equal(RunInfo.Unknown, loaded.GameBuild, "build");
        Check.Equal(RunInfo.Unknown, loaded.Profile, "profile");
        Check.Equal(0, loaded.Mods.Count, "mods");
    }

    [Test]
    public static void UnreadableOrMissingFileGivesNull()
    {
        string path = TempFile();
        Check.True(RunInfo.Load(path) == null, "missing");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Check.True(RunInfo.Load(path) == null, "corrupt");
    }

    [Test]
    public static void ModsLineListsEachModAndItsVersion()
    {
        Check.Equal("mods: BaseLib 0.3.1, ZoneTheSpire unknown",
            RunInfo.ModsLine(new[]
            {
                new RunInfo.ModVersion { Id = "BaseLib", Version = "0.3.1" },
                new RunInfo.ModVersion { Id = "ZoneTheSpire", Version = RunInfo.Unknown },
            }), "two mods");
        Check.Equal("mods: none", RunInfo.ModsLine(Array.Empty<RunInfo.ModVersion>()), "none");
    }
}
