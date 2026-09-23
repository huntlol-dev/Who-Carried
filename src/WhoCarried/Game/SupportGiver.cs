using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using WhoCarried.Core;

namespace WhoCarried.Game;

/// <summary>Gathers what the game can say about who gave something, for <see cref="SupportCredit.Giver"/> to decide.</summary>
internal static class SupportGiver
{
    /// <param name="named">A giver the game named itself; wins when there is one.</param>
    public static ulong? Find(IRunState? run, ulong? named = null)
    {
        ulong? turnEffect = EffectSources.Running is AbstractModel effect ? FactsExtractor.Candidate(effect).OwnerId : null;
        IReadOnlyCollection<ulong>? midEffect = MidEffect(run);
        ulong? actionOwner = null;
        // Only asked on a game without the card-effect check; a missing member there shouldn't lose the gift.
        if (midEffect == null)
        {
            try { actionOwner = GameCompat.RunningActionOwner(); }
            catch (Exception) { }
        }
        return SupportCredit.Giver(named, turnEffect, midEffect, actionOwner);
    }

    /// <summary>What was running, for the log: the turn hook's content, the card or potion in play, or "?".</summary>
    public static string Running()
    {
        if (EffectSources.Running is AbstractModel effect) return effect.Id.Entry;
        try { return GameCompat.RunningActionSource() ?? "?"; }
        catch (Exception) { return "?"; }
    }

    private static IReadOnlyCollection<ulong>? MidEffect(IRunState? run)
    {
        if (run == null || GameCompat.ExecutingCardOrPotion is not { } executing) return null;
        return run.Players.Where(p => executing(p)).Select(p => p.NetId).ToList();
    }
}
