using System;
using UnityEngine;
using Verse;

namespace TouchGrass
{
    public sealed class TouchGrassMod : Mod
    {
        private float settingsHeight = 2020f;
        private Vector2 scrollPosition;
        private string hudHorizontalBuffer;
        private string hudVerticalBuffer;

        public static TouchGrassSettings UserSettings { get; private set; }

        public TouchGrassMod(ModContentPack content) : base(content)
        {
            UserSettings = GetSettings<TouchGrassSettings>();
            // A previous session's limit must never silently become the next
            // session's policy. Every RimWorld process starts in duration mode
            // at 60 minutes, while the rest of the user's preferences persist.
            UserSettings.ResetSessionLimitToStartupDefault();
        }

        public override string SettingsCategory()
        {
            return "TG_SettingsCategory".Translate();
        }

        private static readonly string[] LimitModeLabels =
        {
            "Elapsed active duration",
            "Real-world stop time"
        };

        private static readonly string[] SnoozeCountLabels =
        {
            "One snooze",
            "Two snoozes",
            "Three snoozes"
        };

        private static readonly string[] WindowCountLabels =
        {
            "One window",
            "Two windows",
            "Three windows"
        };

        private static readonly HudAnchor[] AnchorGridOrder =
        {
            HudAnchor.TopLeft,
            HudAnchor.TopCenter,
            HudAnchor.TopRight,
            HudAnchor.BottomLeft,
            HudAnchor.BottomCenter,
            HudAnchor.BottomRight
        };

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settingsHeight = Mathf.Max(settingsHeight, inRect.height);
            scrollPosition.y = Mathf.Clamp(
                scrollPosition.y,
                0f,
                Mathf.Max(0f, settingsHeight - inRect.height));
            Rect viewRect = new Rect(
                0f,
                0f,
                inRect.width - 18f,
                settingsHeight);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            TouchGrassStyle.DrawWorkspace(viewRect);

            Listing_Standard listing = new Listing_Standard
            {
                maxOneColumn = true
            };
            listing.Begin(viewRect.ContractedBy(TouchGrassStyle.ContentPadding));

            GameFont oldFont = Text.Font;
            DrawSessionLimitSection(listing);
            listing.Gap(14f);
            DrawSnoozeSection(listing);
            listing.Gap(14f);
            DrawDailyWindowSection(listing);
            listing.Gap(14f);
            DrawTimerDisplaySection(listing);
            listing.Gap(14f);
            DrawWarningSection(listing);

            settingsHeight = Mathf.Max(
                inRect.height,
                listing.CurHeight + TouchGrassStyle.ContentPadding * 2f);
            Text.Font = oldFont;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawSessionLimitSection(Listing_Standard listing)
        {
            Section(
                listing,
                "Session limit",
                SessionClock.IsPolicyLocked ? "LOCKED" : null);
            if (SessionClock.IsPolicyLocked)
            {
                TouchGrassStyle.Banner(
                    listing,
                    "Locked for this RimWorld session",
                    "Returning to the main menu does not unlock these settings. "
                    + "Restart RimWorld, then change them before loading a "
                    + "colony.",
                    TouchGrassStyle.Warning);
                DrawLockedPolicySummary(listing);
            }
            else
            {
                TouchGrassStyle.Note(
                    listing,
                    "These settings lock as soon as the first colony becomes "
                    + "playable.");
                DrawLimitSettings(listing);
            }

            TouchGrassStyle.Note(
                listing,
                "The timer starts on the first playable colony frame. Startup, "
                + "the main menu and save-loading gaps never count, and the "
                + "session carries across save changes.");
        }

