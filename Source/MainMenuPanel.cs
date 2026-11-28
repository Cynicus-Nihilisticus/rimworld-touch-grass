using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TouchGrass
{
    /// <summary>
    /// A small panel on the main menu for choosing this session's limit before a
    /// colony is loaded, because the policy locks on the first playable frame.
    /// </summary>
    internal static class MainMenuPanel
    {
        private const float Width = 330f;
        private const float Padding = 12f;
        private const float RightEdgeOffset = 150f;

        private static readonly int[] PresetMinutes = { 30, 60, 120, 180 };
        private static readonly string[] PresetLabels =
        {
            "30 min",
            "1 hour",
            "2 hours",
            "3 hours"
        };

        public static void Draw()
        {
            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            if (settings == null || Find.WindowStack.Windows.Count > 0)
            {
                return;
            }

            bool locked = SessionClock.IsPolicyLocked;
            float height = locked ? 132f : 150f;
            // Under the vanilla buttons: the menu is anchored 150px from the
            // right edge and is as wide as this panel, so the left edge lines
            // up with the buttons and the right edge with the links column.
            Rect panel = new Rect(
                UI.screenWidth - RightEdgeOffset - Width,
                Mathf.Min(UI.screenHeight * 0.68f, UI.screenHeight - height - 20f),
                Width,
                height);
            TouchGrassStyle.DrawPanel(panel, TouchGrassStyle.Gold);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;

            float y = panel.y + Padding;
            GUI.color = TouchGrassStyle.Gold;
            Widgets.Label(
                new Rect(panel.x + Padding, y, Width - Padding * 2f, 22f),
                "TOUCH GRASS: THIS SESSION");
            y += 26f;

            GUI.color = TouchGrassStyle.Muted;
            Widgets.Label(
                new Rect(panel.x + Padding, y, 60f, 22f),
                "Limit");
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = TouchGrassStyle.Cyan;
            Widgets.Label(
                new Rect(panel.x + Padding + 40f, y, Width - Padding * 2f - 40f, 22f),
                Summary());
            Text.Anchor = TextAnchor.MiddleLeft;
            y += 28f;

            if (locked)
            {
                GUI.color = TouchGrassStyle.Warning;
                Widgets.Label(
                    new Rect(panel.x + Padding, y, Width - Padding * 2f, 20f),
                    "Locked until RimWorld is restarted.");
            }
            else
            {
                GUI.color = oldColor;
                DrawPresets(settings, panel.x + Padding, y, Width - Padding * 2f);
                y += 36f;
            }

            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;

            Rect more = new Rect(
                panel.x + Padding,
                panel.yMax - Padding - 28f,
                Width - Padding * 2f,
                28f);
            if (TouchGrassStyle.Button(
                more,
                locked ? "View settings" : "More options (stop time, snoozes, windows)"))
            {
                Find.WindowStack.Add(
                    new Dialog_ModSettings(
                        LoadedModManager.GetMod<TouchGrassMod>()));
            }
        }

        private static void DrawPresets(
            TouchGrassSettings settings,
            float x,
            float y,
            float width)
        {
            const float gap = 6f;
            float cell = (width - gap * (PresetMinutes.Length - 1))
                / PresetMinutes.Length;
            int current = settings.LimitMode == SessionLimitMode.Duration
                ? settings.DurationHours * 60 + settings.DurationMinutes
                : -1;
            for (int i = 0; i < PresetMinutes.Length; i++)
            {
                Rect rect = new Rect(x + i * (cell + gap), y, cell, 30f);
                if (TouchGrassStyle.Button(
                    rect,
                    PresetLabels[i],
                    current == PresetMinutes[i],
                    false,
                    TouchGrassStyle.Cyan))
                {
                    settings.LimitMode = SessionLimitMode.Duration;
                    settings.DurationHours = PresetMinutes[i] / 60;
                    settings.DurationMinutes = PresetMinutes[i] % 60;
                    settings.Write();
                }
            }
        }

        private static string Summary()
        {
            if (SessionClock.EffectiveLimitMode == SessionLimitMode.Duration)
            {
                return SessionClock.DescribeDuration(
                    SessionClock.EffectiveDurationLimit) + " of active play";
            }

            return string.Format(
                "stop at {0:00}:{1:00}",
                SessionClock.EffectiveStopHour,
                SessionClock.EffectiveStopMinute);
        }
    }

    [HarmonyPatch(typeof(MainMenuDrawer), "MainMenuOnGUI")]
    internal static class MainMenuPanelPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                MainMenuPanel.Draw();
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Touch Grass] Main-menu panel failed: " + exception,
                    90210);
            }
        }
    }
}
