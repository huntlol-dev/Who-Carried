using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>Deterministic Cards given edge cases, used only by the opt-in developer preview.</summary>
internal static class CardGiftPreview
{
    // preview.flag examples: "1 eng gifts-self", "4 zhs gifts-many", "2 eng gifts-live".
    public static RecapView Apply(RecapView view, string scenario, bool advanced = false)
    {
        var players = view.CardGifts.Select(p => new PlayerInfo(p.PlayerId, p.Label, "", p.ColorHex, p.IconKey ?? "")).ToArray();
        if (players.Length == 0) return view;
        var stats = new RunStats();
        ulong from = players[^1].NetId, to = players[0].NetId;
        var soul = new SourceRef(SourceKind.Card, "SOUL", "Soul / 灵魂");
        // Five Souls for the maker, which aren't listed, and (except in gifts-self) two for a teammate, which are.
        bool made = scenario != "gifts-other" && scenario != "gifts-empty" && (scenario != "gifts-live" || advanced);
        if (made)
        {
            stats.RecordCardGeneration(from, from, soul, 5);
            if (scenario != "gifts-self") stats.RecordCardGeneration(from, to, soul, 2);
        }
        if (scenario == "gifts-other") stats.RecordSupport(from, to, SupportKind.Block, 20);
        if (scenario == "gifts-many")
        {
            for (int i = 0; i < 16; i++)
                stats.RecordCardGeneration(from, to, new SourceRef(SourceKind.Card, "PREVIEW-" + i,
                    i < 2 ? "Twin / 同名卡牌" : "A generated card with a deliberately long name / 一张名称很长的生成卡牌 " + i), i + 1);
        }
        if (scenario == "gifts-tall")
            foreach (PlayerInfo player in players)
                for (int i = 0; i < 128; i++)
                    stats.RecordCardGeneration(player.NetId, player.NetId == to ? from : to, new SourceRef(SourceKind.Card, "TALL-" + i,
                        "Generated card with a long label / 名称很长的生成卡牌 " + i), i + 1);
        var built = RecapBuilder.Build(stats, players, new Dictionary<ulong, DefenseTotals>(), view.Header);
        // The awards too, so a support card's award line names a player who gave that kind of help in this case.
        return view with { CardGiftRows = built.CardGifts, SupportRows = built.Support, Overview = built.Overview, Awards = built.Awards };
    }
}
