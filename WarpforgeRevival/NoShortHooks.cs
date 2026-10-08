namespace WarpforgeRevival
{
    /// <summary>
    /// A note for whoever adds a patch: on Android, do not hook a game method that is only one to
    /// three instructions long (a method that just returns a constant, or just jumps into another).
    ///
    /// A normal hook overwrites 16 bytes, which such a method does not have, so the loader falls
    /// back to a 4-byte hook that needs a small relay placed close by. The hooking library can put
    /// that relay into memory the game or the runtime is using; the game then dies at start-up, or
    /// hangs there for minutes, on some launches and not on others. The loader's log names every
    /// such hook ("Short (4-byte) hook used for the function at ..."); a healthy start has none.
    ///
    /// What to do instead (all four are used in this mod): hook the method it jumps into; hook
    /// something that always runs alongside it; notice the event from the mod's own update loop;
    /// or, for a method that only returns a constant, rewrite that one instruction after checking
    /// it is what is expected (see BattlefieldCards.Answer).
    /// </summary>
    internal static class NoShortHooks { }
}
