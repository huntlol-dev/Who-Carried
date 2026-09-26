namespace WhoCarried.Core;

/// <summary>Direct gifts name their contributor; a reaction can name a recipient as creator instead.</summary>
public static class CardCreationCredit
{
    public static ulong? Resolve(ulong? creator, ulong? recipient, bool observedReaction, ulong? reactionContributor)
    {
        if (creator.HasValue && creator != recipient) return creator;
        return observedReaction ? reactionContributor : creator;
    }
}
