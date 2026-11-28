using System;
using UnityEngine;
using Verse;

namespace TouchGrass
{
    public sealed class TimerGameComponent : GameComponent
    {
        private static long cachedHudSecond = long.MinValue;
        private static int cachedDisplayVersion = -1;
        private static SessionLimitMode cachedLimitMode;
        private static long cachedDurationTicks = -1;
        private static int cachedStopHour = -1;
        private static int cachedStopMinute = -1;
        private static bool cachedDrawOwnRealTime;
        private static int cachedHudColorState;
        private static float cachedHudFraction;
        private static string cachedHudText = string.Empty;

        // RimWorld's own clock and date readouts keep a 7px margin to the
        // right screen edge; matching it puts our text on the same edge.
        private const float StackRightInset = 7f;

        // A thin progress meter sits under the text. It shows time used (a
        // snooze drains instead) and shares the text colour: cyan on track,
        // gold in the last quarter of the limit (at most ten minutes), orange
        // while snoozing, red once over.
        private const float MeterBlock = 12f;
        private const float MeterWidth = 150f;
        private const double SnoozeSeconds = 5d * 60d;
        private const int HudTooltipId = 1904722711;

        private static readonly Func<string> HudTooltipGetter =
            BuildHudTooltip;

        public TimerGameComponent(Game game)
        {
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            SessionClock.NotifyGameLoaded();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            SessionClock.NotifyGameLoaded();
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            SessionClock.ObservePlayableFrame();
        }

        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();

            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            if (settings == null || !settings.ShowHud)
            {
                return;
            }

            // The game already exists while a new world and starting site are
            // being chosen. Draw nothing until a colony is playable, so the HUD
            // and its tooltip cannot get in the way of other mods' windows there.
            if (!SessionClock.IsPlayableColonyUiActive)
            {
                return;
            }

            if (GlobalClockStackPatch.ShouldUseStack(settings))
            {
                // The Harmony postfix draws this HUD inside RimWorld's shared
                // global-controls stack and reserves space for it there.
                return;
            }

            DrawTimerHud(settings);
        }

        internal static void DrawInGlobalControlsStack(
            float x,
            float width,
            ref float curBaseY,
            bool drawOwnRealTime)
        {
            float height = HudHeight(drawOwnRealTime);
            curBaseY -= height;
            DrawTimerReadout(
                new Rect(x, curBaseY, width - StackRightInset, height),
                HudAnchor.BottomRight,
                drawOwnRealTime);
        }

        private static void DrawTimerHud(TouchGrassSettings settings)
        {
            bool drawOwnRealTime = !Prefs.ShowRealtimeClock;
            // These match RimWorld's GlobalControls width and DateReadout
            // right padding so the extra lines align with its existing clock.
            const float width = 200f;
            float height = HudHeight(drawOwnRealTime);
            Rect outer = PositionHud(settings, width, height);
            DrawTimerReadout(outer, settings.HudAnchor, drawOwnRealTime);
        }

        private static bool MeterEnabled()
        {
            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            return settings == null || settings.ShowHudMeter;
        }

        private static float HudHeight(bool drawOwnRealTime)
        {
            return (drawOwnRealTime ? 66f : 44f)
                + (MeterEnabled() ? MeterBlock : 0f);
        }

        private static Color HudColor(int state)
        {
            switch (state)
            {
                case 1:
                    return TouchGrassStyle.Negative;
                case 2:
                    return TouchGrassStyle.Warning;
                case 3:
                    return TouchGrassStyle.Gold;
                default:
                    return TouchGrassStyle.Cyan;
            }
        }

        private static float MeterX(Rect outer, HudAnchor anchor)
        {
            float width = Mathf.Min(MeterWidth, outer.width);
            switch (anchor)
            {
                case HudAnchor.TopLeft:
                case HudAnchor.BottomLeft:
                    return outer.x;
                case HudAnchor.TopCenter:
                case HudAnchor.BottomCenter:
                    return outer.x + (outer.width - width) / 2f;
                default:
                    return outer.xMax - width;
            }
        }

