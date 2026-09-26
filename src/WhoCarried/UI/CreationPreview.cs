using WhoCarried.Core;

namespace WhoCarried.UI;

/// <summary>Deterministic creation edge cases, used only by the opt-in developer preview.</summary>
internal static class CreationPreview
{
    // preview.flag examples: "1 eng creation-only", "4 zhs creation-many", "2 eng creation-live".
    public static RecapView Apply(RecapView view, string scenario, bool advanced = false)
    {
        var players = view.Creation.Select(p => new PlayerInfo(p.PlayerId, p.Label, "", p.ColorHex, p.IconKey ?? "")).ToArray();
        if (players.Length == 0) return view;
        var stats = new RunStats();
        ulong from = players[^1].NetId, to = players[0].NetId;
        var soul = new SourceRef(SourceKind.Card, "SOUL", "Soul / 灵魂");
        bool created = scenario != "creation-gifts" && scenario != "creation-empty" && (scenario != "creation-live" || advanced);
        if (created)
        {
            stats.RecordCardGeneration(from, from, soul, 5);
            if (scenario != "creation-only") stats.RecordCardGeneration(from, to, soul, 2);
        }
        if (scenario == "creation-gifts") stats.RecordSupport(from, to, SupportKind.Block, 20);
        if (scenario == "creation-many")
        {
            for (int i = 0; i < 16; i++)
                stats.RecordCardGeneration(from, to, new SourceRef(SourceKind.Card, "PREVIEW-" + i,
                    i < 2 ? "Twin / 同名卡牌" : "A generated card with a deliberately long name / 一张名称很长的生成卡牌 " + i), i + 1);
        }
        if (scenario == "creation-tall")
            foreach (PlayerInfo player in players)
                for (int i = 0; i < 128; i++)
                    stats.RecordCardGeneration(player.NetId, to, new SourceRef(SourceKind.Card, "TALL-" + i,
                        "Generated card with a long label / 名称很长的生成卡牌 " + i), i + 1);
        var built = RecapBuilder.Build(stats, players, new Dictionary<ulong, DefenseTotals>(), view.Header);
        return view with { CreationRows = built.Creation, SupportRows = built.Support, Overview = built.Overview };
    }
}
