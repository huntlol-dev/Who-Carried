using System.Globalization;
using System.Text.RegularExpressions;

namespace WhoCarried.Core;

/// <summary>
/// Rebuilds a run's stats from the events.log the mod wrote while it was played, applying today's rules: block
/// removed is kept apart from damage dealt, and pet attacks are split by what triggered them. Stats an older log never
/// recorded (cards created, what enemy debuffs cost, Strength-loss prevention, in-fight HP lows, support given to teammates) stay empty.
/// </summary>
public static class LogReplay
{
    public sealed record Result(RunStats Stats, IReadOnlyList<PlayerInfo> Players, bool? Victory, string RunKey);

    private const string Where = @"^\[F(\d+) A(\d+)\] ";
    // Logs written before the rename start with "Run Recap".
    private static readonly Regex Header = new(@"^(?:Who Carried|Run Recap) v\S+ - run (\S+) started", RegexOptions.Compiled);
    private static readonly Regex Resumed = new(@"^--- resumed run (\S+): \d+ fights restored", RegexOptions.Compiled);
    private static readonly Regex Player = new(@"^player (\d+) = (.+) \((.*)\) #([0-9a-fA-F]{6})$", RegexOptions.Compiled);
    private static readonly Regex FightStart = new(Where + @"fight start: (.*?)(?: \[(\w+)\])?$", RegexOptions.Compiled);
    private static readonly Regex FightEnd = new(Where + @"fight end", RegexOptions.Compiled);
    private static readonly Regex Hit = new(Where + @"(.+?) <- (\w+):(\S+) \((.*)\) (\d+) hp \| target (.*?), blocked (\d+), dealer (.*), stack \[(.*)\]$", RegexOptions.Compiled);
    // Doom's, and any other effect's that kills outright (Zone the Spire's Hallowed).
    private static readonly Regex Kill = new(Where + @"(.+?) <- (\w+):(\S+) \((.*)\) (\d+) hp \| target (.*), (?:doom|direct) kill$", RegexOptions.Compiled);
    private static readonly Regex Applied = new(Where + @"(.+?) applied (\d+) (\S+) \((.*)\) \| target", RegexOptions.Compiled);
    private static readonly Regex Received = new(Where + @"(.+?) received (\d+) (\S+) \((.*)\) \| applier", RegexOptions.Compiled);
    private static readonly Regex Bonus = new(Where + @"(.+?) \+(\d+) bonus via (\S+) \((.*)\) on ", RegexOptions.Compiled);
    private static readonly Regex Prevented = new(Where + @"(.+?) prevented (\d+) via (\S+) \((.*)\) \|", RegexOptions.Compiled);
    private static readonly Regex PetTook = new(Where + @"(.+?) pet (\S+) took (\d+) hp \|", RegexOptions.Compiled);
    private static readonly Regex RunEnded = new(Where + @"run ended: (victory|defeat)", RegexOptions.Compiled);
    private static readonly Regex HpLow = new(Where + @"(.+?) hp low (\d+)/(\d+)$", RegexOptions.Compiled);
    private static readonly Regex Badge = new(Where + @"(.+?) badge (\S+) \((\w+)\)$", RegexOptions.Compiled);
    private static readonly Regex Gave = new(Where + @"(.+?) gave (\d+) (energy|cards|block|buffs|draws) to (.+?) \| (.*)$", RegexOptions.Compiled);

    /// <summary>The first line of a run's events.log, which <see cref="Parse"/> reads the run key back from.</summary>
    public static string HeaderLine(string version, string runKey, DateTime started) =>
        $"Who Carried v{version} - run {runKey} started {started.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>Written when a saved run is picked up again. Its key wins over the header's: see <see cref="RunStatsStore.LoadIfResumable"/>.</summary>
    public static string ResumedLine(string runKey, int fightsRestored) =>
        $"--- resumed run {runKey}: {fightsRestored} fights restored ---";

    /// <summary>A player's pet losing HP to an enemy (after its "[F.. A..] " prefix), which <see cref="Parse"/> reads back.</summary>
    public static string PetTookLine(string owner, string petId, int hp, string dealer) =>
        $"{owner} pet {petId} took {hp} hp | dealer {dealer}";

    /// <summary>
    /// Help one player gave another (after its "[F.. A..] " prefix), which <see cref="Parse"/> reads back.
    /// <paramref name="source"/> is the id of what gave it, or "?".
    /// </summary>
    public static string SupportLine(string giver, string recipient, SupportKind kind, int amount, string source) =>
        $"{giver} gave {amount} {SupportWord(kind)} to {recipient} | {source}";

    /// <summary>The log's word for a kind of support: "energy", "cards", "block", "buffs", "draws".</summary>
    public static string SupportWord(SupportKind kind) => kind.ToString().ToLowerInvariant();

