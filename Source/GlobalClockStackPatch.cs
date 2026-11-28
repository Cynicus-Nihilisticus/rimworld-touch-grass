using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TouchGrass
{
    /// <summary>
    /// Installs the mod's Harmony patches (the clock-stack patches below and the
    /// main-menu panel). RimWorld passes a shared vertical cursor through its
    /// bottom-right global controls. Drawing at that cursor and moving it upward
    /// makes later countdowns and letters reserve our HUD's height instead of
    /// overlapping it.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GlobalClockStackPatch
    {
        private const string HarmonyId =
            "Cynicus.TouchGrass.GlobalClockStack";

        private static bool installed;

        static GlobalClockStackPatch()
        {
            try
            {
                Harmony harmony = new Harmony(HarmonyId);
                harmony.PatchAll(typeof(GlobalClockStackPatch).Assembly);
                installed = true;
                Log.Message(
                    "[Touch Grass] Added timer to RimWorld's "
                    + "bottom-right global-controls stack.");
            }
            catch (Exception exception)
            {
                installed = false;
                Log.Error(string.Format(
                    "[Touch Grass] Could not integrate the timer "
                    + "with the global-controls stack. The custom-position "
                    + "fallback will be used.\n{0}",
                    exception));
            }
        }

        public static bool ShouldUseStack(TouchGrassSettings settings)
        {
            return installed
                && settings != null
                && settings.ShowHud
                && settings.HudAnchor == HudAnchor.BottomRight
                && SessionClock.IsPlayableColonyUiActive;
        }

        [HarmonyPatch(
            typeof(GlobalControlsUtility),
            "DoRealtimeClock")]
        private static class RealtimeClockPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                float leftX,
                float width,
                ref float curBaseY)
            {
                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                if (Prefs.ShowRealtimeClock && ShouldUseStack(settings))
                {
                    TimerGameComponent.DrawInGlobalControlsStack(
                        leftX,
                        width,
                        ref curBaseY,
                        false);
                }
            }
        }

        [HarmonyPatch(
            typeof(GlobalControlsUtility),
            "DoDate")]
        private static class DatePatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                float leftX,
                float width,
                ref float curBaseY)
            {
                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                if (!Prefs.ShowRealtimeClock && ShouldUseStack(settings))
                {
                    TimerGameComponent.DrawInGlobalControlsStack(
                        leftX,
                        width,
                        ref curBaseY,
                        true);
                }
            }
        }
    }
}
