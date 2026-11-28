using Verse;

namespace TouchGrass
{
    /// <summary>
    /// Compatibility checks for other mods, where Touch Grass changes its own
    /// behaviour instead of only warning the player.
    /// </summary>
    internal static class ModCompatibility
    {
        private static bool? noPauseChallengeLoaded;

        /// <summary>
        /// TRUE while pardeike's NoPauseChallenge is active. That mod removes
        /// the ability to pause, so the stop-or-snooze decision cannot wait
        /// for a response. Snoozes are therefore disabled and the deadline
        /// autosaves and quits immediately.
        /// </summary>
        public static bool IsNoPauseChallengeLoaded()
        {
            if (noPauseChallengeLoaded.HasValue)
            {
                return noPauseChallengeLoaded.Value;
            }

            ModMetaData metadata = ModLister.GetActiveModWithIdentifier(
                "brrainz.nopausechallenge");
            noPauseChallengeLoaded = metadata != null;
            if (noPauseChallengeLoaded.Value)
            {
                Log.Message(
                    "[Touch Grass] NoPauseChallenge is active: "
                    + "snoozes are disabled and the deadline will autosave and "
                    + "quit immediately.");
            }
            return noPauseChallengeLoaded.Value;
        }
    }
}