        private static void DrawTimerReadout(
            Rect outer,
            HudAnchor anchor,
            bool drawOwnRealTime)
        {
            // The HUD is non-interactive. RimWorld still reserves its stack
            // height on every GUI event, but text work is only needed to repaint.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            DateTime now = DateTime.Now;
            RefreshHudCache(now, drawOwnRealTime);
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = HorizontalAnchor(anchor);
            Text.WordWrap = false;
            GUI.color = HudColor(cachedHudColorState);
            Widgets.Label(outer, cachedHudText);

            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            if (MeterEnabled())
            {
                TouchGrassStyle.DrawMeter(
                    new Rect(
                        MeterX(outer, anchor),
                        outer.yMax - 9f,
                        Mathf.Min(MeterWidth, outer.width),
                        6f),
                    cachedHudFraction,
                    HudColor(cachedHudColorState));
            }

            // The tooltip text is only built while the pointer is over the
            // readout, so it costs nothing the rest of the time. A stable id
            // plus a getter lets the text tick without restarting the tooltip.
            if (Mouse.IsOver(outer))
            {
                TooltipHandler.TipRegion(
                    outer,
                    HudTooltipGetter,
                    HudTooltipId);
            }
        }

        private static string BuildHudTooltip()
        {
            TimerSnapshot snapshot = SessionClock.Snapshot(DateTime.Now);
            string text = "Touch Grass session\nPlayed: "
                + SessionClock.FormatSpan(snapshot.Elapsed);
            if (snapshot.StartedAt.HasValue)
            {
                text += string.Format(
                    " (started {0:HH:mm})",
                    snapshot.StartedAt.Value);
            }

            text += "\nTarget: " + snapshot.TargetDescription;
            if (SessionClock.SnoozesAllowed > 0)
            {
                text += "\nSnoozes left: " + snapshot.SnoozesRemaining;
            }

            return text;
        }

