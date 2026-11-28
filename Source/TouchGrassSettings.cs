using System;
using System.Globalization;
using Verse;

namespace TouchGrass
{
    public sealed class TouchGrassSettings : ModSettings
    {
        public SessionLimitMode LimitMode = SessionLimitMode.Duration;

        // Duration mode supports 00:01 through 24:59.
        public int DurationHours = 1;
        public int DurationMinutes;

        // Real-world stop-time mode uses the computer's local 24-hour clock.
        public int StopHour = 18;
        public int StopMinute;

        public bool ShowHud = true;
        public bool ShowHudMeter = true;
        public HudAnchor HudAnchor = HudAnchor.BottomRight;
        public int HudHorizontalOffset = 7;
        public int HudVerticalOffset = 166;

        public bool RepeatReminderEnabled = true;
        public string RepeatedMessage = string.Empty;
        public bool ShowAfterReboot;

        // The policy allows one to three deliberate five-minute snoozes.
        public int SnoozesAllowed = 1;

        // Up to three real-world daily windows may be enabled.
        public bool UseDailyPlayWindows;
        public int DailyWindowCount = 1;
        public IntRange AllowedDayPeriod = new IntRange(0, 24 * 60 - 1);
        public IntRange AllowedDayPeriod2 = new IntRange(0, 24 * 60 - 1);
        public IntRange AllowedDayPeriod3 = new IntRange(0, 24 * 60 - 1);

        private string lastShutdown;

        public DateTime? LastShutdownMoment
        {
            get
            {
                DateTime parsed;
                return DateTime.TryParseExact(
                    lastShutdown,
                    "o",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsed)
                    ? parsed
                    : (DateTime?)null;
            }
            set
            {
                lastShutdown = value.HasValue
                    ? value.Value.ToString("o", CultureInfo.InvariantCulture)
                    : null;
            }
        }

        public TimeSpan DurationLimit
        {
            get
            {
                int hours = Math.Max(0, Math.Min(24, DurationHours));
                int minutes = Math.Max(0, Math.Min(59, DurationMinutes));
                TimeSpan result = new TimeSpan(hours, minutes, 0);
                return result < TimeSpan.FromMinutes(1)
                    ? TimeSpan.FromMinutes(1)
                    : result;
            }
        }

        public void ResetSessionLimitToStartupDefault()
        {
            LimitMode = SessionLimitMode.Duration;
            DurationHours = 1;
            DurationMinutes = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref LimitMode, "LimitMode", SessionLimitMode.Duration);
            Scribe_Values.Look(ref DurationHours, "DurationHours", 1);
            Scribe_Values.Look(ref DurationMinutes, "DurationMinutes", 0);
            Scribe_Values.Look(ref StopHour, "StopHour", 18);
            Scribe_Values.Look(ref StopMinute, "StopMinute", 0);
            Scribe_Values.Look(ref ShowHud, "ShowHud", true);
            Scribe_Values.Look(ref ShowHudMeter, "ShowHudMeter", true);
            Scribe_Values.Look(ref HudAnchor, "HudAnchor", HudAnchor.BottomRight);
            Scribe_Values.Look(
                ref HudHorizontalOffset,
                "HudHorizontalOffset",
                7);
            Scribe_Values.Look(ref HudVerticalOffset, "HudVerticalOffset", 166);
            Scribe_Values.Look(
                ref RepeatReminderEnabled,
                "RepeatReminderEnabled",
                true);
            Scribe_Values.Look(
                ref RepeatedMessage,
                "RepeatedMessage",
                string.Empty);
            Scribe_Values.Look(ref ShowAfterReboot, "ShowAfterReboot", false);
            Scribe_Values.Look(ref SnoozesAllowed, "SnoozesAllowed", 1);
            Scribe_Values.Look(
                ref UseDailyPlayWindows,
                "UseDailyPlayWindows",
                false);
            Scribe_Values.Look(ref DailyWindowCount, "DailyWindowCount", 1);
            Scribe_Values.Look(
                ref AllowedDayPeriod,
                "AllowedDayPeriod",
                new IntRange(0, 24 * 60 - 1));
            Scribe_Values.Look(
                ref AllowedDayPeriod2,
                "AllowedDayPeriod2",
                new IntRange(0, 24 * 60 - 1));
            Scribe_Values.Look(
                ref AllowedDayPeriod3,
                "AllowedDayPeriod3",
                new IntRange(0, 24 * 60 - 1));
            Scribe_Values.Look(ref lastShutdown, "lastShutdown", null);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Keep hand-edited or damaged settings inside their valid ranges.
                DurationHours = Math.Max(0, Math.Min(24, DurationHours));
                DurationMinutes = Math.Max(0, Math.Min(59, DurationMinutes));
                StopHour = Math.Max(0, Math.Min(23, StopHour));
                StopMinute = Math.Max(0, Math.Min(59, StopMinute));
                SnoozesAllowed = Math.Max(1, Math.Min(3, SnoozesAllowed));
                DailyWindowCount = Math.Max(1, Math.Min(3, DailyWindowCount));
                AllowedDayPeriod = ClampDayRange(AllowedDayPeriod);
                AllowedDayPeriod2 = ClampDayRange(AllowedDayPeriod2);
                AllowedDayPeriod3 = ClampDayRange(AllowedDayPeriod3);
                HudHorizontalOffset = Math.Max(
                    -2000,
                    Math.Min(2000, HudHorizontalOffset));
                HudVerticalOffset = Math.Max(
                    0,
                    Math.Min(2000, HudVerticalOffset));
            }
        }

        private static IntRange ClampDayRange(IntRange range)
        {
            range.min = Math.Max(0, Math.Min(24 * 60 - 1, range.min));
            range.max = Math.Max(0, Math.Min(24 * 60 - 1, range.max));
            return range;
        }
    }
}
