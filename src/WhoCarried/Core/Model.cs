namespace WhoCarried.Core;

public enum SourceKind { Card, Power, Relic, Potion, Orb, Pet, Monster, Other, Unknown }

/// <summary>What caused a piece of damage. <see cref="Key"/> merges repeat hits from the same source.</summary>
public sealed record SourceRef(SourceKind Kind, string Id, string Label)
{
    public static SourceRef Unknown => new(SourceKind.Unknown, "UNKNOWN", WhoCarried.Localization.Loc.Text("WHO_CARRIED.sources.unknown"));

    public string Key => $"{Kind}:{Id}";
}

/// <summary>A possible source of a hit, plus the player who owns it (if any).</summary>
public sealed record SourceCandidate(SourceRef Source, ulong? OwnerId);

/// <summary>Plain facts about one damage result, extracted from game objects by Game/FactsExtractor.</summary>
/// <param name="Effect">
/// For a hit with no dealer: the game content that was running when its damage started (see <see cref="EffectScopes"/>).
/// </param>
/// <param name="TargetHpInfinite">The target health bar shows infinite: an enemy in a phase where it cannot die.</param>
public sealed record DamageFacts(
    int HpRemoved,
    int Blocked,
    bool TargetIsEnemy,
    ulong? TargetPlayerId,
    ulong? DealerPlayerId,
    SourceCandidate? Pet,
    SourceCandidate? Card,
    SourceCandidate? StackTop,
    SourceCandidate? Fallback,
    SourceCandidate? Effect = null,
    bool TargetHpInfinite = false);

/// <summary>A player as shown in the recap. CharacterId (e.g. "IRONCLAD") is used to look up the character icon.</summary>
public sealed record PlayerInfo(ulong NetId, string Name, string Character, string ColorHex, string CharacterId = "");
