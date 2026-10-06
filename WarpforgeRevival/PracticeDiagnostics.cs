using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>Temporary diagnostics for the Play-page practice menu.</summary>
    internal static class PracticeDiagnostics
    {
        private static string N(object o) => o == null ? "NULL" : "ok";

        [HarmonyPatch(typeof(PracticeModePopup), nameof(PracticeModePopup.Open))]
        private static class Open
        {
            private static void Prefix(PracticeModePopup __instance)
            {
                try
                {
                    var p = __instance;
                    var dd = p._defaultDeck;
                    RevivalMod.Log.Msg($"[practicediag] mode={p.currentPlayMode} decks={(p.decks == null ? -1 : p.decks.Length)} defaultDeck={((object)dd == null ? "NULL" : dd.name)} " +
                                       $"wrapper={N(p.playerDeckInfoWrapper)} deckInfo={N(p.deckInfo)} toggle={N(p.gameModeSelector)}");
                    if ((object)dd != null)
                        RevivalMod.Log.Msg($"[practicediag] default classic={((object)dd.classicDeck == null ? "NULL" : dd.classicDeck.deckId)} skirmish={((object)dd.skirmishDeck == null ? "NULL" : dd.skirmishDeck.deckId)}");
                    int nullClassic = 0, nullSkirmish = 0;
                    if (p.decks != null)
                        foreach (var d in p.decks)
                        {
                            if ((object)d == null) continue;
                            if ((object)d.classicDeck == null) nullClassic++;
                            if ((object)d.skirmishDeck == null) nullSkirmish++;
                        }
                    RevivalMod.Log.Msg($"[practicediag] decks without classic={nullClassic} without skirmish={nullSkirmish}");
                    var gm = LiveOpsManager.GetHandler<GameModes>();
                    RevivalMod.Log.Msg($"[practicediag] GameModes handler={N(gm)}");
                    if (gm != null)
                    {
                        foreach (PlayModes m in new[] { PlayModes.Classic, PlayModes.Skirmish, PlayModes.OfflinePractice, PlayModes.OwnDeckTraining })
                        {
                            string r;
                            try { var e = gm.GetActiveEvent(m); r = e == null ? "NULL" : "found"; } catch (Exception ex) { r = "threw " + ex.Message; }
                            RevivalMod.Log.Msg($"[practicediag] active event for {m}: {r}");
                        }
                    }
                    var pd = PlayerDataManager.singletonManager;
                    RevivalMod.Log.Msg($"[practicediag] playerData={N(pd)}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[practicediag] " + e); }
            }
        }
    }
}