        private static void RefreshHudCache(
            DateTime now,
            bool drawOwnRealTime)
        {
            long currentSecond = now.Ticks / TimeSpan.TicksPerSecond;
            int displayVersion = SessionClock.DisplayVersion;
            SessionLimitMode limitMode = SessionClock.EffectiveLimitMode;
            TimeSpan durationLimit = SessionClock.EffectiveDurationLimit;
            int stopHour = SessionClock.EffectiveStopHour;
            int stopMinute = SessionClock.EffectiveStopMinute;
            if (currentSecond == cachedHudSecond
                && displayVersion == cachedDisplayVersion
                && limitMode == cachedLimitMode
                && durationLimit.Ticks == cachedDurationTicks
                && stopHour == cachedStopHour
                && stopMinute == cachedStopMinute
                && drawOwnRealTime == cachedDrawOwnRealTime)
            {
                return;
            }

            TimerSnapshot snapshot = SessionClock.Snapshot(now, false);
            string limitText;
            if (snapshot.IsSnoozing)
            {
                string remainingLabel = snapshot.SnoozesRemaining == 1
                    ? "1 left"
                    : snapshot.SnoozesRemaining + " left";
                limitText = string.Format(
                    "Snooze {0} ({1})",
                    SessionClock.FormatSpan(snapshot.SnoozeRemaining),
                    remainingLabel);
            }
            else if (limitMode == SessionLimitMode.RealWorldStopTime)
            {
                limitText = string.Format(
                    "Stop time {0:00}:{1:00}",
                    stopHour,
                    stopMinute);
            }
            else
            {
                limitText = "Remaining "
                    + SessionClock.FormatSpan(snapshot.Remaining);
            }
            if (drawOwnRealTime)
            {
                cachedHudText = string.Format(
                    "Real time {0:HH:mm:ss}\n"
                    + "Elapsed {1}\n"
                    + "{2}",
                    now,
                    SessionClock.FormatSpan(snapshot.Elapsed),
                    limitText);
            }
            else
            {
                cachedHudText = string.Format(
                    "Elapsed {0}\n{1}",
                    SessionClock.FormatSpan(snapshot.Elapsed),
                    limitText);
            }

            float fraction;
            int colorState;
            switch (snapshot.State)
            {
                case SessionTimerState.Snoozing:
                    fraction = (float)Math.Max(
                        0d,
                        Math.Min(
                            1d,
                            snapshot.SnoozeRemaining.TotalSeconds
                                / SnoozeSeconds));
                    colorState = 2;
                    break;
                case SessionTimerState.Expired:
                case SessionTimerState.WarningTest:
                    fraction = 1f;
                    colorState = 1;
                    break;
                case SessionTimerState.Active:
                    TimeSpan total = limitMode == SessionLimitMode.Duration
                        ? durationLimit
                        : (snapshot.StartedAt.HasValue
                            ? now - snapshot.StartedAt.Value
                            : TimeSpan.Zero) + snapshot.Remaining;
                    fraction = total.Ticks > 0
                        ? (float)Math.Max(
                            0d,
                            Math.Min(
                                1d,
                                1d - (double)snapshot.Remaining.Ticks
                                    / total.Ticks))
                        : 0f;
                    TimeSpan warnWindow = TimeSpan.FromTicks(
                        Math.Min(
                            TimeSpan.FromMinutes(10).Ticks,
                            total.Ticks / 4));
                    colorState = total.Ticks > 0
                        && snapshot.Remaining <= warnWindow
                        ? 3
                        : 0;
                    break;
                default:
                    fraction = 0f;
                    colorState = 0;
                    break;
            }

            cachedHudFraction = fraction;
            cachedHudColorState = colorState;
            cachedHudSecond = currentSecond;
            cachedDisplayVersion = displayVersion;
            cachedLimitMode = limitMode;
            cachedDurationTicks = durationLimit.Ticks;
            cachedStopHour = stopHour;
            cachedStopMinute = stopMinute;
            cachedDrawOwnRealTime = drawOwnRealTime;
        }

        private static Rect PositionHud(
            TouchGrassSettings settings,
            float width,
            float height)
        {
            float x;
            switch (settings.HudAnchor)
            {
                case HudAnchor.TopLeft:
                case HudAnchor.BottomLeft:
                    x = settings.HudHorizontalOffset;
                    break;
                case HudAnchor.TopCenter:
                case HudAnchor.BottomCenter:
                    x = (UI.screenWidth - width) / 2f
                        + settings.HudHorizontalOffset;
                    break;
                default:
                    x = UI.screenWidth - width - settings.HudHorizontalOffset;
                    break;
            }

            bool bottom = settings.HudAnchor == HudAnchor.BottomLeft
                || settings.HudAnchor == HudAnchor.BottomCenter
                || settings.HudAnchor == HudAnchor.BottomRight;
            float y = bottom
                ? UI.screenHeight - height - settings.HudVerticalOffset
                : settings.HudVerticalOffset;

            x = Mathf.Clamp(x, 0f, Math.Max(0f, UI.screenWidth - width));
            y = Mathf.Clamp(y, 0f, Math.Max(0f, UI.screenHeight - height));
            return new Rect(x, y, width, height);
        }

        private static TextAnchor HorizontalAnchor(HudAnchor anchor)
        {
            switch (anchor)
            {
                case HudAnchor.TopLeft:
                case HudAnchor.BottomLeft:
                    return TextAnchor.UpperLeft;
                case HudAnchor.TopCenter:
                case HudAnchor.BottomCenter:
                    return TextAnchor.UpperCenter;
                default:
                    return TextAnchor.UpperRight;
            }
        }
    }
}