        private static void DrawLimitSettings(Listing_Standard listing)
        {
            int mode = TouchGrassStyle.Segmented(
                listing,
                LimitModeLabels,
                UserSettings.LimitMode == SessionLimitMode.Duration ? 0 : 1);
            if (mode == 0)
            {
                UserSettings.LimitMode = SessionLimitMode.Duration;
            }
            else if (mode == 1)
            {
                UserSettings.LimitMode = SessionLimitMode.RealWorldStopTime;
            }

            listing.Gap(4f);
            if (UserSettings.LimitMode == SessionLimitMode.Duration)
            {
                DrawDurationPicker(listing);
                TouchGrassStyle.Readout(
                    listing,
                    "Session limit",
                    SessionClock.DescribeDuration(UserSettings.DurationLimit)
                    + " of active play",
                    TouchGrassStyle.Cyan);
            }
            else
            {
                DrawStopTimePicker(listing);
                TimerSnapshot targetPreview = SessionClock.Snapshot(
                    DateTime.Now);
                TouchGrassStyle.Readout(
                    listing,
                    "Next stop",
                    targetPreview.TargetDescription,
                    TouchGrassStyle.Cyan);
                TouchGrassStyle.Note(
                    listing,
                    "A real clock appointment: the deadline stays fixed while a "
                    + "save loads, although elapsed play time still excludes "
                    + "the loading gap.");
            }
        }

        private static void DrawLockedPolicySummary(Listing_Standard listing)
        {
            if (SessionClock.EffectiveLimitMode == SessionLimitMode.Duration)
            {
                TouchGrassStyle.Readout(
                    listing,
                    "Active limit",
                    SessionClock.DescribeDuration(
                        SessionClock.EffectiveDurationLimit)
                    + " of active play",
                    TouchGrassStyle.Cyan);
            }
            else
            {
                TimerSnapshot snapshot = SessionClock.Snapshot(DateTime.Now);
                TouchGrassStyle.Readout(
                    listing,
                    "Active stop time",
                    snapshot.TargetDescription,
                    TouchGrassStyle.Cyan);
            }
        }

        private static void DrawSnoozeSection(Listing_Standard listing)
        {
            Section(
                listing,
                "Five-minute snoozes",
                SessionClock.IsPolicyLocked
                    && !SessionClock.NoPauseChallengeActive
                    ? "LOCKED"
                    : null);
            if (SessionClock.NoPauseChallengeActive)
            {
                TouchGrassStyle.Banner(
                    listing,
                    "NoPauseChallenge is active",
                    "Pausing is unavailable, so snoozes are disabled: at the "
                    + "deadline the mod autosaves and quits immediately "
                    + "instead of opening a decision window.",
                    TouchGrassStyle.Warning);
                return;
            }

            if (SessionClock.IsPolicyLocked)
            {
                TouchGrassStyle.Readout(
                    listing,
                    "Allowance",
                    PluralSnoozes(SessionClock.SnoozesAllowed),
                    TouchGrassStyle.Cyan);
                TouchGrassStyle.Readout(
                    listing,
                    "Remaining this session",
                    SessionClock.SnoozesRemaining.ToString(),
                    TouchGrassStyle.Cyan);
            }
            else
            {
                int picked = TouchGrassStyle.Segmented(
                    listing,
                    SnoozeCountLabels,
                    UserSettings.SnoozesAllowed - 1);
                if (picked >= 0)
                {
                    UserSettings.SnoozesAllowed = picked + 1;
                }
            }

            TouchGrassStyle.Note(
                listing,
                "At each deadline a required stop-or-snooze window opens. After "
                + "the final snooze ends, the mod autosaves and quits.");
        }