    /// <param name="title">Display name for a model id (a card that made a pet attack); null falls back to the id.</param>
    public static Result Parse(IEnumerable<string> lines, Func<string, string?>? title = null)
    {
        var stats = new RunStats();
        var players = new List<PlayerInfo>();
        var byName = new Dictionary<string, ulong>(StringComparer.Ordinal);
        bool? victory = null;
        string runKey = "";
        var badges = new List<(ulong Player, EarnedBadge Badge)>();

        ulong? Who(string name) => byName.TryGetValue(name, out ulong id) ? id : null;
        static int Int(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

        foreach (string line in lines)
        {
            Match m;
            if ((m = Header.Match(line)).Success || (m = Resumed.Match(line)).Success)
            {
                runKey = m.Groups[1].Value;
            }
            else if ((m = Player.Match(line)).Success)
            {
                ulong id = ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (players.Any(p => p.NetId == id)) continue; // listed again after a Save & Quit resume
                players.Add(new PlayerInfo(id, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value.ToLowerInvariant()));
                byName[m.Groups[2].Value] = id;
            }
            else if ((m = FightStart.Match(line)).Success)
            {
                stats.BeginFight(Int(m.Groups[2]), Int(m.Groups[1]), m.Groups[3].Value, m.Groups[4].Value);
            }
            else if (FightEnd.IsMatch(line))
            {
                stats.EndFight();
            }
            else if ((m = Hit.Match(line)).Success)
            {
                SourceRef source = Source(m.Groups[4].Value, m.Groups[5].Value, m.Groups[6].Value, m.Groups[11].Value, title);
                stats.RecordDamage(Who(m.Groups[3].Value), source, Int(m.Groups[7]), Int(m.Groups[9]));
            }
            else if ((m = Kill.Match(line)).Success)
            {
                SourceRef source = Source(m.Groups[4].Value, m.Groups[5].Value, m.Groups[6].Value, "", title);
                stats.RecordDamage(Who(m.Groups[3].Value), source, Int(m.Groups[7]));
            }
            else if ((m = Applied.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id)
                    stats.RecordDebuffApplied(id, Power(m.Groups[5].Value, m.Groups[6].Value), Int(m.Groups[4]));
            }
            else if ((m = Received.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id)
                    stats.RecordDebuffReceived(id, Power(m.Groups[5].Value, m.Groups[6].Value), Int(m.Groups[4]));
            }
            else if ((m = Bonus.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id)
                    stats.RecordDebuffBonus(id, Power(m.Groups[5].Value, m.Groups[6].Value), Int(m.Groups[4]));
            }
            else if ((m = Prevented.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id)
                    stats.RecordDebuffPrevented(id, Power(m.Groups[5].Value, m.Groups[6].Value), Int(m.Groups[4]));
            }
            else if ((m = PetTook.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id) stats.RecordPetTanked(id, Int(m.Groups[5]));
            }
            else if ((m = Gave.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong from && Who(m.Groups[6].Value) is ulong to)
                    stats.RecordSupport(from, to, Enum.Parse<SupportKind>(m.Groups[5].Value, ignoreCase: true), Int(m.Groups[4]));
            }
            else if ((m = RunEnded.Match(line)).Success)
            {
                // A victory is final: some mods end the run a second time as a defeat.
                if (m.Groups[3].Value == "victory") victory = true;
                else victory ??= false;
            }
            else if ((m = HpLow.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id) stats.RecordHp(id, Int(m.Groups[4]), Int(m.Groups[5]));
            }
            else if ((m = Badge.Match(line)).Success)
            {
                if (Who(m.Groups[3].Value) is ulong id) badges.Add((id, new EarnedBadge { Id = m.Groups[4].Value, Rarity = m.Groups[5].Value }));
            }
        }

        foreach (IGrouping<ulong, (ulong Player, EarnedBadge Badge)> player in badges.GroupBy(b => b.Player))
            stats.SetBadges(player.Key, player.Select(b => b.Badge));

        stats.RunKey = runKey;
        stats.Finished = victory != null;
        stats.Victory = victory;
        return new Result(stats, players, victory, runKey);
    }

    private static SourceRef Power(string id, string label) => new(SourceKind.Power, id, label);

    /// <summary>The logged source, with a pet's attack split by the model on top of the stack ("Osty via Unleash").</summary>
    private static SourceRef Source(string kind, string id, string label, string stack, Func<string, string?>? title)
    {
        SourceKind parsed = Enum.TryParse(kind, out SourceKind k) ? k : SourceKind.Unknown;
        var source = new SourceRef(parsed, id, label);
        if (parsed != SourceKind.Pet || id.Contains(Attribution.ViaSeparator)) return source;
        string top = stack.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        if (top.Length == 0 || top == id) return source;
        return Attribution.PetVia(source, new SourceRef(SourceKind.Card, top, title?.Invoke(top) ?? Pretty(top)));
    }

    /// <summary>"INTOTHESPIREVERSE-WILD_STRIKE" → "Wild Strike".</summary>
    public static string Pretty(string id)
    {
        string entry = id.Contains('-') ? id[(id.LastIndexOf('-') + 1)..] : id;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(entry.Replace('_', ' ').ToLowerInvariant());
    }
}
