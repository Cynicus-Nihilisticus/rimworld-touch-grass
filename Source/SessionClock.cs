using System;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace TouchGrass
{
    /// <summary>
    /// Process-local timer state. Nothing here is saved into a colony.
    /// </summary>
    internal static class SessionClock
    {
        private static readonly double SecondsPerStopwatchTick =
            1d / Stopwatch.Frequency;

        private static TimeSpan accumulatedActiveTime;
        private static long lastPlayableTimestamp;
        private static bool hasPlayableTimestamp;
        private static bool hasStarted;
        private static bool waitingForPlayableColony = true;
        private static bool manualWarningTest;
        private static bool quitTriggered;
        private static DateTime? sessionStartedAt;
        private static DateTime? realWorldDeadline;
        private static bool policyCaptured;
        private static SessionLimitMode activeLimitMode;
        private static TimeSpan activeDurationLimit;
        private static int activeStopHour;
        private static int activeStopMinute;
        private static int activeSnoozesAllowed;
        private static bool activeDailyWindowsEnabled;
        private static int activeDailyWindowCount;
        private static IntRange activeDailyWindow1;
        private static IntRange activeDailyWindow2;
        private static IntRange activeDailyWindow3;
        private static int snoozesUsed;
        private static bool snoozing;
        private static TimeSpan snoozeUntilElapsed;
        private static DateTime? snoozeUntilWallClock;
        private static bool snoozeDialogOpen;
        private static int displayVersion;

        public static bool IsWaitingForPlayableColony
        {
            get { return waitingForPlayableColony; }
        }

        public static bool IsManualWarningTest
        {
            get { return manualWarningTest; }
        }

        public static bool IsQuitTriggered
        {
            get { return quitTriggered; }
        }

        public static bool IsPlayableColonyUiActive
        {
            get { return IsPlayableColonyUi(); }
        }

        public static int DisplayVersion
        {
            get { return displayVersion; }
        }

        public static bool IsPolicyLocked
        {
            get { return policyCaptured; }
        }

        public static SessionLimitMode EffectiveLimitMode
        {
            get
            {
                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                return policyCaptured
                    ? activeLimitMode
                    : settings == null
                        ? SessionLimitMode.Duration
                        : settings.LimitMode;
            }
        }

        public static TimeSpan EffectiveDurationLimit
        {
            get
            {
                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                return policyCaptured
                    ? activeDurationLimit
                    : settings == null
                        ? TimeSpan.FromHours(1)
                        : settings.DurationLimit;
            }
        }

        public static int EffectiveStopHour
        {
            get
            {
                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                return policyCaptured
                    ? activeStopHour
                    : settings == null ? 18 : settings.StopHour;
            }
        }

        public static int EffectiveStopMinute
        {
            get
            {
                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                return policyCaptured
                    ? activeStopMinute
                    : settings == null ? 0 : settings.StopMinute;
            }
        }

        public static bool NoPauseChallengeActive
        {
            get { return ModCompatibility.IsNoPauseChallengeLoaded(); }
        }

        public static int SnoozesAllowed
        {
            get
            {
                if (NoPauseChallengeActive)
                {
                    // NoPauseChallenge removes pausing, so the stop-or-snooze
                    // decision cannot wait for a response. Zero snoozes makes
                    // every deadline save and quit immediately.
                    return 0;
                }

                TouchGrassSettings settings = TouchGrassMod.UserSettings;
                return policyCaptured
                    ? activeSnoozesAllowed
                    : settings == null ? 1 : settings.SnoozesAllowed;
            }
        }

        public static int SnoozesRemaining
        {
            get { return Math.Max(0, SnoozesAllowed - snoozesUsed); }
        }

        /// <summary>
        /// RimWorld calls this after a new colony or save has finished loading, but
        /// before its normal play UI is drawn. The old active segment was already
        /// accumulated frame-by-frame, so dropping the last sample here excludes
        /// the entire no-UI gap rather than accidentally charging it to the player.
        /// </summary>
        public static void NotifyGameLoaded()
        {
            waitingForPlayableColony = true;
            hasPlayableTimestamp = false;
            manualWarningTest = false;
            // A root/save transition discards the old WindowStack. Clear the
            // guard so an unresolved deadline can reopen its required dialog
            // on the next playable colony frame.
            snoozeDialogOpen = false;
            IncrementDisplayVersion();
        }

        /// <summary>
        /// Called from normal play update/UI callbacks. A monotonic stopwatch is
        /// used for duration accounting, so changing the computer clock cannot
        /// add or remove elapsed session time.
        /// </summary>
        public static void ObservePlayableFrame()
        {
            if (!IsPlayableColonyUi())
            {
                return;
            }

            DateTime now = DateTime.Now;
            long timestamp = Stopwatch.GetTimestamp();

            if (!hasStarted)
            {
                StartNewSession(now, timestamp);
                return;
            }

            if (waitingForPlayableColony || !hasPlayableTimestamp)
            {
                // Continue the existing process-wide session without adding the
                // main-menu or save-loading gap between the two playable frames.
                waitingForPlayableColony = false;
                lastPlayableTimestamp = timestamp;
                hasPlayableTimestamp = true;
                RefreshDeadline(now);
                return;
            }

            long elapsedTicks = timestamp - lastPlayableTimestamp;
            if (elapsedTicks > 0)
            {
                accumulatedActiveTime += TimeSpan.FromSeconds(
                    elapsedTicks * SecondsPerStopwatchTick);
            }

            lastPlayableTimestamp = timestamp;
            RefreshDeadline(now);
        }

        public static bool TriggerWarningTest()
        {
            ObservePlayableFrame();
            if (!IsPlayableColonyUi()
                || !hasStarted
                || waitingForPlayableColony)
            {
                Log.Warning(
                    "[Touch Grass] Load a playable colony before starting the warning test.");
                return false;
            }

            manualWarningTest = true;
            IncrementDisplayVersion();
            if (Current.Game != null)
            {
                Messages.Message(
                    "TG_MsgTestEnabled".Translate(),
                    MessageTypeDefOf.NeutralEvent);
            }

            return true;
        }

        public static void ClearWarningTest()
        {
            bool wasEnabled = manualWarningTest;
            manualWarningTest = false;
            if (wasEnabled)
            {
                IncrementDisplayVersion();
            }
            if (wasEnabled && Current.Game != null)
            {
                Messages.Message(
                    "TG_MsgTestCleared".Translate(),
                    MessageTypeDefOf.NeutralEvent);
            }
        }

        public static TimerSnapshot Snapshot(DateTime now)
        {
            return Snapshot(now, true);
        }

        public static TimerSnapshot Snapshot(
            DateTime now,
            bool includeTargetDescription)
        {
            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            if (settings == null)
            {
                return TimerSnapshot.Waiting;
            }

            RefreshDeadline(now);
            RefreshSnoozeState(now);

            TimeSpan elapsed = accumulatedActiveTime;
            TimeSpan remaining;
            bool baseExpired;
            string target;
            SessionLimitMode limitMode = EffectiveLimitMode;

            if (limitMode == SessionLimitMode.Duration)
            {
                TimeSpan durationLimit = EffectiveDurationLimit;
                remaining = Positive(durationLimit - elapsed);
                baseExpired = hasStarted && elapsed >= durationLimit;
                target = includeTargetDescription
                    ? DescribeDuration(durationLimit) + " of active play"
                    : null;
            }
            else
            {
                DateTime deadline = realWorldDeadline
                    ?? NextStopTime(
                        now,
                        EffectiveStopHour,
                        EffectiveStopMinute);
                remaining = Positive(deadline - now);
                baseExpired = hasStarted && now >= deadline;
                target = includeTargetDescription
                    ? DescribeStopTime(deadline, now)
                    : null;
            }

            bool warningDue = baseExpired && !snoozing;
            TimeSpan snoozeRemaining = GetSnoozeRemaining(now);
            SessionTimerState state;
            if (manualWarningTest)
            {
                state = SessionTimerState.WarningTest;
            }
            else if (!hasStarted || waitingForPlayableColony)
            {
                state = SessionTimerState.WaitingForColony;
            }
            else if (snoozing)
            {
                state = SessionTimerState.Snoozing;
            }
            else if (!warningDue)
            {
                state = SessionTimerState.Active;
            }
            else
            {
                state = SessionTimerState.Expired;
            }

            return new TimerSnapshot(
                sessionStartedAt,
                elapsed,
                remaining,
                snoozeRemaining,
                manualWarningTest || warningDue,
                state,
                target,
                SnoozesRemaining);
        }

        public static bool ShouldForceQuit(TimerSnapshot snapshot)
        {
            if (manualWarningTest
                || quitTriggered
                || waitingForPlayableColony)
            {
                return false;
            }

            return snapshot.Expired
                && !snapshot.IsSnoozing
                && snapshot.SnoozesRemaining <= 0;
        }

        public static bool CanOfferSnooze(TimerSnapshot snapshot)
        {
            return snapshot.Expired
                && !snapshot.IsSnoozing
                && snapshot.SnoozesRemaining > 0
                && !NoPauseChallengeActive
                && !manualWarningTest
                && !quitTriggered
                && !waitingForPlayableColony;
        }

        public static bool BeginSnooze(DateTime now)
        {
            TimerSnapshot snapshot = Snapshot(now, false);
            if (!CanOfferSnooze(snapshot))
            {
                return false;
            }

            snoozesUsed++;
            snoozing = true;
            snoozeDialogOpen = false;
            if (EffectiveLimitMode == SessionLimitMode.Duration)
            {
                snoozeUntilElapsed = accumulatedActiveTime
                    + TimeSpan.FromMinutes(5);
                snoozeUntilWallClock = null;
            }
            else
            {
                snoozeUntilElapsed = TimeSpan.Zero;
                snoozeUntilWallClock = now.AddMinutes(5);
            }

            IncrementDisplayVersion();
            return true;
        }

        public static bool TryMarkSnoozeDialogOpen()
        {
            if (snoozeDialogOpen || quitTriggered)
            {
                return false;
            }

            snoozeDialogOpen = true;
            return true;
        }

        public static void MarkSnoozeDialogClosed()
        {
            snoozeDialogOpen = false;
        }

        public static void MarkQuitTriggered()
        {
            quitTriggered = true;
            snoozeDialogOpen = false;
            IncrementDisplayVersion();
        }

        /// <summary>A limit in words ("1 hour 30 minutes"); seconds are dropped
        /// because limits are whole minutes.</summary>
        public static string DescribeDuration(TimeSpan value)
        {
            value = Positive(value);
            int hours = (int)value.TotalHours;
            int minutes = value.Minutes;
            string result = hours == 0
                ? string.Empty
                : hours == 1 ? "1 hour" : hours + " hours";
            if (minutes > 0)
            {
                result += (result.Length > 0 ? " " : string.Empty)
                    + (minutes == 1 ? "1 minute" : minutes + " minutes");
            }

            return result.Length > 0 ? result : "0 minutes";
        }

        public static string FormatSpan(TimeSpan value)
        {
            value = Positive(value);
            int hours = (int)value.TotalHours;
            return string.Format(
                "{0:00}:{1:00}:{2:00}",
                hours,
                value.Minutes,
                value.Seconds);
        }

        private static bool IsPlayableColonyUi()
        {
            return Current.Game != null
                && Current.Root_Play != null
                && Current.ProgramState == ProgramState.Playing;
        }

        private static void StartNewSession(DateTime now, long timestamp)
        {
            CapturePolicy();
            accumulatedActiveTime = TimeSpan.Zero;
            lastPlayableTimestamp = timestamp;
            hasPlayableTimestamp = true;
            hasStarted = true;
            waitingForPlayableColony = false;
            manualWarningTest = false;
            quitTriggered = false;
            snoozesUsed = 0;
            snoozing = false;
            snoozeUntilElapsed = TimeSpan.Zero;
            snoozeUntilWallClock = null;
            snoozeDialogOpen = false;
            sessionStartedAt = now;
            ClearDeadline();
            IncrementDisplayVersion();
            RefreshDeadline(now);
        }

        private static void RefreshDeadline(DateTime now)
        {
            if (!hasStarted)
            {
                return;
            }

            if (EffectiveLimitMode != SessionLimitMode.RealWorldStopTime)
            {
                if (realWorldDeadline.HasValue)
                {
                    ClearDeadline();
                }
                return;
            }

            if (realWorldDeadline.HasValue)
            {
                return;
            }

            realWorldDeadline = NextStopTime(
                now,
                EffectiveStopHour,
                EffectiveStopMinute);
        }

        private static DateTime NextStopTime(
            DateTime now,
            int stopHour,
            int stopMinute)
        {
            int hour = Math.Max(0, Math.Min(23, stopHour));
            int minute = Math.Max(0, Math.Min(59, stopMinute));
            DateTime candidate = now.Date.AddHours(hour).AddMinutes(minute);

            // The selected clock time is always the next occurrence.
            if (candidate <= now)
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        private static string DescribeStopTime(DateTime deadline, DateTime now)
        {
            string day;
            if (deadline.Date == now.Date)
            {
                day = "today";
            }
            else if (deadline.Date == now.Date.AddDays(1))
            {
                day = "tomorrow";
            }
            else
            {
                day = deadline.ToString("yyyy-MM-dd");
            }

            return string.Format("{0} at {1:HH:mm}", day, deadline);
        }

        public static bool IsWithinDailyWindows(DateTime now)
        {
            if (!activeDailyWindowsEnabled || !policyCaptured)
            {
                return true;
            }

            int minute = now.Hour * 60 + now.Minute;
            if (IsMinuteInRange(minute, activeDailyWindow1))
            {
                return true;
            }

            if (activeDailyWindowCount >= 2
                && IsMinuteInRange(minute, activeDailyWindow2))
            {
                return true;
            }

            return activeDailyWindowCount >= 3
                && IsMinuteInRange(minute, activeDailyWindow3);
        }

        /// <summary>The locked daily windows, for the read-only schedule strip.
        /// False when the schedule is unrestricted or not yet locked.</summary>
        public static bool TryGetActiveWindows(
            out IntRange first,
            out IntRange second,
            out IntRange third,
            out int count)
        {
            first = activeDailyWindow1;
            second = activeDailyWindow2;
            third = activeDailyWindow3;
            count = activeDailyWindowCount;
            return policyCaptured && activeDailyWindowsEnabled;
        }

        public static string DailyWindowsDescription()
        {
            if (!policyCaptured || !activeDailyWindowsEnabled)
            {
                return "unrestricted";
            }

            string result = FormatDayRange(activeDailyWindow1);
            if (activeDailyWindowCount >= 2)
            {
                result += ", " + FormatDayRange(activeDailyWindow2);
            }

            if (activeDailyWindowCount >= 3)
            {
                result += ", " + FormatDayRange(activeDailyWindow3);
            }

            return result;
        }

        private static void CapturePolicy()
        {
            if (policyCaptured)
            {
                return;
            }

            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            if (settings == null)
            {
                activeLimitMode = SessionLimitMode.Duration;
                activeDurationLimit = TimeSpan.FromHours(1);
                activeStopHour = 18;
                activeStopMinute = 0;
                activeSnoozesAllowed = 1;
                activeDailyWindowsEnabled = false;
                activeDailyWindowCount = 1;
                activeDailyWindow1 = new IntRange(0, 24 * 60 - 1);
                activeDailyWindow2 = activeDailyWindow1;
                activeDailyWindow3 = activeDailyWindow1;
            }
            else
            {
                activeLimitMode = settings.LimitMode;
                activeDurationLimit = settings.DurationLimit;
                activeStopHour = Math.Max(0, Math.Min(23, settings.StopHour));
                activeStopMinute = Math.Max(0, Math.Min(59, settings.StopMinute));
                activeSnoozesAllowed = Math.Max(
                    1,
                    Math.Min(3, settings.SnoozesAllowed));
                activeDailyWindowsEnabled = settings.UseDailyPlayWindows;
                activeDailyWindowCount = Math.Max(
                    1,
                    Math.Min(3, settings.DailyWindowCount));
                activeDailyWindow1 = settings.AllowedDayPeriod;
                activeDailyWindow2 = settings.AllowedDayPeriod2;
                activeDailyWindow3 = settings.AllowedDayPeriod3;
            }

            policyCaptured = true;
        }

        private static void RefreshSnoozeState(DateTime now)
        {
            if (!snoozing)
            {
                return;
            }

            bool finished = EffectiveLimitMode == SessionLimitMode.Duration
                ? accumulatedActiveTime >= snoozeUntilElapsed
                : !snoozeUntilWallClock.HasValue
                    || now >= snoozeUntilWallClock.Value;
            if (!finished)
            {
                return;
            }

            snoozing = false;
            snoozeUntilElapsed = TimeSpan.Zero;
            snoozeUntilWallClock = null;
            snoozeDialogOpen = false;
            IncrementDisplayVersion();
        }

        private static TimeSpan GetSnoozeRemaining(DateTime now)
        {
            if (!snoozing)
            {
                return TimeSpan.Zero;
            }

            return EffectiveLimitMode == SessionLimitMode.Duration
                ? Positive(snoozeUntilElapsed - accumulatedActiveTime)
                : Positive(snoozeUntilWallClock.Value - now);
        }

        private static bool IsMinuteInRange(int minute, IntRange range)
        {
            if (range.min <= range.max)
            {
                return minute >= range.min && minute <= range.max;
            }

            return minute >= range.min || minute <= range.max;
        }

        private static string FormatDayRange(IntRange range)
        {
            return string.Format(
                "{0:00}:{1:00}–{2:00}:{3:00}",
                range.min / 60,
                range.min % 60,
                range.max / 60,
                range.max % 60);
        }

        private static TimeSpan Positive(TimeSpan value)
        {
            return value > TimeSpan.Zero ? value : TimeSpan.Zero;
        }

        private static void ClearDeadline()
        {
            realWorldDeadline = null;
        }

        private static void IncrementDisplayVersion()
        {
            unchecked
            {
                displayVersion++;
            }
        }
    }

    internal enum SessionTimerState
    {
        WaitingForColony,
        Active,
        Expired,
        Snoozing,
        WarningTest
    }

    internal struct TimerSnapshot
    {
        public static readonly TimerSnapshot Waiting = new TimerSnapshot(
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            false,
            SessionTimerState.WaitingForColony,
            "not set",
            0);

        public readonly DateTime? StartedAt;
        public readonly TimeSpan Elapsed;
        public readonly TimeSpan Remaining;
        public readonly TimeSpan SnoozeRemaining;
        public readonly bool Expired;
        public readonly SessionTimerState State;
        public readonly string TargetDescription;
        public readonly int SnoozesRemaining;

        public bool IsSnoozing
        {
            get { return State == SessionTimerState.Snoozing; }
        }

        public TimerSnapshot(
            DateTime? startedAt,
            TimeSpan elapsed,
            TimeSpan remaining,
            TimeSpan snoozeRemaining,
            bool expired,
            SessionTimerState state,
            string targetDescription,
            int snoozesRemaining)
        {
            StartedAt = startedAt;
            Elapsed = elapsed;
            Remaining = remaining;
            SnoozeRemaining = snoozeRemaining;
            Expired = expired;
            State = state;
            TargetDescription = targetDescription;
            SnoozesRemaining = snoozesRemaining;
        }
    }
}
