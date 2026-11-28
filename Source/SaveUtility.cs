using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;

namespace TouchGrass
{
    internal enum SaveAndQuitResult
    {
        Failed,
        DeveloperModeSimulation,
        ShutdownRequested
    }

    internal static class SaveUtility
    {
        private static readonly FieldInfo TicksSinceLastAutosave =
            typeof(Autosaver).GetField(
                "ticksSinceSave",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static Dictionary<string, DateTime> SaveWriteTimes()
        {
            Dictionary<string, DateTime> times =
                new Dictionary<string, DateTime>();
            foreach (FileInfo file in GenFilePaths.AllSavedGameFiles)
            {
                times[file.FullName] = file.LastWriteTimeUtc;
            }

            return times;
        }

        /// <summary>TRUE when a save file that is new or changed since
        /// <paramref name="before"/> ends with the closing save-game tag. A
        /// missing or cut-off file fails the check.</summary>
        private static bool WroteCompleteSave(
            Dictionary<string, DateTime> before)
        {
            foreach (FileInfo file in GenFilePaths.AllSavedGameFiles)
            {
                DateTime oldTime;
                if (before.TryGetValue(file.FullName, out oldTime)
                    && oldTime == file.LastWriteTimeUtc)
                {
                    continue;
                }

                if (EndsWithClosingTag(file.FullName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EndsWithClosingTag(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                byte[] tail = new byte[(int)Math.Min(64L, stream.Length)];
                stream.Seek(-tail.Length, SeekOrigin.End);
                int read = 0;
                while (read < tail.Length)
                {
                    int count = stream.Read(tail, read, tail.Length - read);
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                return Encoding.UTF8.GetString(tail, 0, read)
                    .TrimEnd()
                    .EndsWith("</savegame>", StringComparison.Ordinal);
            }
        }

        private static bool ForceAutosave()
        {
            if (Current.Game == null || Current.Game.autosaver == null)
            {
                return false;
            }

            try
            {
                Dictionary<string, DateTime> before = SaveWriteTimes();
                Current.Game.autosaver.DoAutosave();

                // RimWorld catches its own save errors, so returning from
                // DoAutosave proves nothing. Look for the file instead.
                if (!WroteCompleteSave(before))
                {
                    Log.Error(
                        "[Touch Grass] Autosave failed: no complete save file "
                        + "was written.");
                    return false;
                }

                // Restart the game's own autosave timer through its private
                // field. The null check keeps a future rename from turning a
                // successful autosave into a failed shutdown request.
                if (TicksSinceLastAutosave != null)
                {
                    TicksSinceLastAutosave.SetValue(
                        Current.Game.autosaver,
                        0);
                }
                return true;
            }
            catch (Exception exception)
            {
                Log.Error(string.Format(
                    "[Touch Grass] Autosave failed: {0}",
                    exception));
                return false;
            }
        }

        public static SaveAndQuitResult ForceSaveAndQuit()
        {
            if (!ForceAutosave())
            {
                Messages.Message(
                    "TG_MsgCouldNotAutosave".Translate(),
                    MessageTypeDefOf.NegativeEvent);
                return SaveAndQuitResult.Failed;
            }

            if (Prefs.DevMode)
            {
                Messages.Message(
                    "TG_MsgDevMode".Translate(),
                    MessageTypeDefOf.NeutralEvent);
                return SaveAndQuitResult.DeveloperModeSimulation;
            }

            TaggedString message = "TG_MsgAutosaveAndQuit".Translate();
            Messages.Message(message, MessageTypeDefOf.NeutralEvent);
            Log.Message(message);
            Root.Shutdown();
            return SaveAndQuitResult.ShutdownRequested;
        }
    }
}
