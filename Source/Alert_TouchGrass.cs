using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace TouchGrass
{
    public sealed class Alert_TouchGrass : Alert_Critical
    {
        private const double CooldownMinutes = 10d;
        private const double ReminderSeconds = 20d;
        private const double AutosaveRetrySeconds = 20d;

        private readonly DateTime? earlyRebootUntil;
        private DateTime nextReminderAt = DateTime.MinValue;
        private DateTime nextAutomaticQuitAttemptAt = DateTime.MinValue;
        private bool initialReminderShown;
        private AlertActivation activeActivation;

        public Alert_TouchGrass()
        {
            defaultLabel = "TG_AlertTitleTimeout".Translate();

            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            DateTime? lastShutdown = settings == null
                ? null
                : settings.LastShutdownMoment;
            if (lastShutdown.HasValue)
            {
                DateTime candidate = lastShutdown.Value.AddMinutes(CooldownMinutes);
                if (lastShutdown.Value <= DateTime.Now
                    && candidate > DateTime.Now)
                {
                    earlyRebootUntil = candidate;
                }
            }
        }

        public override TaggedString GetExplanation()
        {
            DateTime now = DateTime.Now;
            if (SessionClock.IsManualWarningTest)
            {
                return "TG_AlertDescTest".Translate();
            }

            TimerSnapshot snapshot = SessionClock.Snapshot(now);
            if (snapshot.Expired
                && !SessionClock.IsWaitingForPlayableColony)
            {
                string elapsed = SessionClock.FormatSpan(snapshot.Elapsed);
                if (snapshot.SnoozesRemaining > 0)
                {
                    return "TG_AlertDescSnoozeChoice".Translate(
                        elapsed,
                        snapshot.TargetDescription,
                        snapshot.SnoozesRemaining);
                }

                return "TG_AlertDescNoSnoozes".Translate(
                    elapsed,
                    snapshot.TargetDescription);
            }

            ImmediateReason reason = GetImmediateReason(now);
            if (reason == ImmediateReason.OutsideAllowedHours)
            {
                return "TG_AlertDescOutsideHours".Translate();
            }

            if (reason == ImmediateReason.EarlyReboot)
            {
                return "TG_AlertDescEarlyReboot".Translate(CooldownMinutes);
            }

            return "TG_AlertDescSnoozeChoice".Translate(
                SessionClock.FormatSpan(snapshot.Elapsed),
                snapshot.TargetDescription,
                snapshot.SnoozesRemaining);
        }

        public override AlertReport GetReport()
        {
            DateTime now = DateTime.Now;

            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            if (settings == null)
            {
                return AlertReport.Inactive;
            }

            // Alerts must never enforce or autosave while RimWorld is in a loader
            // or at the main menu, even if an absolute stop time passes there.
            if (!SessionClock.IsPlayableColonyUiActive)
            {
                return AlertReport.Inactive;
            }

            // A successful shutdown request normally ends the process before
            // another alert poll. Developer mode deliberately simulates that
            // request without closing RimWorld, so consider the warning fully
            // resolved instead of leaving a permanent red alert and reminder.
            if (SessionClock.IsQuitTriggered)
            {
                ResetActivation();
                return AlertReport.Inactive;
            }

            ImmediateReason reason = GetImmediateReason(now);
            TimerSnapshot snapshot = SessionClock.Snapshot(now, false);
            bool timerExpired = snapshot.Expired
                && !SessionClock.IsWaitingForPlayableColony;
            if (timerExpired && !SessionClock.IsManualWarningTest)
            {
                // The process-wide session deadline always wins over secondary
                // restart and daily-window warnings. Otherwise either warning
                // could postpone the required modal or final automatic quit.
                reason = ImmediateReason.None;
            }


            AlertActivation activation = ActivationFor(
                reason,
                timerExpired);
            if (activation != activeActivation)
            {
                // An advisory warning or safe test can already be active when
                // the real session deadline arrives. Treat each source change
                // as a fresh warning so genuine expiry still records its
                // restart-cooldown moment and emits its first reminder even
                // when repeated reminders are disabled.
                activeActivation = activation;
                initialReminderShown = false;
                nextReminderAt = DateTime.MinValue;
            }

            if (SessionClock.IsManualWarningTest)
            {
                defaultLabel = "TG_AlertTitleTimeout".Translate();
            }
            else if (reason == ImmediateReason.OutsideAllowedHours)
            {
                defaultLabel = "TG_AlertTitleOutsideHours".Translate();
            }
            else if (reason == ImmediateReason.EarlyReboot)
            {
                defaultLabel = "TG_AlertTitleEarlyReboot".Translate();
            }
            else
            {
                defaultLabel = "TG_AlertTitleTimeout".Translate();
            }

            if (reason == ImmediateReason.None && !timerExpired)
            {
                ResetActivation();
                return AlertReport.Inactive;
            }

            if (reason == ImmediateReason.None
                && timerExpired
                && !SessionClock.IsManualWarningTest
                && SessionClock.CanOfferSnooze(snapshot))
            {
                Dialog_SnoozeChoice.EnsureOpen(snapshot);
            }

            TickManager tickManager = Find.TickManager;
            if (tickManager != null)
            {
                tickManager.slower.SignalForceNormalSpeed();
            }

            bool reminderDue = now >= nextReminderAt
                && (!initialReminderShown || settings.RepeatReminderEnabled);
            if (reminderDue)
            {
                if (timerExpired
                    && !SessionClock.IsManualWarningTest
                    && !initialReminderShown)
                {
                    settings.LastShutdownMoment = now;
                    settings.Write();
                }

                string message = "TG_MsgRepeated".Translate(
                    settings.RepeatedMessage);
                Messages.Message(message, MessageTypeDefOf.NegativeEvent);
                if (tickManager != null
                    && !SessionClock.NoPauseChallengeActive)
                {
                    // NoPauseChallenge blocks pausing anyway; asking for it
                    // would only fight that mod and spam the log.
                    tickManager.Pause();
                }
                nextReminderAt = now.AddSeconds(ReminderSeconds);
                initialReminderShown = true;
            }

            // Only genuine timer expiry may automatically quit. The manual test,
            // restart cooldown, and daily-hours warning are deliberately safe.
            if (reason == ImmediateReason.None
                && timerExpired
                && !SessionClock.IsManualWarningTest
                && SessionClock.ShouldForceQuit(snapshot)
                && now >= nextAutomaticQuitAttemptAt)
            {
                // A failed autosave must not be retried from every alert poll.
                // Leave the alert active and retry at the same restrained
                // cadence as reminders; clicking the alert can still retry now.
                nextAutomaticQuitAttemptAt = now.AddSeconds(
                    AutosaveRetrySeconds);
                Messages.Message(
                    "TG_MsgForceQuitting".Translate(),
                    MessageTypeDefOf.NeutralEvent);
                SaveAndQuitResult result = SaveUtility.ForceSaveAndQuit();
                if (result != SaveAndQuitResult.Failed)
                {
                    SessionClock.MarkQuitTriggered();
                }
            }

            return AlertReport.Active;
        }

        protected override void OnClick()
        {
            if (Event.current.button == 0)
            {
                if (SessionClock.IsQuitTriggered)
                {
                    return;
                }

                if (SessionClock.IsManualWarningTest)
                {
                    SessionClock.ClearWarningTest();
                }
                else
                {
                    TimerSnapshot snapshot = SessionClock.Snapshot(
                        DateTime.Now,
                        false);
                    bool genuineTimerExpiry = snapshot.Expired
                        && !SessionClock.IsWaitingForPlayableColony;
                    if (!genuineTimerExpiry)
                    {
                        // Daily-window and early-restart alerts are advisory.
                        // Clicking one must never become an unlabelled quit
                        // command merely because it shares this alert class.
                        base.OnClick();
                        return;
                    }

                    if (SessionClock.CanOfferSnooze(snapshot))
                    {
                        Dialog_SnoozeChoice.EnsureOpen(snapshot);
                    }
                    else
                    {
                        SaveAndQuitResult result =
                            SaveUtility.ForceSaveAndQuit();
                        if (result != SaveAndQuitResult.Failed)
                        {
                            SessionClock.MarkQuitTriggered();
                        }
                    }
                }

                return;
            }

            base.OnClick();
        }

        private static AlertActivation ActivationFor(
            ImmediateReason reason,
            bool timerExpired)
        {
            if (SessionClock.IsManualWarningTest)
            {
                return AlertActivation.ManualTest;
            }

            if (timerExpired)
            {
                return AlertActivation.TimerExpired;
            }

            if (reason == ImmediateReason.OutsideAllowedHours)
            {
                return AlertActivation.OutsideAllowedHours;
            }

            return reason == ImmediateReason.EarlyReboot
                ? AlertActivation.EarlyReboot
                : AlertActivation.None;
        }

        private void ResetActivation()
        {
            activeActivation = AlertActivation.None;
            initialReminderShown = false;
            nextReminderAt = DateTime.MinValue;
        }

        private ImmediateReason GetImmediateReason(DateTime now)
        {
            TouchGrassSettings settings = TouchGrassMod.UserSettings;
            if (settings == null)
            {
                return ImmediateReason.None;
            }

            if (!SessionClock.IsWithinDailyWindows(now))
            {
                return ImmediateReason.OutsideAllowedHours;
            }

            if (settings.ShowAfterReboot
                && earlyRebootUntil.HasValue
                && now < earlyRebootUntil.Value)
            {
                return ImmediateReason.EarlyReboot;
            }

            return ImmediateReason.None;
        }

        private enum ImmediateReason
        {
            None,
            EarlyReboot,
            OutsideAllowedHours
        }

        private enum AlertActivation
        {
            None,
            ManualTest,
            TimerExpired,
            EarlyReboot,
            OutsideAllowedHours
        }
    }
}
