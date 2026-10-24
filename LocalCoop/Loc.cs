using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Language of the texts this mod adds (co-op menu, banner). English is the source language of every string in the code; with the game set to German
    /// they are swapped for the German text. Source of the language: the game's own locale (Unity Localization), `-lclang=de|en` forces one (twin and test).
    /// Strings that are composed at run time are translated by parts (`Parts`), the rest by exact match (`Exact`); text that matches neither stays English.
    /// </summary>
    internal static class Loc
    {
        private static string _lang;
        private static float _langAt = -10f;

        /// <summary>"de" or "en". Re-read every few seconds: the player can change the game's language in the options.</summary>
        internal static string Lang
        {
            get
            {
                string forced = Lc.Str("-lclang=");
                if (forced.Length > 0) return forced.ToLowerInvariant().StartsWith("de") ? "de" : "en";
                float now = Time.realtimeSinceStartup;
                if (_lang != null && now < _langAt + 3f) return _lang;
                _langAt = now;
                string code = "";
                try
                {
                    var loc = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
                    if (loc != null) code = loc.Identifier.Code ?? "";
                }
                catch { }
                if (code.Length == 0) { try { code = Application.systemLanguage == SystemLanguage.German ? "de" : "en"; } catch { code = "en"; } }
                _lang = code.ToLowerInvariant().StartsWith("de") ? "de" : "en";
                return _lang;
            }
        }

        internal static bool German { get { return Lang == "de"; } }

        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>
        {
            { "LOCAL CO-OP", "LOKALER KOOP" },
            { " chooses for both: ", " wählt für beide: " },
            { "Remaining mission goals in this sector:", "Verbleibende Missionsziele in diesem Sektor:" },
            { "Remaining mission goals at this gate:", "Verbleibende Missionsziele an diesem Tor:" },
            { "Locked.", "Gesperrt." },
            { "TWO DWARVES. ONE RUN.", "ZWEI ZWERGE. EIN EINSATZ." },
            { "MISSION SETTINGS", "MISSIONSEINSTELLUNGEN" },
            { "MISSION TYPE", "MISSIONSTYP" },
            { "ELIMINATION", "ELIMINIERUNG" },
            { "ESCORT", "BEGLEITAUFTRAG" },
            { "Defeat all enemies in the area.", "Besiege alle Gegner im Gebiet." },
            { "Protect the Drilldozer and escort it to the drop pod.", "Beschütze den Drilldozer und eskortiere ihn zur Landekapsel." },
            { "Choose the mission type. The host sets up the mission, then both players pick their dwarf at the same time.", "Wähle den Missionstyp. Der Host richtet die Mission ein, danach wählen beide Spieler gleichzeitig ihren Zwerg." },
            { "SECTOR", "SEKTOR" },
            { "GATE", "TOR" },
            { "GATE (END OF A SECTOR)", "TOR (ENDE EINES SEKTORS)" },
            { "BIOME", "BIOM" },
            { "BIOME GOALS", "BIOM-ZIELE" },
            { "BIOME (FIXED BY THE GATE)", "BIOM (VOM TOR VORGEGEBEN)" },
            { "MAIN MISSION GOALS", "HAUPTZIELE DER MISSION" },
            { "DANGER", "GEFAHR" },
            { "CAMPAIGN", "KAMPAGNE" },
            { "BACK", "ZURÜCK" },
            { "CONTINUE", "WEITER" },
            { "CONFIRM", "BESTÄTIGEN" },
            { "CONFIRM MISSION", "MISSION BESTÄTIGEN" },
            { "CHANGE", "ÄNDERN" },
            { "CONFIRMED", "BESTÄTIGT" },
            { "CHOOSE YOUR CLASS", "WÄHLE DEINE KLASSE" },
            { "CHOOSE YOUR SUBCLASS", "WÄHLE DEINE UNTERKLASSE" },
            { "CLASS", "KLASSE" },
            { "SUBCLASS", "UNTERKLASSE" },
            { "Confirmed. Waiting for the other player.", "Bestätigt. Warte auf den anderen Spieler." },
            { "RUN OVERVIEW", "EINSATZÜBERSICHT" },
            { "READY", "BEREIT" },
            { "READY UP", "BEREIT" },
            { "START RUN", "EINSATZ STARTEN" },
            { "START (P1)", "STARTEN (P1)" },
            { "Everyone is ready", "Alle sind bereit" },
            { "Waiting for player 2", "Warte auf Spieler 2" },
            { "The host starts the run.", "Der Host startet den Einsatz." },
            { "Player 1 (host)", "Spieler 1 (Host)" },
            { "Player 1", "Spieler 1" },
            { "Player 2", "Spieler 2" },
            { "Players", "Spieler" },
            { "Mission Type", "Missionstyp" },
            { "Sector", "Sektor" },
            { "Danger", "Gefahr" },
            { "Biome", "Biom" },
            { "Classes", "Klassen" },
            { "Subclasses", "Unterklassen" },
            { "LOCKED", "GESPERRT" },
            { "Locked", "Gesperrt" },
            { "Controller 1 connected", "Controller 1 verbunden" },
            { "Controller 2 connected", "Controller 2 verbunden" },
            { "Controller 2 connected (last connected pad)", "Controller 2 verbunden (zuletzt verbundenes Gamepad)" },
            { "Remote Play controller connected", "Remote-Play-Controller verbunden" },
            { "LEVEL UP", "LEVEL-UP" },
            { "SHOP", "SHOP" },
            { "WEAPON UPGRADE", "WAFFEN-UPGRADE" },
            { "Crit chance", "Krit-Chance" },
            { "Crit damage", "Krit-Schaden" },
            { "Dodge chance", "Ausweichchance" },
            { "Health regeneration", "Lebensregeneration" },
            { "+10% Move speed", "+10 % Bewegungstempo" },
            { "COOP BONUS", "KOOP-BONUS" },
            { "DOWN", "AM BODEN" },
            { "REVIVE", "WIEDERBELEBEN" },
            { "REVIVE: ON", "WIEDERBELEBEN: AN" },
            { "PLAYER 1", "SPIELER 1" },
            { "PLAYER 2", "SPIELER 2" },
            { "REVIVE: OFF", "WIEDERBELEBEN: AUS" },
            { "both dwarves get this upgrade", "beide Zwerge erhalten dieses Upgrade" },
        };

        // composed texts: replaced inside the string, longest first
        private static readonly KeyValuePair<string, string>[] Parts =
        {
            new KeyValuePair<string, string>("Unlocks at player level ", "Ab Spielerstufe "),
            new KeyValuePair<string, string>("Unlocks at class level ", "Ab Klassenstufe "),
            new KeyValuePair<string, string>("Starting weapon: ", "Startwaffe: "),
            new KeyValuePair<string, string>("CHOOSE YOUR SUBCLASS", "WÄHLE DEINE UNTERKLASSE"),
            new KeyValuePair<string, string>("CHOOSE YOUR CLASS", "WÄHLE DEINE KLASSE"),
            new KeyValuePair<string, string>("PLAYER 1: ", "SPIELER 1: "),
            new KeyValuePair<string, string>("PLAYER 2: ", "SPIELER 2: "),
            new KeyValuePair<string, string>("PLAYER LEVEL ", "SPIELERSTUFE "),
            new KeyValuePair<string, string>("PLAYER ", "SPIELER "),
            new KeyValuePair<string, string>("CLEAR GATE ", "TOR BEZWINGEN "),
            new KeyValuePair<string, string>("HIGH SCORE", "HIGHSCORE"),
            new KeyValuePair<string, string>("CONFIRMED", "BESTÄTIGT"),
            new KeyValuePair<string, string>("SECTOR ", "SEKTOR "),
            new KeyValuePair<string, string>("DANGER ", "GEFAHR "),
            new KeyValuePair<string, string>("GATE ", "TOR "),
            new KeyValuePair<string, string>("Gate ", "Tor "),
            new KeyValuePair<string, string>("Danger ", "Gefahr "),
            new KeyValuePair<string, string>(" POINTS", " PUNKTE"),
            new KeyValuePair<string, string>("POINTS ", "PUNKTE "),
            new KeyValuePair<string, string>("Level up", "Level-up"),
        };

        /// <summary>The text in the game's language. English stays unchanged; unknown text stays as it is.</summary>
        internal static string T(string en)
        {
            if (string.IsNullOrEmpty(en) || !German) return en;
            string de;
            if (Exact.TryGetValue(en, out de)) return de;
            string s = en;
            foreach (var kv in Parts) if (s.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        /// <summary>Test: how many texts under `root` are still in English although the game runs in German (they would flicker until the next sweep).</summary>
        internal static int Untranslated(Transform root)
        {
            if (root == null || !German) return 0;
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true)) if (t != null && T(t.text) != t.text) n++;
            return n;
        }

        internal static void Tick() { GameLangTick(); if (CoopMenu.Visible) Sweep(CoopMenu.RootTransform); }

        internal static int Swapped;
        private static float _sweepAt;

        /// <summary>Text the menu code assigns later (refreshes) is translated here, twice a second, for every text under `root`.
        /// Idempotent: German text matches no English key. Twin `-lcnoloc` leaves everything English.</summary>
        internal static void Sweep(Transform root)
        {
            if (root == null || Lc.Has("-lcnoloc")) return;
            float now = Time.realtimeSinceStartup;
            if (now < _sweepAt) return;
            _sweepAt = now + 0.5f;
            SweepNow(root);
        }

        /// <summary>The same sweep without the pause: right after the menu code wrote its (English) texts, before the frame is drawn.</summary>
        internal static void SweepNow(Transform root)
        {
            if (root == null || Lc.Has("-lcnoloc") || !German) return;
            foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null) continue;
                string d = T(t.text);
                if (d != t.text) { t.text = d; Swapped++; }
            }
        }

        private static bool _gameLangDone; private static float _gameLangAt = -1f; private static int _gameLangTries, _gameLangSets;

        /// <summary>Test only (`-lcgamelang=en`): switches the game's own language, so that screenshots are fully English. Not persistent: it only sets the running game's
        /// selected locale; the saves are restored by the harness as always.</summary>
        internal static void GameLangTick()
        {
            string want = Lc.Str("-lcgamelang=");
            if (want.Length == 0 || _gameLangTries > 900) return;
            float now = Time.realtimeSinceStartup;
            if (_gameLangAt < 0f) _gameLangAt = now + 2f;
            if (now < _gameLangAt) return;
            _gameLangAt = now + 1f; _gameLangTries++;
            try
            {
                var locs = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales;
                if (locs == null || locs.Locales == null || locs.Locales.Count == 0) return;
                var loc = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales.GetLocale(new UnityEngine.Localization.LocaleIdentifier(want));
                if (loc == null) { Lc.LineOnce("loc: the game has no locale '" + want + "'"); _gameLangTries = 99; return; }
                var cur = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
                if (cur != null && cur.Identifier.Code != null && cur.Identifier.Code.StartsWith(want, StringComparison.OrdinalIgnoreCase)) return;          // already there; the game may set its own language again later, so this keeps checking
                UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = loc;
                _lang = null; _gameLangSets++;
                Lc.Line("loc: the game's language set to " + want + " (test only, time " + _gameLangSets + ")");
            }
            catch (Exception e) { Lc.LineOnce("loc: game language switch failed " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