        private static void DrawDailyWindowSection(Listing_Standard listing)
        {
            Section(
                listing,
                "Permitted daily play windows",
                SessionClock.IsPolicyLocked ? "LOCKED" : null);
            if (SessionClock.IsPolicyLocked)
            {
                TouchGrassStyle.Readout(
                    listing,
                    "Schedule",
                    SessionClock.DailyWindowsDescription(),
                    TouchGrassStyle.Cyan);
                IntRange lockedFirst;
                IntRange lockedSecond;
                IntRange lockedThird;
                int lockedCount;
                if (SessionClock.TryGetActiveWindows(
                    out lockedFirst,
                    out lockedSecond,
                    out lockedThird,
                    out lockedCount))
                {
                    TouchGrassStyle.ScheduleStrip(
                        listing,
                        lockedFirst,
                        lockedSecond,
                        lockedThird,
                        lockedCount,
                        NowMinute());
                }

                return;
            }

            TouchGrassStyle.Toggle(
                listing,
                "Restrict play to configured daily windows",
                ref UserSettings.UseDailyPlayWindows);
            if (!UserSettings.UseDailyPlayWindows)
            {
                TouchGrassStyle.Note(
                    listing,
                    "Play is permitted at any time of day.");
                return;
            }

            int oldCount = UserSettings.DailyWindowCount;
            int picked = TouchGrassStyle.Segmented(
                listing,
                WindowCountLabels,
                oldCount - 1);
            if (picked >= 0 && picked + 1 != oldCount)
            {
                UserSettings.DailyWindowCount = picked + 1;
                if (oldCount < 2 && UserSettings.DailyWindowCount >= 2)
                {
                    UserSettings.AllowedDayPeriod2 =
                        UserSettings.AllowedDayPeriod;
                }

                if (oldCount < 3 && UserSettings.DailyWindowCount >= 3)
                {
                    UserSettings.AllowedDayPeriod3 =
                        UserSettings.AllowedDayPeriod2;
                }
            }

            TouchGrassStyle.Note(
                listing,
                "Play is permitted while the local time is inside any window. "
                + "A start later than its end crosses midnight.");
            TouchGrassStyle.ScheduleStrip(
                listing,
                UserSettings.AllowedDayPeriod,
                UserSettings.AllowedDayPeriod2,
                UserSettings.AllowedDayPeriod3,
                UserSettings.DailyWindowCount,
                NowMinute());
            DrawDailyRangePicker(
                listing,
                "Window 1",
                ref UserSettings.AllowedDayPeriod);
            if (UserSettings.DailyWindowCount >= 2)
            {
                DrawDailyRangePicker(
                    listing,
                    "Window 2",
                    ref UserSettings.AllowedDayPeriod2);
            }

            if (UserSettings.DailyWindowCount >= 3)
            {
                DrawDailyRangePicker(
                    listing,
                    "Window 3",
                    ref UserSettings.AllowedDayPeriod3);
            }
        }

        private void DrawTimerDisplaySection(Listing_Standard listing)
        {
            Section(listing, "Timer display");
            TouchGrassStyle.Toggle(
                listing,
                "Show the session timer on screen",
                ref UserSettings.ShowHud);
            if (!UserSettings.ShowHud)
            {
                return;
            }

            TouchGrassStyle.Toggle(
                listing,
                "Show the progress meter under the timer",
                ref UserSettings.ShowHudMeter);
            DrawAnchorGrid(listing);
            if (UserSettings.HudAnchor == HudAnchor.BottomRight)
            {
                TouchGrassStyle.Note(
                    listing,
                    "Bottom right joins RimWorld's own clock stack, so "
                    + "countdowns, letters and notifications are placed above "
                    + "the timer automatically. Choose another position to set "
                    + "manual offsets.");
                TouchGrassStyle.ColorLegend(listing);
                return;
            }

            DrawNumberRow(
                listing,
                "Horizontal margin (px)",
                ref UserSettings.HudHorizontalOffset,
                ref hudHorizontalBuffer,
                -2000,
                2000);
            DrawNumberRow(
                listing,
                "Vertical margin (px)",
                ref UserSettings.HudVerticalOffset,
                ref hudVerticalBuffer,
                0,
                2000);
            TouchGrassStyle.Note(
                listing,
                "For left and right positions the horizontal margin is the "
                + "distance from the screen edge. For centre positions it "
                + "shifts the timer away from the centre.");
            TouchGrassStyle.ColorLegend(listing);
        }

