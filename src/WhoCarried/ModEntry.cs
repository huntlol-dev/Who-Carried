using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using WhoCarried.Game;
using WhoCarried.UI;

namespace WhoCarried;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    /// <summary>The manifest's version, which the build stamps on the assembly.</summary>
    public static readonly string Version =
        typeof(ModEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    private static readonly Type[] PatchClasses =
    {
        typeof(AfterDamageGivenPatch),
        typeof(DoomKillPatch),
        typeof(AfterPowerAmountChangedPatch),
        typeof(BeforeDamageReceivedPatch),
        typeof(ModifyDamagePatch),
        typeof(ModifyHpLostPatch),
        typeof(BeforeBlockGainedPatch),
        typeof(AfterBlockGainedPatch),
        typeof(GainEnergyPatch),
        typeof(AfterCardDrawnPatch),
        typeof(CardGeneratedPatch),
        typeof(BeforeCombatStartPatch),
        typeof(AfterCombatEndPatch),
        typeof(RunEndedPatch),
        typeof(NewRunSetUpPatch),
        typeof(SavedRunSetUpPatch),
        typeof(GameOverScreenPatch),
        typeof(TopBarPatch),
        typeof(TopBarNavigationPatch),
        typeof(DamageCommandPatch),
    };

    public static void Initialize()
    {
        string modDir = Path.GetDirectoryName(typeof(ModEntry).Assembly.Location) ?? ".";
        ModLocalization.Install();
        Tracker.Init(modDir);

        var harmony = new Harmony("whocarried");
        int applied = 0;
        foreach (Type patchClass in PatchClasses)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                applied++;
            }
            catch (Exception e)
            {
                Log.Error($"[WhoCarried] patch {patchClass.Name} failed: {e.Message}");
            }
        }

        RunManager.Instance.RunStarted += run =>
        {
            try { Tracker.OnRunStarted(run); }
            catch (Exception e) { Tracker.LogError("RunStarted", e); }
            // Once every mod's content is registered; after the run's log starts, so its summary lands in it.
            try { EffectSources.InstallOnce(Tracker.DataDir); }
            catch (Exception e) { Tracker.LogError("effect sources", e); }
        };

        RecapUi.Install();
        Log.Info($"[WhoCarried] loaded v{Version}: {applied}/{PatchClasses.Length} patches applied");
        try { GameCompat.LogAtStart(); }
        catch (Exception e) { Log.Warn($"[WhoCarried] game API check failed: {e.Message}"); }
    }
}
