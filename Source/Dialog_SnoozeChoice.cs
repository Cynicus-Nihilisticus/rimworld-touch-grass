using System;
using System.Diagnostics;
using UnityEngine;
using Verse;

namespace TouchGrass
{
    internal sealed class Dialog_SnoozeChoice : Window
    {
        private const double ResponseTimeoutSeconds = 5d * 60d;

        private readonly string targetDescription;
        private readonly int snoozesRemaining;
        private readonly long responseDeadlineTimestamp;
        private bool automaticQuitAttempted;
        private bool resolved;

        public override Vector2 InitialSize
        {
            get { return new Vector2(580f, 344f); }
        }

        private Dialog_SnoozeChoice(
            string targetDescription,
            int snoozesRemaining)
        {
            this.targetDescription = targetDescription;
            this.snoozesRemaining = snoozesRemaining;
            responseDeadlineTimestamp = Stopwatch.GetTimestamp()
                + (long)(ResponseTimeoutSeconds * Stopwatch.Frequency);

            doCloseX = false;
            doCloseButton = false;
            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            draggable = false;
            resizeable = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            forceCatchAcceptAndCancelEventEvenIfUnfocused = true;
            layer = WindowLayer.Dialog;
        }

        public static void EnsureOpen(TimerSnapshot snapshot)
        {
            if (!SessionClock.CanOfferSnooze(snapshot)
                || !SessionClock.TryMarkSnoozeDialogOpen())
            {
                return;
            }

            try
            {
                TimerSnapshot fullSnapshot = SessionClock.Snapshot(
                    DateTime.Now);
                Find.WindowStack.Add(new Dialog_SnoozeChoice(
                    fullSnapshot.TargetDescription,
                    fullSnapshot.SnoozesRemaining));
            }
            catch (Exception exception)
            {
                SessionClock.MarkSnoozeDialogClosed();
                Log.Error(string.Format(
                    "[Touch Grass] Could not open the snooze "
                    + "decision window.\n{0}",
                    exception));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            long nowTimestamp = Stopwatch.GetTimestamp();
            if (!resolved
                && !automaticQuitAttempted
                && nowTimestamp >= responseDeadlineTimestamp)
            {
                automaticQuitAttempted = true;
                if (TrySaveAndQuit())
                {
                    return;
                }
            }

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            Rect content = inRect.ContractedBy(16f);
            TouchGrassStyle.DrawPanel(inRect, TouchGrassStyle.Warning);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = TouchGrassStyle.Gold;
            Widgets.Label(
                new Rect(content.x, content.y, content.width, 38f),
                "It's time to go touch grass");

            // Hierarchy: what happened (muted) > what to do (white) > what
            // happens otherwise (muted) > the live countdown (accent).
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            float y = content.y + 42f;
            GUI.color = TouchGrassStyle.Muted;
            Widgets.Label(
                new Rect(content.x, y, content.width, 22f),
                "Target reached: " + targetDescription);
            y += 30f;

            Text.WordWrap = true;
            GUI.color = Color.white;
            string plural = snoozesRemaining == 1 ? "snooze" : "snoozes";
            string body = string.Format(
                "Autosave and quit now, or take a five-minute snooze. "
                + "You have {0} {1} left.",
                snoozesRemaining,
                plural);
            float bodyHeight = Text.CalcHeight(body, content.width);
            Widgets.Label(
                new Rect(content.x, y, content.width, bodyHeight),
                body);
            y += bodyHeight + 8f;

            GUI.color = TouchGrassStyle.Muted;
            const string note =
                "After the last snooze RimWorld autosaves and quits by "
                + "itself. If you do not respond within five minutes, it "
                + "assumes you are away and does the same.";
            float noteHeight = Text.CalcHeight(note, content.width);
            Widgets.Label(
                new Rect(content.x, y, content.width, noteHeight),
                note);
            y += noteHeight + 10f;

            GUI.color = TouchGrassStyle.Warning;
            Widgets.Label(
                new Rect(
                    content.x,
                    y,
                    content.width,
                    Mathf.Max(24f, Text.LineHeight * 2f)),
                automaticQuitAttempted
                    ? "The automatic autosave could not complete. Choose an "
                        + "option to try again."
                    : "No response? Autosave and quit in "
                        + FormatCountdown(nowTimestamp));
            if (!automaticQuitAttempted)
            {
                // The response timer drains; it turns red for the last 30 s.
                float remainingFraction = (float)Math.Max(
                    0d,
                    Math.Min(
                        1d,
                        (responseDeadlineTimestamp - nowTimestamp)
                            / (ResponseTimeoutSeconds * Stopwatch.Frequency)));
                TouchGrassStyle.DrawMeter(
                    new Rect(content.x, y + 24f, content.width, 6f),
                    remainingFraction,
                    remainingFraction < 0.1f
                        ? TouchGrassStyle.Negative
                        : TouchGrassStyle.Warning);
            }

            const float buttonHeight = 44f;
            const float buttonGap = 16f;
            float buttonWidth = (content.width - buttonGap) / 2f;
            float buttonY = content.yMax - buttonHeight;

            GUI.color = oldColor;
            if (TouchGrassStyle.Button(
                    new Rect(
                        content.x,
                        buttonY,
                        buttonWidth,
                        buttonHeight),
                    "Snooze 5 minutes",
                    false,
                    false,
                    TouchGrassStyle.Warning,
                    true))
            {
                if (SessionClock.BeginSnooze(DateTime.Now))
                {
                    resolved = true;
                    Close();
                }
            }

            if (TouchGrassStyle.Button(
                new Rect(
                    content.x + buttonWidth + buttonGap,
                    buttonY,
                    buttonWidth,
                    buttonHeight),
                "Autosave and quit now",
                false,
                true,
                TouchGrassStyle.Negative,
                true))
            {
                TrySaveAndQuit();
            }

            GUI.color = oldColor;
            Text.WordWrap = oldWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private string FormatCountdown(long nowTimestamp)
        {
            long remainingTicks = Math.Max(
                0L,
                responseDeadlineTimestamp - nowTimestamp);
            double remainingSeconds = remainingTicks
                / (double)Stopwatch.Frequency;
            int displaySeconds = (int)Math.Ceiling(remainingSeconds);
            return string.Format(
                "{0:00}:{1:00}",
                displaySeconds / 60,
                displaySeconds % 60);
        }

        private bool TrySaveAndQuit()
        {
            SaveAndQuitResult result = SaveUtility.ForceSaveAndQuit();
            if (result == SaveAndQuitResult.Failed)
            {
                return false;
            }

            SessionClock.MarkQuitTriggered();
            resolved = true;
            Close();
            return true;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!resolved)
            {
                SessionClock.MarkSnoozeDialogClosed();
            }
        }
    }
}
