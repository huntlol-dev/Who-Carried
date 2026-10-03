using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class RunOriginTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "whocarried-tests", Guid.NewGuid().ToString("N"), "runs");

    [Test]
    public static void ASavedRunCountsAsLoadedEvenAtReloadCountZero()
    {
        var origin = new RunOrigin();
        origin.SetUpSaved();
        Check.True(origin.TakeLoadedFromSave(numReloads: 0), "loaded");
    }

    /// <summary>
    /// The public branch's guest on a run's first reload: its own clock stamped the new run, the save carries the host's,
    /// and its reload count is still 0 because only the host bumps it there.
    /// </summary>
    [Test]
    public static void AGuestsFirstReloadOnThePublicBranchResumes()
    {
        var runs = new RunFolders(NewRoot());
        var stats = new RunStats { RunKey = "SEED:1789580126" };
        stats.BeginFight(1, 2, "Cultist");
        stats.RecordDamage(1, new SourceRef(SourceKind.Card, "STRIKE", "Strike"), 6);
        stats.EndFight();
        RunStatsStore.Save(stats, Path.Combine(runs.Create(stats.RunKey, DateTime.Now), RunFolders.StatsFile));

        var origin = new RunOrigin();
        origin.SetUpSaved();
        RunStats? resumed = runs.FindResumable("SEED:1789580128", origin.TakeLoadedFromSave(numReloads: 0))?.Stats;

        Check.True(resumed != null, "resumed");
        Check.Equal(1, resumed!.Fights.Count, "fights kept");
        Check.Equal("SEED:1789580128", resumed.RunKey, "takes the host's key");
    }

    [Test]
    public static void ANewRunIsNotLoaded()
    {
        var origin = new RunOrigin();
        origin.SetUpNew();
        Check.True(!origin.TakeLoadedFromSave(numReloads: 0), "new");
    }

    [Test]
    public static void WithNoSetUpSeenTheReloadCountDecides()
    {
        Check.True(!new RunOrigin().TakeLoadedFromSave(numReloads: 0), "count 0: new");
        Check.True(new RunOrigin().TakeLoadedFromSave(numReloads: 2), "count 2: loaded");
    }

    [Test]
    public static void TheAnswerIsForOneRunOnly()
    {
        var origin = new RunOrigin();
        origin.SetUpSaved();
        origin.TakeLoadedFromSave(numReloads: 0);
        Check.True(!origin.TakeLoadedFromSave(numReloads: 0), "a later run with no set-up seen falls back to the count");
    }
}