        private static void DrawAnchorGrid(Listing_Standard listing)
        {
            const float gap = 6f;
            for (int row = 0; row < 2; row++)
            {
                Rect line = listing.GetRect(34f);
                float cell = (line.width - gap * 2f) / 3f;
                for (int column = 0; column < 3; column++)
                {
                    HudAnchor anchor = AnchorGridOrder[row * 3 + column];
                    Rect rect = new Rect(
                        line.x + column * (cell + gap),
                        line.y,
                        cell,
                        line.height);
                    if (TouchGrassStyle.Button(
                            rect,
                            AnchorLabel(anchor),
                            UserSettings.HudAnchor == anchor,
                            false,
                            TouchGrassStyle.Cyan))
                    {
                        UserSettings.HudAnchor = anchor;
                    }
                }

                listing.Gap(2f);
            }

            listing.Gap(2f);
        }

        private static void DrawNumberRow(
            Listing_Standard listing,
            string label,
            ref int value,
            ref string buffer,
            int min,
            int max)
        {
            Rect row = listing.GetRect(30f);
            Rect labelRect = new Rect(
                row.x + 4f,
                row.y,
                row.width - 140f,
                row.height);
            Rect fieldRect = new Rect(
                row.xMax - 130f,
                row.y + 1f,
                126f,
                row.height - 2f);
            TouchGrassStyle.RowLabel(labelRect, label);
            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
            Text.WordWrap = oldWrap;
            listing.Gap(2f);
        }

        private static string DrawTextRow(
            Listing_Standard listing,
            string label,
            string text)
        {
            Rect row = listing.GetRect(30f);
            Rect labelRect = new Rect(
                row.x + 4f,
                row.y,
                row.width * 0.32f,
                row.height);
            Rect fieldRect = new Rect(
                labelRect.xMax,
                row.y + 1f,
                row.xMax - labelRect.xMax,
                row.height - 2f);
            TouchGrassStyle.RowLabel(labelRect, label);
            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            string result = Widgets.TextField(fieldRect, text ?? string.Empty);
            Text.WordWrap = oldWrap;
            listing.Gap(2f);
            return result;
        }

        private static void DrawWarningSection(Listing_Standard listing)
        {
            Section(listing, "Warnings and quitting");
            TouchGrassStyle.Toggle(
                listing,
                "Repeat the reminder about every 20 seconds",
                ref UserSettings.RepeatReminderEnabled);
            UserSettings.RepeatedMessage = DrawTextRow(
                listing,
                "Extra reminder text",
                UserSettings.RepeatedMessage);
            TouchGrassStyle.Toggle(
                listing,
                "Warn if RimWorld is restarted within 10 minutes of a stop "
                + "warning",
                ref UserSettings.ShowAfterReboot);

            bool warningTestActive = SessionClock.IsManualWarningTest;
            // The safe warning test is a developer tool: only offered in
            // developer mode (or to clear a test that is already running).
            if (!Prefs.DevMode && !warningTestActive)
            {
                return;
            }

            listing.Gap(8f);
            bool canStartWarningTest = SessionClock.IsPlayableColonyUiActive;
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled
                && (warningTestActive || canStartWarningTest);
            string warningButtonLabel = warningTestActive
                ? "Clear safe warning test"
                : "Trigger safe warning test now";
            if (TouchGrassStyle.Button(
                    listing.GetRect(38f),
                    warningButtonLabel,
                    warningTestActive,
                    false,
                    TouchGrassStyle.Warning))
            {
                if (warningTestActive)
                {
                    SessionClock.ClearWarningTest();
                }
                else
                {
                    SessionClock.TriggerWarningTest();
                }
            }

            GUI.enabled = oldEnabled;
            listing.Gap(4f);
            TouchGrassStyle.Note(
                listing,
                canStartWarningTest || warningTestActive
                    ? "The test shows the alert, pause, speed cap and reminder "
                        + "without autosaving or closing RimWorld."
                    : "Load a playable colony to enable the safe warning test.");
        }

        private static void DrawDailyRangePicker(
            Listing_Standard listing,
            string label,
            ref IntRange range)
        {
            range.min = Math.Max(0, Math.Min(24 * 60 - 1, range.min));
            range.max = Math.Max(0, Math.Min(24 * 60 - 1, range.max));
            TouchGrassStyle.Readout(
                listing,
                label,
                MinutesToClock(range.min) + "–" + MinutesToClock(range.max),
                TouchGrassStyle.Cyan);

            int startHour = range.min / 60;
            int startMinute = range.min % 60;
            int endHour = range.max / 60;
            int endMinute = range.max % 60;

            Rect picker = listing.GetRect(112f);
            const float columnWidth = 58f;
            const float colonWidth = 18f;
            const float toWidth = 28f;
            const float gap = 5f;
            const float arrowSize = 22f;
            const float numberHeight = 44f;
            const float rowGap = 4f;
            float totalWidth = columnWidth * 4f
                + colonWidth * 2f
                + toWidth
                + gap * 6f;
            float x = picker.x + (picker.width - totalWidth) / 2f;

            DrawTimeSpinnerColumn(
                new Rect(x, picker.y, columnWidth, picker.height),
                ref startHour,
                24,
                label.ToLowerInvariant() + " start hour",
                arrowSize,
                numberHeight,
                rowGap);
            x += columnWidth + gap;
            DrawPickerSeparator(
                new Rect(
                    x,
                    picker.y + arrowSize + rowGap,
                    colonWidth,
                    numberHeight),
                ":");
            x += colonWidth + gap;
            DrawTimeSpinnerColumn(
                new Rect(x, picker.y, columnWidth, picker.height),
                ref startMinute,
                60,
                label.ToLowerInvariant() + " start minute",
                arrowSize,
                numberHeight,
                rowGap);
            x += columnWidth + gap;
            DrawPickerSeparator(
                new Rect(
                    x,
                    picker.y + arrowSize + rowGap,
                    toWidth,
                    numberHeight),
                "to");
            x += toWidth + gap;
            DrawTimeSpinnerColumn(
                new Rect(x, picker.y, columnWidth, picker.height),
                ref endHour,
                24,
                label.ToLowerInvariant() + " end hour",
                arrowSize,
                numberHeight,
                rowGap);
            x += columnWidth + gap;
            DrawPickerSeparator(
                new Rect(
                    x,
                    picker.y + arrowSize + rowGap,
                    colonWidth,
                    numberHeight),
                ":");
            x += colonWidth + gap;
            DrawTimeSpinnerColumn(
                new Rect(x, picker.y, columnWidth, picker.height),
                ref endMinute,
                60,
                label.ToLowerInvariant() + " end minute",
                arrowSize,
                numberHeight,
                rowGap);

            range = new IntRange(
                startHour * 60 + startMinute,
                endHour * 60 + endMinute);
        }

        private static void DrawPickerSeparator(Rect rect, string text)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            Widgets.Label(rect, text);
            Text.WordWrap = oldWrap;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void Section(Listing_Standard listing, string title)
        {
            Section(listing, title, null);
        }

        private static void Section(
            Listing_Standard listing,
            string title,
            string chip)
        {
            TouchGrassStyle.DrawSectionHeading(listing.GetRect(36f), title, chip);
            listing.Gap(8f);
        }

        private static void DrawDurationPicker(Listing_Standard listing)
        {
            EnsureValidDuration();
            DrawTwoPartTimePicker(
                listing,
                "Stop after (hours : minutes)",
                ref UserSettings.DurationHours,
                25,
                "duration hour",
                ref UserSettings.DurationMinutes,
                60,
                "duration minute");
            EnsureValidDuration();
        }

        private static void DrawStopTimePicker(Listing_Standard listing)
        {
            DrawTwoPartTimePicker(
                listing,
                "Stop at (24-hour clock)",
                ref UserSettings.StopHour,
                24,
                "stop hour",
                ref UserSettings.StopMinute,
                60,
                "stop minute");
        }

        private static void DrawTwoPartTimePicker(
            Listing_Standard listing,
            string label,
            ref int firstValue,
            int firstValueCount,
            string firstUnit,
            ref int secondValue,
            int secondValueCount,
            string secondUnit)
        {
            TouchGrassStyle.Note(listing, label);

            Rect picker = listing.GetRect(112f);
            const float columnWidth = 72f;
            const float colonWidth = 28f;
            const float columnGap = 8f;
            const float arrowSize = 24f;
            const float numberHeight = 44f;
            const float rowGap = 4f;
            float totalWidth = columnWidth * 2f
                + colonWidth
                + columnGap * 2f;
            float firstX = picker.x + (picker.width - totalWidth) / 2f;
            float secondX = firstX + columnWidth + columnGap
                + colonWidth + columnGap;

            DrawTimeSpinnerColumn(
                new Rect(firstX, picker.y, columnWidth, picker.height),
                ref firstValue,
                firstValueCount,
                firstUnit,
                arrowSize,
                numberHeight,
                rowGap);
            DrawTimeSpinnerColumn(
                new Rect(secondX, picker.y, columnWidth, picker.height),
                ref secondValue,
                secondValueCount,
                secondUnit,
                arrowSize,
                numberHeight,
                rowGap);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            Widgets.Label(
                new Rect(
                    firstX + columnWidth + columnGap,
                    picker.y + arrowSize + rowGap,
                    colonWidth,
                    numberHeight),
                ":");
            Text.WordWrap = oldWrap;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawTimeSpinnerColumn(
            Rect column,
            ref int value,
            int valueCount,
            string unit,
            float arrowSize,
            float numberHeight,
            float rowGap)
        {
            float arrowX = column.x + (column.width - arrowSize) / 2f;
            Rect upButton = new Rect(
                arrowX,
                column.y,
                arrowSize,
                arrowSize);
            Rect numberRect = new Rect(
                column.x,
                upButton.yMax + rowGap,
                column.width,
                numberHeight);
            Rect downButton = new Rect(
                arrowX,
                numberRect.yMax + rowGap,
                arrowSize,
                arrowSize);

            if (TouchGrassStyle.IconButton(
                upButton,
                TexButton.ReorderUp,
                "Increase " + unit))
            {
                value = Wrap(value + 1, valueCount);
            }

            TouchGrassStyle.DrawField(numberRect);
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            Widgets.Label(numberRect, value.ToString("00"));
            Text.WordWrap = oldWrap;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            if (TouchGrassStyle.IconButton(
                downButton,
                TexButton.ReorderDown,
                "Decrease " + unit))
            {
                value = Wrap(value - 1, valueCount);
            }

            Event currentEvent = Event.current;
            if (Mouse.IsOver(column)
                && currentEvent.type == EventType.ScrollWheel)
            {
                int direction = currentEvent.delta.y < 0f ? 1 : -1;
                value = Wrap(value + direction, valueCount);
                currentEvent.Use();
            }

            TooltipHandler.TipRegion(
                column,
                "Adjust the " + unit
                + " with the arrows or mouse wheel.");
        }

        private static void EnsureValidDuration()
        {
            if (UserSettings.DurationHours == 0
                && UserSettings.DurationMinutes == 0)
            {
                UserSettings.DurationMinutes = 1;
            }
        }

        private static int Wrap(int value, int valueCount)
        {
            value %= valueCount;
            return value < 0 ? value + valueCount : value;
        }

        private static int NowMinute()
        {
            DateTime now = DateTime.Now;
            return now.Hour * 60 + now.Minute;
        }

        private static string AnchorLabel(HudAnchor anchor)
        {
            switch (anchor)
            {
                case HudAnchor.TopLeft:
                    return "Top left";
                case HudAnchor.TopCenter:
                    return "Top centre";
                case HudAnchor.TopRight:
                    return "Top right";
                case HudAnchor.BottomLeft:
                    return "Bottom left";
                case HudAnchor.BottomCenter:
                    return "Bottom centre";
                default:
                    return "Bottom right (clock stack)";
            }
        }

        private static string MinutesToClock(int minutes)
        {
            minutes = Math.Max(0, Math.Min(24 * 60 - 1, minutes));
            return new DateTime(new TimeSpan(0, minutes, 0).Ticks)
                .ToString("HH:mm");
        }

        private static string PluralSnoozes(int count)
        {
            return count == 1
                ? "one snooze"
                : count + " snoozes";
        }
    }
}
