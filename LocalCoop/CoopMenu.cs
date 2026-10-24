using System;
using System.Collections.Generic;
using System.Text;
using Assets.Scripts.Data;
using Assets.Scripts.Milestones;
using Assets.Scripts.RankSystem;
using DRS.SaveSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>One player's choice in the co-op menu.</summary>
    internal sealed class PlayerPick
    {
        internal int ClassIndex;                 // index into CoopMenu.Classes
        internal ClassArtifactData Sub;          // chosen subclass (never null once the menu is built)
    }

    /// <summary>
    /// The local co-op menu: its own uGUI canvas over the main menu. It only holds choices (this class is the state and the
    /// overview); starting a run is in CoopStart. All data comes from the game's own collections, nothing hard-coded except labels.
    /// </summary>
    internal static partial class CoopMenu
    {
        internal static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);
        internal static readonly Color OrangeP1 = new Color(1f, 0.16f, 0.12f, 1f);          // hand test: player 1 was orange here and red elsewhere; one red now (same value as Chooser.Red)
        internal static readonly Color BlueP2 = new Color(0.20f, 0.58f, 1f, 1f);
        private static readonly Color Panel = new Color(0.04f, 0.06f, 0.10f, 0.94f);
        private static readonly Color Idle = new Color(0.10f, 0.13f, 0.19f, 1f);
        private static readonly Color Text = new Color(0.90f, 0.93f, 0.97f, 1f);

        // ---- state -------------------------------------------------------------------------------------------
        internal static int MissionType;         // 0 Elimination, 1 Escort (EMainMissionType)
        internal static int Sector;              // 0..4 (shown 1..5)
        internal static int Danger;              // 0..4 (shown 1..5)
        internal static int BiomeIndex;
        internal static bool GateSel;            // the sector's gate node (the boss-like level at the end of the sector) instead of a biome node
        internal static readonly PlayerPick P1 = new PlayerPick(), P2 = new PlayerPick();
        internal static ClassData[] Classes;     // Gunner, Driller, Engineer, Scout (the visible four)
        internal static BiomeLevelData[] Biomes;
        private static ClassArtifactData[] _allSubs;

        // ---- ui ------------------------------------------------------------------------------------------------
        private static GameObject _root;
        private static TMP_FontAsset _font;
        internal static TMP_FontAsset FontAsset { get { return _font; } }
        private static readonly List<Action> _refresh = new List<Action>();
        internal static readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>();
        internal static readonly Dictionary<string, TextMeshProUGUI> Values = new Dictionary<string, TextMeshProUGUI>();
        private static readonly Dictionary<string, Image> Images = new Dictionary<string, Image>();
        private static readonly Dictionary<string, Outline> Marks = new Dictionary<string, Outline>();
        internal static bool Built { get { return _root != null; } }
        internal static bool Visible { get { return _root != null && _root.activeSelf; } }
        internal static Transform RootTransform { get { return _root != null ? _root.transform : null; } }

        /// <summary>"classic": the all-in-one menu (tests, `-lcoldmenu`); "lobby": players and OPEN MISSION MENU; "p2": player 2's class page
        /// shown over the game's own mission wizard (see Flow).</summary>
        internal static string Mode = "classic";
        internal static readonly bool[] Confirmed = new bool[2];      // player n's class and subclass are confirmed (split-screen pick)
        internal static readonly string[] Stage = { "c", "c" };       // "c": class page, "s": subclass page
        internal static bool P2Ready;
        private static GameObject _startHint;

        internal static bool ShareTwin;          // -lcuishare: P2 follows P1 (twin of the independence check, proves the check can fail)

        internal static readonly string[] ClassOrder = { "GUNNER", "DRILLER", "ENGINEER", "SCOUT" };

        // ---- data --------------------------------------------------------------------------------------------
        private static bool LoadData()
        {
            var cc = Resources.FindObjectsOfTypeAll<ClassCollection>();
            var bl = Resources.FindObjectsOfTypeAll<BiomeLevelDataCollection>();
            if (cc.Length == 0 || bl.Length == 0) return false;
            var list = new List<ClassData>();
            foreach (var name in ClassOrder)
                foreach (var c in cc[0].GetClasses())
                    if (c != null && c.Dwarf.ToString() == name) list.Add(c);
            Classes = list.ToArray();
            Biomes = bl[0].Data;
            _allSubs = Resources.FindObjectsOfTypeAll<ClassArtifactData>();
            return Classes.Length == 4 && Biomes != null && Biomes.Length > 0;
        }

        internal static List<ClassArtifactData> SubsFor(ClassData c)
        {
            var r = new List<ClassArtifactData>();
            foreach (var a in _allSubs)
            {
                if (a == null) continue;
                bool fits = a.DefaultClass == c.Dwarf;
                try { foreach (var d in a.PossibleClasses) if (d == c.Dwarf) fits = true; } catch { }
                if (fits) r.Add(a);
            }
            r.Sort((x, y) => SubLevel(x) != SubLevel(y) ? SubLevel(x).CompareTo(SubLevel(y)) : string.CompareOrdinal(x.name, y.name));
            // the class's own default first
            if (c.DefaultClassArtifact != null) { int i = r.IndexOf(c.DefaultClassArtifact); if (i > 0) { var d = r[i]; r.RemoveAt(i); r.Insert(0, d); } }
            return r;
        }

        private static void Defaults(PlayerPick p, int classIndex)
        {
            p.ClassIndex = classIndex;
            p.Sub = Classes[classIndex].DefaultClassArtifact;
            var subs = SubsFor(Classes[classIndex]);
            if (p.Sub == null && subs.Count > 0) p.Sub = subs[0];
        }

        // ---- progress: what the player's save has unlocked (the game's own answers) -----------------------------------
        internal static int ClickCount;          // clicks that reached one of the menu's buttons (test)
        internal static bool IgnoreLocks;        // -lcuinolocks: twin of the lock checks, proves they can fail
        private static SaveDataManager _save;
        private static MissionRoadManager _road;
        private static MilestoneTracker _tracker;

        private static void Services()
        {
            if (_save == null) _save = CoopStart.Resolve<SaveDataManager>();
            if (_road == null) _road = CoopStart.Resolve<MissionRoadManager>();
            if (_tracker == null) _tracker = CoopStart.Resolve<MilestoneTracker>();
        }

        /// <summary>The player's own level (the number that opens the classes), -1 when unavailable.</summary>
        internal static int PlayerLevel()
        {
            try { var m = CoopStart.Resolve<PlayerRankManager>(); return m != null ? m.PlayerRank : -1; }
            catch (Exception e) { Lc.LineOnce("ui: player level failed: " + e.GetType().Name + ": " + e.Message); return -1; }
        }

        /// <summary>The player level a mission type opens at (its road's unlock milestone, when that is a player-rank milestone), 0 when unknown.</summary>
        internal static int MissionUnlockLevel(int mission)
        {
            try
            {
                var st = RoadState(mission);
                var ms = st != null && st.MissionRoadData != null ? st.MissionRoadData.MilestoneToUnlock : null;
                if (ms == null) return 0;
                Lc.LineOnce("ui: mission type " + mission + " unlocks with milestone " + ms.name + " (" + ms.GetIl2CppType().Name + ") target=" + ms.Target);
                return ms.TryCast<MSDataPlayerRank>() != null ? ms.Target : 0;
            }
            catch (Exception e) { Lc.LineOnce("ui: mission unlock level failed: " + e.GetType().Name + ": " + e.Message); return 0; }
        }

        private static MissionRoadState RoadState(int mission)
        {
            Services();
            return _road != null ? _road.GetMissionRoadState((EMainMissionType)mission) : null;
        }

        internal static MissionNodeState NodeOf(int mission, int sector, int biomeIndex)
        {
            var st = RoadState(mission);
            if (st == null || Biomes == null) return null;
            if (GateSel) { var g = GateNode(mission, sector); if (g != null) return g; }
            return BiomeNode(mission, sector, biomeIndex);
        }

        internal static MissionNodeState BiomeNode(int mission, int sector, int biomeIndex)
        {
            var st = RoadState(mission);
            if (st == null || Biomes == null) return null;
            MissionNodeState n;
            return st.TryGetNodeState(Biomes[biomeIndex].BiomeType, sector, out n) ? n : null;
        }

        /// <summary>The gate node of a sector (the boss-like level at its end); the last sector has none.</summary>
        internal static MissionNodeState GateNode(int mission, int sector)
        {
            var st = RoadState(mission);
            if (st == null || st.SectorStates == null || sector < 0 || sector >= st.SectorStates.Count) return null;
            try { return st.SectorStates[sector].GateState; } catch { return null; }
        }

        internal static bool GateOpen(int mission, int sector)
        {
            var g = GateNode(mission, sector);
            if (g == null) return false;
            if (IgnoreLocks) return true;
            return !g.IsLocked && SectorOpen(mission, sector);
        }

        /// <summary>Mission points of the save for this mission type (what the game's unlock costs are paid in).</summary>
        internal static int Points(int mission)
        {
            Services();
            try { return _road != null ? _road.GetMissionPoints((EMainMissionType)mission) : 0; } catch { return 0; }
        }

        private static int BiomeCost(int mission, int sector, int biomeIndex) { var n = BiomeNode(mission, sector, biomeIndex); return n != null ? n.UnlocksAt : 0; }
        private static int GateCost(int mission, int sector) { var g = GateNode(mission, sector); return g != null ? g.UnlocksAt : 0; }

        internal static bool SectorOpen(int mission, int sector)
        {
            if (IgnoreLocks) return true;
            var st = RoadState(mission);
            return st != null && st.IsUnlocked && sector < st.SectorStates.Count && !st.SectorStates[sector].IsLocked;
        }

        internal static bool BiomeOpen(int mission, int sector, int biomeIndex)
        {
            if (IgnoreLocks) return true;
            var n = NodeOf(mission, sector, biomeIndex);
            return n != null && !n.IsLocked;
        }

        internal static bool DangerOpen(int mission, int sector, int biomeIndex, int danger)
        {
            if (IgnoreLocks) return true;
            Services();
            var n = NodeOf(mission, sector, biomeIndex);
            bool gate = n != null && n.Data != null && n.Data.Type.ToString() == "GATE";
            if (gate) return danger >= n.MinHazIndex && (danger == n.MinHazIndex || (_save != null && _save.IsHazIndexUnlocked(danger)));       // a gate starts at its own minimum level whatever the save has unlocked
            return _save != null && _save.IsHazIndexUnlocked(danger) && (n == null || danger >= n.MinHazIndex);
        }

        internal static bool ClassOpen(int classIndex)
        {
            if (IgnoreLocks) return true;
            Services();
            return _save != null && _save.IsClassUnlocked(Classes[classIndex].Dwarf);
        }

        internal static bool SubOpen(ClassArtifactData a)
        {
            if (IgnoreLocks) return true;
            Services();
            return a != null && _save != null && _save.IsUnlocked(a);
        }

        /// <summary>Make the selection valid: whatever is locked is replaced by the nearest open choice, in dependency order.</summary>
        private static void Normalize()
        {
            if (!SectorOpen(MissionType, Sector)) for (int i = 0; i < 5; i++) if (SectorOpen(MissionType, i)) { Sector = i; break; }
            if (GateSel && !GateOpen(MissionType, Sector)) GateSel = false;
            if (GateSel)
            {
                var gn = GateNode(MissionType, Sector);
                for (int i = 0; gn != null && i < Biomes.Length; i++) if (Biomes[i].BiomeType == gn.Data.Biome) { BiomeIndex = i; break; }      // the gate fixes the biome
            }
            else if (!BiomeOpen(MissionType, Sector, BiomeIndex)) for (int i = 0; i < Biomes.Length && i < 6; i++) if (BiomeOpen(MissionType, Sector, i)) { BiomeIndex = i; break; }
            if (!DangerOpen(MissionType, Sector, BiomeIndex, Danger)) for (int i = 0; i < 5; i++) if (DangerOpen(MissionType, Sector, BiomeIndex, i)) { Danger = i; break; }
            NormalizePlayer(P1); NormalizePlayer(P2);
        }

        private static void NormalizePlayer(PlayerPick p)
        {
            if (!ClassOpen(p.ClassIndex)) for (int i = 0; i < 4; i++) if (ClassOpen(i)) { Defaults(p, i); break; }
            if (!SubOpen(p.Sub)) { foreach (var a in SubsFor(Classes[p.ClassIndex])) if (SubOpen(a)) { p.Sub = a; break; } }
        }

        internal static bool Valid()
        {
            return SectorOpen(MissionType, Sector) && (GateSel ? GateOpen(MissionType, Sector) : BiomeOpen(MissionType, Sector, BiomeIndex)) && DangerOpen(MissionType, Sector, BiomeIndex, Danger)
                && ClassOpen(P1.ClassIndex) && ClassOpen(P2.ClassIndex) && SubOpen(P1.Sub) && SubOpen(P2.Sub);
        }

        // ---- build -------------------------------------------------------------------------------------------
        internal static bool Build(TMP_FontAsset font)
        {
            if (_root != null) return true;
            if (!LoadData()) { Lc.LineOnce("ui: co-op menu data not available yet"); return false; }
            _font = font;
            MissionType = 0; Sector = 0; Danger = 2; BiomeIndex = 0;
            { int fc = 0; for (int i = 0; i < Classes.Length; i++) if (DisplayPos(i) == 0) { fc = i; break; } Defaults(P1, fc); Defaults(P2, fc); }      // hand test: the focus started on the second class of the list: the first one shown is the default

            _root = new GameObject("LcCoopMenu");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            var rt = _root.GetComponent<RectTransform>();

            var bg = Rect(rt, "Background", 0, 0, 1920, 1080, new Color(0.075f, 0.075f, 0.08f, 1f));
            Stretch(bg);
            // the map pages (sector, biome, danger) sit on an artwork of the game instead of the flat dark colour, like the game's own campaign screens
            var back = Rect(bg, "Backdrop", 0, 0, 1920, 1080, new Color(0.5f, 0.5f, 0.5f, 1f));
            Stretch(back); back.GetComponent<Image>().raycastTarget = false;
            back.gameObject.SetActive(false);

            Label(rt, "LOCAL CO-OP", 34, 24, 600, 60, 44, Gold, TextAlignmentOptions.MidlineLeft, true);
            Label(rt, "TWO DWARVES. ONE RUN.", 1300, 28, 590, 50, 30, new Color(0.45f, 0.62f, 0.85f), TextAlignmentOptions.MidlineRight, true);
            Values["plevel"] = Label(rt, "", 760, 28, 500, 50, 30, Gold, TextAlignmentOptions.MidlineLeft, true);       // hand test: the player level (it opens the classes) was nowhere to be seen

            // tab bar (the real tabs live in the game's own menu; here the active one is shown)
            Tab(rt, "CAMPAIGN", 520, false);
            Tab(rt, "LOCAL CO-OP", 775, true);

            BuildLeft(rt);
            BuildPlayer(rt, "P1", 560, OrangeP1, P1);
            BuildPlayer(rt, "P2", 1045, BlueP2, P2);
            BuildOverview(rt);
            BuildGoals(rt);
            BuildLobby(rt);
            BuildSectorPage(rt);
            BuildBiomePage(rt);
            Values["crumb"] = Label(rt, "", 34, 100, 1200, 56, 34, Gold, TextAlignmentOptions.MidlineLeft, true);
            Values["pointsTop"] = Label(rt, "", 1450, 100, 440, 56, 30, Gold, TextAlignmentOptions.MidlineRight, true);
            BuildPick(rt, 0); BuildPick(rt, 1);
            BuildFinal(rt, 0); BuildFinal(rt, 1);
            BuildFinalGoals(rt);
            _startHint = Label(rt, "", 1000, 990, 480, 60, 26, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.MidlineRight, true).gameObject;
            Values["starthint"] = _startHint.GetComponent<TextMeshProUGUI>();
            Btn(rt, "confirm", "CONFIRM MISSION", 1450, 975, 440, 80, Gold, 38, () => { RefreshAll(); if (Valid()) ShowMode("pick"); }, new Color(0.1f, 0.07f, 0.0f));

            Btn(rt, "back", "BACK", 34, 985, 230, 62, Idle, 30, () => BackStep());
            Btn(rt, "start", "START (P1)", 1500, 975, 390, 80, Gold, 40, () => { if (Mode != "final" || ((P2Ready || Lc.Has("-lcnoready")) && Valid())) CoopStart.Begin(); }, new Color(0.1f, 0.07f, 0.0f));

            Btn(rt, "revive", "REVIVE: ON", 640, 975, 300, 62, Idle, 28, () => { Revive.On = !Revive.On; RefreshAll(); });

            _root.SetActive(false);
            SetMode("classic");
            RefreshAll();
            Lc.Line("ui: co-op menu built (" + Buttons.Count + " buttons)");
            return true;
        }

        private static void Tab(RectTransform parent, string text, float x, bool active)
        {
            var r = Rect(parent, "Tab" + text, x, 105, 240, 52, active ? new Color(0.35f, 0.25f, 0.04f, 1f) : Idle);
            if (active) Mark(r.gameObject, "tab", Gold, 3f, true);
            Label(r, text, 0, 0, 240, 52, 26, active ? Gold : Text, TextAlignmentOptions.Midline, true);
        }

        private static void BuildLeft(RectTransform parent)
        {
            var p = Rect(parent, "MissionSettings", 34, 185, 500, 785, Panel);
            Label(p, "MISSION SETTINGS", 20, 8, 300, 44, 28, Text, TextAlignmentOptions.MidlineLeft, true);
            Values["points"] = Label(p, "", 300, 8, 190, 44, 20, Gold, TextAlignmentOptions.MidlineRight, true);

            Label(p, "MISSION TYPE", 20, 56, 460, 26, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            string[] mt = { "ELIMINATION", "ESCORT" };
            string[] md = { "Defeat all enemies in the area.", "Protect the Drilldozer and escort it to the drop pod." };
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                var b = Btn(p, "mission" + i, "", 20 + i * 240, 84, 230, 110, Idle, 20, () => { MissionType = k; RefreshAll(); });
                Label(b.transform as RectTransform, mt[i], 8, 14, 214, 36, 26, Text, TextAlignmentOptions.Midline, true);
                Label(b.transform as RectTransform, md[i], 10, 52, 210, 54, 17, new Color(0.7f, 0.76f, 0.85f), TextAlignmentOptions.Top, false);
                var ul = Label(b.transform as RectTransform, "", 8, 88, 214, 20, 15, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.Midline, true); ul.enableWordWrapping = false; Values["mission" + i + "cost"] = ul;
            }

            Label(p, "SECTOR", 20, 204, 460, 26, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                var b = Btn(p, "sector" + i, (i + 1).ToString(), 20 + i * 92, 232, 84, 52, Idle, 28, () => { Sector = k; GateSel = false; RefreshAll(); });
                CostLabel(b.transform as RectTransform, "sector" + i, 84, 52);
            }

            Label(p, "GATE (END OF A SECTOR)", 20, 290, 460, 22, 16, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = Btn(p, "gate" + i, "GATE " + (i + 1), 20 + i * 118, 312, 108, 44, Idle, 22, () => { Sector = k; GateSel = true; RefreshAll(); });
                CostLabel(b.transform as RectTransform, "gate" + i, 108, 44);
            }

            Label(p, "DANGER", 20, 362, 460, 26, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            for (int i = 0; i < 5; i++) { int k = i; Btn(p, "danger" + i, (i + 1).ToString(), 20 + i * 92, 388, 84, 46, Idle, 28, () => { Danger = k; RefreshAll(); }); }

            Values["biomehead"] = Label(p, "BIOME", 20, 442, 460, 26, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            for (int i = 0; i < Biomes.Length && i < 6; i++)
            {
                int k = i;
                var b = Btn(p, "biome" + i, "", 20, 470 + i * 51, 460, 47, Idle, 20, () => { BiomeIndex = k; GateSel = false; RefreshAll(); });
                var thumb = Rect(b.transform as RectTransform, "Thumb", 4, 3, 100, 41, new Color(1, 1, 1, 1));
                try { var img = thumb.GetComponent<Image>(); var sp = Biomes[i].BiomeData != null ? Biomes[i].BiomeData.IllustrationBar : null; if (sp != null) img.sprite = sp; img.preserveAspect = false; } catch { }
                Label(b.transform as RectTransform, BiomeName(Biomes[i]), 114, 3, 250, 41, 24, Text, TextAlignmentOptions.MidlineLeft, true);
                CostLabel(b.transform as RectTransform, "biome" + i, 460, 47);
            }
        }

        /// <summary>The unlock requirement shown on a locked choice (what the game shows on its locked hexes): points, or what to clear.</summary>
        private static void CostLabel(RectTransform btn, string key, float w, float h)
        {
            var t = Label(btn, "", w - 150, h - 22, 146, 20, 15, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.MidlineRight, true);
            t.enableWordWrapping = false;
            Values[key + "cost"] = t;
        }

        private static void BuildPlayer(RectTransform parent, string tag, float x, Color accent, PlayerPick pick)
        {
            var p = Rect(parent, "Panel" + tag, x, 185, 470, 615, Panel);
            Mark(p.gameObject, "panel" + tag, accent, 3f, true);
            var badge = Rect(p, "Badge", 0, 0, 90, 56, accent);
            Label(badge, tag, 0, 0, 90, 56, 36, new Color(0.08f, 0.05f, 0f), TextAlignmentOptions.Midline, true);
            Label(p, tag == "P1" ? "Player 1" : "Player 2", 110, 0, 340, 56, 30, Text, TextAlignmentOptions.MidlineLeft, true);

            Label(p, "CLASS", 250, 70, 200, 26, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                Btn(p, tag + "class" + i, Classes[i].DisplayName, 250, 98 + DisplayPos(i) * 62, 205, 54, Idle, 26, () => PickClass(pick, tag, k));
            }

            var illu = Rect(p, "Preview", 10, 70, 230, 400, new Color(1, 1, 1, 0f));
            var pi = illu.GetComponent<Image>();
            pi.preserveAspect = true;
            Images[tag + "preview"] = pi;

            Label(p, "SUBCLASS", 20, 486, 300, 26, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var b = Btn(p, tag + "sub" + i, "", 20 + i * 92, 516, 84, 84, Idle, 18, () => PickSub(pick, k));
                var icon = Rect(b.transform as RectTransform, "Icon", 6, 6, 72, 72, new Color(1, 1, 1, 1));
                icon.GetComponent<Image>().preserveAspect = true;
                Images[tag + "subicon" + i] = icon.GetComponent<Image>();
            }
            var subName = Label(p, "", 290, 516, 170, 84, 20, Text, TextAlignmentOptions.MidlineLeft, false);
            Values[tag + "subname"] = subName;
        }

        private static void Toggle(string name, bool on)
        {
            var t = _root.transform.Find(name);
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        /// <summary>Which widgets of the menu are shown (the menu is one canvas, the modes only switch parts of it).</summary>
        internal static void SetMode(string mode)
        {
            Mode = mode;
            bool c = mode == "classic", l = mode == "lobby", sc = mode == "sector", bi = mode == "biome", dg = mode == "danger", p = mode == "pick", f = mode == "final";
            Toggle("MissionSettings", c); Toggle("PanelP1", c); Toggle("PanelP2", c); Toggle("Goals", c); Toggle("TabCAMPAIGN", c); Toggle("TabLOCAL CO-OP", c);
            { var bd = _root.transform.Find("Background/Backdrop"); bool on = (sc || bi || dg) && !Lc.Has("-lcnobackdrop"); if (bd != null) { if (on && bd.GetComponent<Image>().sprite == null) bd.GetComponent<Image>().sprite = Spr("DRS_PromoPoster_Blur"); if (bd.gameObject.activeSelf != on) bd.gameObject.SetActive(on); } }
            Toggle("Overview", c || f); Toggle("Lobby", l); Toggle("TypeCards", l); Toggle("SectorPage", sc); Toggle("BiomePage", bi); Toggle("BiomeInfo", bi); Toggle("DangerPage", dg); Toggle("DangerInfo", dg);
            Toggle("PickP1", p); Toggle("PickP2", p); Toggle("FinalP1", f); Toggle("FinalP2", f); Toggle("FinalGoals", f && !Lc.Has("-lcnofinalgoals"));
            Toggle("LobbyHint", l);
            Toggle("AHint", (l || sc) && !Lc.Has("-lcnoahint"));
            Toggle("Btn_start", c || f); Toggle("Btn_confirm", dg); Toggle("Btn_bnext", bi); Toggle("Btn_revive", f);
            if (_startHint != null) _startHint.SetActive(f);
            Values["crumb"].gameObject.SetActive(sc || bi || dg);
            Values["pointsTop"].gameObject.SetActive(sc || bi || dg);
            var ov = _root.transform.Find("Overview");
            if (ov != null) ov.GetComponent<RectTransform>().anchoredPosition = new Vector2(c ? 1545f : 34f, -185f);
        }

        internal static bool BackdropOn { get { try { var bd = _root.transform.Find("Background/Backdrop"); return bd != null && bd.gameObject.activeInHierarchy && bd.GetComponent<Image>().sprite != null; } catch { return false; } } }
        internal static int LinkCount { get { int n = 0; foreach (var kv in Arts) if (kv.Key.StartsWith("link")) n++; return n; } }

        internal static void ShowMode(string mode)
        {
            if (_root == null) return;
            if (mode == "lobby") { Confirmed[0] = Confirmed[1] = false; Stage[0] = Stage[1] = "c"; P2Ready = false; if (Lc.InRunAt < 0f) CoopStart.Started = false; }     // a new start after a finished or abandoned run
            SetMode(mode);
            Lc.Line("ui: page " + mode);
            Show();
        }

        /// <summary>BACK / B: one step back; the first page closes the menu.</summary>
        internal static void BackStep()
        {
            if (Mode == "sector") ShowMode("lobby");
            else if (Mode == "biome") ShowMode("sector");
            else if (Mode == "danger") ShowMode("biome");
            else if (Mode == "pick") ShowMode("danger");
            else if (Mode == "final") { P2Ready = false; ShowMode("pick"); }
            else Hide();
        }

        // ---- split-screen pick: player 1 on the left half, player 2 on the right half, both at the same time ---------------
        private static readonly Dictionary<string, RectTransform> Fills = new Dictionary<string, RectTransform>();
        private static readonly Dictionary<string, GameObject> Parts = new Dictionary<string, GameObject>();

        private static RectTransform Clear(RectTransform parent, string name, float y, float h)
        {
            var r = Rect(parent, name, 0, y, 940, h, new Color(0, 0, 0, 0));
            r.GetComponent<Image>().raycastTarget = false;
            return r;
        }

        private static void BuildPick(RectTransform parent, int side)
        {
            string tag = side == 0 ? "P1" : "P2";
            Color accent = side == 0 ? OrangeP1 : BlueP2;
            var pick = side == 0 ? P1 : P2;
            var half = Rect(parent, "Pick" + tag, side == 0 ? 10 : 970, 150, 940, 825, Panel);
            Mark(half.gameObject, "pick" + tag, accent, 3f, true);
            var badge = Rect(half, "Badge", 0, 0, 90, 56, accent);
            Label(badge, tag, 0, 0, 90, 56, 36, new Color(0.08f, 0.05f, 0f), TextAlignmentOptions.Midline, true);
            Values[tag + "head"] = Label(half, "", 110, 0, 820, 56, 30, Text, TextAlignmentOptions.MidlineLeft, true);

            // class page
            var cs = Clear(half, "StageC", 66, 759); Parts[tag + "C"] = cs.gameObject;
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = Btn(cs, tag + "c" + i, "", 20 + DisplayPos(i) * 228, 10, 212, 250, Clear0, 24, () => { Defaults(pick, k); RefreshAll(); });
                string dw = Classes[i].Dwarf.ToString(); dw = dw.Substring(0, 1) + dw.Substring(1).ToLowerInvariant();
                var sp = Spr("Hex_Portrait_" + dw) ?? Classes[i].IllustrationVertical ?? Classes[i].Illustration;
                Art(b.transform as RectTransform, "Portrait", 0, 0, 212, 200, sp, Color.white);
                Values[tag + "cname" + i] = Label(b.transform as RectTransform, Classes[i].DisplayName, 0, 198, 212, 52, 28, Text, TextAlignmentOptions.Midline, true);
                Frames[tag + "c" + i] = Art(b.transform as RectTransform, "Frame", -6, -6, 224, 212, Spr("HexButton_C_Frame-Hover"), Gold);
                // the class level as the game shows it: a number badge and the progress to the next level
                var track = Rect(b.transform as RectTransform, "BarTrack", 36, 192, 140, 8, new Color(0f, 0f, 0f, 0.75f)); track.GetComponent<Image>().raycastTarget = false;
                var fill = Rect(track, "Fill", 0, 0, 140, 8, Gold); fill.GetComponent<Image>().raycastTarget = false;
                var lvlBadge = Rect(b.transform as RectTransform, "LvlBadge", 66, 160, 80, 30, Gold); lvlBadge.GetComponent<Image>().raycastTarget = false;
                Values[tag + "clvl" + i] = Label(lvlBadge, "", 0, 0, 80, 30, 24, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
                Parts[tag + "bar" + i] = track.gameObject; Parts[tag + "lvl" + i] = lvlBadge.gameObject; Fills[tag + "bar" + i] = fill;
            }
            var cbox = Rect(cs, "DescBox", 20, 300, 900, 350, new Color(0.07f, 0.09f, 0.14f, 1f));
            var cbar = Rect(cbox, "Bar", 0, 0, 900, 50, Gold);
            Values[tag + "ctitle"] = Label(cbar, "", 0, 0, 900, 50, 28, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
            Values[tag + "cdesc"] = Label(cbox, "", 24, 66, 852, 130, 24, Text, TextAlignmentOptions.TopLeft, false);
            Values[tag + "cstats"] = Label(cbox, "", 24, 200, 852, 140, 26, Gold, TextAlignmentOptions.TopLeft, true);
            Btn(cs, tag + "next", "CONTINUE", 640, 670, 280, 76, Gold, 34, () => { Stage[side] = "s"; RefreshAll(); }, new Color(0.1f, 0.07f, 0f));

            // subclass page
            var ss = Clear(half, "StageS", 66, 759); Parts[tag + "S"] = ss.gameObject;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var b = Btn(ss, tag + "s" + i, "", 20 + i * 302, 10, 286, 250, Idle, 22, () => { PickSub(pick, k); });
                var icon = Rect(b.transform as RectTransform, "Icon", 53, 14, 180, 170, new Color(1, 1, 1, 1));
                icon.GetComponent<Image>().preserveAspect = true; icon.GetComponent<Image>().raycastTarget = false;
                Images[tag + "sicon" + i] = icon.GetComponent<Image>();
                Values[tag + "sname" + i] = Label(b.transform as RectTransform, "", 0, 190, 286, 56, 26, Text, TextAlignmentOptions.Midline, true);
            }
            for (int j = 0; j < 2; j++)
            {
                var box = Rect(ss, "SubBox" + j, 20 + j * 456, 280, 444, 370, new Color(0.07f, 0.09f, 0.14f, 1f));
                var bar = Rect(box, "Bar", 0, 0, 444, 50, Gold);
                Values[tag + "t" + j] = Label(bar, "", 0, 0, 444, 50, 26, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
                var ic = Rect(box, "Icon", 142, 62, 160, 110, new Color(1, 1, 1, 1));
                ic.GetComponent<Image>().preserveAspect = true; ic.GetComponent<Image>().raycastTarget = false;
                Images[tag + "bicon" + j] = ic.GetComponent<Image>();
                Values[tag + "d" + j] = Label(box, "", 20, 184, 404, 180, 22, Text, TextAlignmentOptions.Top, false);
            }
            Btn(ss, tag + "back", "BACK", 20, 670, 260, 76, Idle, 30, () => { Stage[side] = "c"; RefreshAll(); });
            Btn(ss, tag + "ok", "CONFIRM", 640, 670, 280, 76, Gold, 34, () => { Confirm(side); }, new Color(0.1f, 0.07f, 0f));

            // confirmed
            var ds = Clear(half, "StageD", 66, 759); Parts[tag + "D"] = ds.gameObject;
            Arts[tag + "dportrait"] = Art(ds, "Portrait", 354, 20, 212, 200, null, Color.white); Arts[tag + "dportrait"].raycastTarget = false; Arts[tag + "dportrait"].preserveAspect = true;      // hand test: the confirmed page showed an empty box; the chosen class's picture
            Values[tag + "dname"] = Label(ds, "", 20, 240, 900, 80, 46, Text, TextAlignmentOptions.Midline, true);
            Label(ds, "Confirmed. Waiting for the other player.", 20, 330, 900, 50, 26, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.Midline, false);
            Btn(ds, tag + "edit", "CHANGE", 340, 420, 260, 70, Idle, 30, () => { Unconfirm(side); });
        }

        // what the owner reads in the game (read by hand, not from the game's data): classes open with the player's level, subclasses with the class's level
        /// <summary>The class level of the save (the number the game's own class hex shows) and the progress towards the next one; -1 when unavailable.</summary>
        internal static int ClassLevel(ClassData c, out float progress)
        {
            progress = 0f;
            try
            {
                var m = CoopStart.Resolve<PlayerRankManager>();
                if (m == null) return -1;
                progress = m.GetProgressTowardsNextClassRank(c.Dwarf);
                return m.GetRankForClass(c.Dwarf).Rank;
            }
            catch (Exception e) { Lc.LineOnce("ui: class level failed: " + e.GetType().Name + ": " + e.Message); return -1; }
        }

        /// <summary>The player level a class opens at (0 = free), the owner's reading of the game's menu (see ClassLockText).</summary>
        private static int ClassNeeds(ClassData c)
        {
            switch (c.Dwarf.ToString()) { case "GUNNER": return 3; case "ENGINEER": return 5; case "DRILLER": return 7; default: return 0; }
        }

        /// <summary>Where class i stands in the lists: by the level it opens at (Scout, Gunner, Engineer, Driller), not by the game's data order. Twin `-lcnoclassorder`.</summary>
        internal static int DisplayPos(int i)
        {
            if (Lc.Has("-lcnoclassorder") || Classes == null || i < 0 || i >= Classes.Length) return i;
            int pos = 0;
            for (int j = 0; j < Classes.Length; j++)
                if (j != i && (ClassNeeds(Classes[j]) < ClassNeeds(Classes[i]) || (ClassNeeds(Classes[j]) == ClassNeeds(Classes[i]) && j < i))) pos++;
            return pos;
        }

        private static string ClassLockText(ClassData c)
        {
            switch (c.Dwarf.ToString())
            {
                case "GUNNER": return "Unlocks at player level 3";
                case "ENGINEER": return "Unlocks at player level 5";
                case "DRILLER": return "Unlocks at player level 7";
                default: return "Locked";
            }
        }

        /// <summary>The class level a subclass opens at (0 = free): the owner's reading of the game's menu, the game's data has no such number. Second subclass 9, third 18.</summary>
        internal static int SubLevel(ClassArtifactData a)
        {
            string n = a != null ? a.name : "";
            foreach (var k in new[] { "Recon", "Interrogator", "Juggernaut", "Maintenance" }) if (n.Contains(k)) return 9;
            foreach (var k in new[] { "SharpShooter", "StrongArm", "Gunner_Heavy", "Explosive" }) if (n.Contains(k)) return 18;
            return 0;
        }

        private static string SubLockText(ClassArtifactData a)
        {
            int l = SubLevel(a);
            return l > 0 ? "Unlocks at class level " + l : "Locked";
        }

        private static void Confirm(int side)
        {
            Confirmed[side] = true;
            if (Confirmed[0] && Confirmed[1]) { P2Ready = false; ShowMode("final"); } else RefreshAll();
        }

        private static void Unconfirm(int side)
        {
            Confirmed[side] = false; Stage[side] = "c"; P2Ready = false;
            if (Mode == "final") ShowMode("pick"); else RefreshAll();
        }

        private static void RefreshPick(int side)
        {
            string tag = side == 0 ? "P1" : "P2";
            var pk = side == 0 ? P1 : P2;
            bool done = Confirmed[side], sub = Stage[side] == "s";
            Parts[tag + "C"].SetActive(!done && !sub); Parts[tag + "S"].SetActive(!done && sub); Parts[tag + "D"].SetActive(done);
            var cls = Classes[pk.ClassIndex];
            Values[tag + "head"].text = (side == 0 ? "PLAYER 1: " : "PLAYER 2: ") + (done ? "CONFIRMED" : sub ? "CHOOSE YOUR SUBCLASS" : "CHOOSE YOUR CLASS");
            for (int i = 0; i < 4; i++)
            {
                bool copen = ClassOpen(i);
                SetOpen(tag + "c" + i, copen); SetMark(tag + "c" + i, pk.ClassIndex == i);
                float prog = 0f;
                int lvl = copen ? ClassLevel(Classes[i], out prog) : -1;
                Parts[tag + "lvl" + i].SetActive(lvl >= 0); Parts[tag + "bar" + i].SetActive(lvl >= 0);
                if (lvl >= 0) { Values[tag + "clvl" + i].text = lvl.ToString(); Fills[tag + "bar" + i].sizeDelta = new Vector2(140f * Mathf.Clamp01(prog), 8f); }
                Values[tag + "cname" + i].text = copen ? Classes[i].DisplayName : Classes[i].DisplayName + "\n<size=62%><color=#FFD060>" + ClassLockText(Classes[i]) + "</color></size>";
            }
            if (Arts.ContainsKey(tag + "dportrait")) { string dw0 = cls.Dwarf.ToString(); dw0 = dw0.Substring(0, 1) + dw0.Substring(1).ToLowerInvariant(); var pi = Arts[tag + "dportrait"]; pi.sprite = Spr("Hex_Portrait_" + dw0) ?? cls.IllustrationVertical ?? cls.Illustration; pi.color = Color.white; pi.enabled = pi.sprite != null && !Lc.Has("-lcnoconfirmpic"); }
            Values[tag + "ctitle"].text = cls.DisplayName;
            Values[tag + "cdesc"].text = Clean(cls.Description);
            Values[tag + "cstats"].text = ClassStats(cls);
            var subs = SubsFor(cls);
            for (int i = 0; i < 3; i++)
            {
                bool has = i < subs.Count;
                Buttons[tag + "s" + i].gameObject.SetActive(has);
                if (!has) continue;
                Images[tag + "sicon" + i].sprite = subs[i].Icon;
                string stitle = !string.IsNullOrEmpty(subs[i].Title) ? subs[i].Title : SubName(subs[i]);
                Values[tag + "sname" + i].text = SubOpen(subs[i]) ? stitle : stitle + "\n<size=62%><color=#FFD060>" + SubLockText(subs[i]) + "</color></size>";
                SetOpen(tag + "s" + i, SubOpen(subs[i])); SetMark(tag + "s" + i, subs[i] == pk.Sub);
            }
            var cur = pk.Sub; var w = cur != null ? cur.StarterWeapon : null;
            Values[tag + "t0"].text = cur != null && !string.IsNullOrEmpty(cur.Title) ? cur.Title : SubName(cur);
            Values[tag + "d0"].text = (cur != null && !string.IsNullOrEmpty(cur.Description) ? Clean(cur.Description) + "\n" : "") + SubStats(cur);
            Images[tag + "bicon0"].sprite = cur != null ? cur.Icon : null;
            Values[tag + "t1"].text = w != null ? Clean(w.Title) : "-";
            Values[tag + "d1"].text = w != null ? Clean(w.Description) : "";
            Images[tag + "bicon1"].sprite = w != null ? w.Icon : null;
            SetOpen(tag + "ok", SubOpen(pk.Sub));
            Values[tag + "dname"].text = cls.DisplayName + " / " + SubTitle(pk.Sub);
            if (Lc.Has("-lcpickseq") && side == 1 && !Confirmed[0])        // twin: player 2's half waits for player 1
                foreach (var k in new[] { "P2c0", "P2c1", "P2c2", "P2c3", "P2next", "P2s0", "P2s1", "P2s2", "P2ok", "P2back" }) SetOpen(k, false);
        }

        // ---- final page: the overview, player 2's READY, the host starts ------------------------------------------------
        private static void BuildFinal(RectTransform parent, int side)
        {
            string tag = side == 0 ? "P1" : "P2";
            Color accent = side == 0 ? OrangeP1 : BlueP2;
            var p = Rect(parent, "Final" + tag, side == 0 ? 410 : 960, 185, 520, 700, Panel);
            Mark(p.gameObject, "final" + tag, accent, 3f, true);
            var badge = Rect(p, "Badge", 0, 0, 90, 56, accent);
            Label(badge, tag, 0, 0, 90, 56, 36, new Color(0.08f, 0.05f, 0f), TextAlignmentOptions.Midline, true);
            Label(p, side == 0 ? "Player 1 (host)" : "Player 2", 110, 0, 400, 56, 30, Text, TextAlignmentOptions.MidlineLeft, true);
            var illu = Rect(p, "Preview", 20, 70, 230, 400, new Color(1, 1, 1, 0f));
            illu.GetComponent<Image>().preserveAspect = true; Images[tag + "fimg"] = illu.GetComponent<Image>();
            Values[tag + "fclass"] = Label(p, "", 270, 90, 240, 80, 34, Text, TextAlignmentOptions.TopLeft, true);
            Values[tag + "fsub"] = Label(p, "", 270, 180, 240, 90, 24, new Color(0.7f, 0.76f, 0.85f), TextAlignmentOptions.TopLeft, false);
            Values[tag + "fweap"] = Label(p, "", 270, 280, 240, 120, 22, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.TopLeft, false);
            Btn(p, tag + "fedit", "CHANGE", 20, 490, 230, 60, Idle, 26, () => { Unconfirm(side); });
            if (side == 1)
            {
                Btn(p, "P2ready", "", 20, 590, 480, 90, Idle, 36, () => { P2Ready = !P2Ready; Lc.Line("ui: player 2 ready = " + P2Ready); RefreshAll(); });
                Values["P2readylabel"] = Label(Buttons["P2ready"].transform as RectTransform, "", 0, 0, 480, 90, 38, Text, TextAlignmentOptions.Midline, true);
                var ck = Rect(Buttons["P2ready"].transform as RectTransform, "ReadyCheck", 70, 20, 50, 50, new Color(0.05f, 0.06f, 0.1f, 1f)); var cki = ck.GetComponent<Image>(); cki.raycastTarget = false; cki.enabled = false; Images["p2check"] = cki;
                var cko = ck.gameObject.AddComponent<UnityEngine.UI.Outline>(); cko.effectColor = new Color(0.85f, 0.88f, 0.95f, 1f); cko.effectDistance = new Vector2(2f, 2f);
                var tk = Rect(ck, "Tick", 4, 4, 42, 42, Color.white); var tki = tk.GetComponent<Image>(); tki.sprite = Spr("Icon_Check-Polished") ?? CheckSprite(); tki.raycastTarget = false; Images["p2tick"] = tki;
            }
            else Label(p, "The host starts the run.", 20, 600, 480, 60, 24, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.Midline, false);
        }

        private static void RefreshFinal()
        {
            foreach (var side in new[] { 0, 1 })
            {
                string tag = side == 0 ? "P1" : "P2";
                var pk = side == 0 ? P1 : P2;
                var cls = Classes[pk.ClassIndex];
                var sp = cls.IllustrationVertical ?? cls.Illustration;
                Images[tag + "fimg"].sprite = sp; Images[tag + "fimg"].color = sp != null ? Color.white : new Color(1, 1, 1, 0);
                Values[tag + "fclass"].text = cls.DisplayName;
                Values[tag + "fsub"].text = SubTitle(pk.Sub);
                Values[tag + "fweap"].text = pk.Sub != null && pk.Sub.StarterWeapon != null ? "Starting weapon: " + Clean(pk.Sub.StarterWeapon.Title) : "";
            }
            Values["P2readylabel"].text = P2Ready ? "READY" : "READY UP";
            if (Images.ContainsKey("p2check")) {
                // hand test: a check mark that appears from nowhere says nothing; an empty box is there first and gets ticked
                var ci = Images["p2check"]; bool twin = Lc.Has("-lcnoreadycheck");
                ci.enabled = !twin && (P2Ready || !Lc.Has("-lcnoreadybox"));
                if (Images.ContainsKey("p2tick")) Images["p2tick"].enabled = P2Ready && !twin;
            }
            var rg = Buttons["P2ready"].GetComponent<Image>(); if (rg != null) rg.color = P2Ready ? new Color(0.2f, 0.55f, 0.3f, 1f) : Idle;
            if (Mode == "final") SetOpen("start", (P2Ready || Lc.Has("-lcnoready")) && Valid());
            Values["starthint"].text = P2Ready ? "Everyone is ready" : "Waiting for player 2";
            var rv = Buttons["revive"];
            var rvt = rv.GetComponentInChildren<TextMeshProUGUI>(true); if (rvt != null) rvt.text = Loc.T(Revive.On ? "REVIVE: ON" : "REVIVE: OFF");
            var rvi = rv.GetComponent<Image>();
            if (Lc.Has("-lcrevivebutton")) { if (rvi != null) rvi.color = Revive.On ? new Color(0.2f, 0.55f, 0.3f, 1f) : Idle; }
            else { if (rvi != null) rvi.color = Clear0; if (rvt != null) rvt.color = Revive.On ? new Color(0.45f, 0.88f, 0.52f, 1f) : new Color(0.62f, 0.64f, 0.7f, 1f); }      // a prompt with its Y icon, not a button
        }


        private static void BuildGoals(RectTransform parent)
        {
            var p = Rect(parent, "Goals", 560, 812, 930, 165, Panel);
            string[] heads = { "MAIN MISSION GOALS", "BIOME GOALS", "MUTATOR" };
            for (int i = 0; i < 3; i++)
            {
                float x = 14 + i * 308;
                Label(p, heads[i], x, 6, 296, 26, 19, Gold, TextAlignmentOptions.MidlineLeft, true);
                var t = Label(p, "", x, 34, 296, 126, 16, Text, TextAlignmentOptions.TopLeft, false);
                t.overflowMode = TextOverflowModes.Ellipsis;
                Values["goals" + i] = t;
            }
        }

        /// <summary>The game's texts carry font and material tags for its own font assets; they are dropped (colors stay).</summary>
        internal static string Clean(string t)
        {
            return string.IsNullOrEmpty(t) ? "" : System.Text.RegularExpressions.Regex.Replace(t, "</?(font|material)[^>]*>", "");
        }

        private static string GoalLine(MilestoneData g, bool done)
        {
            string text = Clean(g.GetFormattedDescriptionText());
            return done ? "<color=#7C9A7C><s>" + text + "</s></color>" : "- " + text;
        }

        private static bool BiomeGoalDone(MilestoneData g)
        {
            Services();
            try
            {
                if (_tracker == null) return false;
                foreach (var v in _tracker.Milestones) if (v != null && v.Data == g) return v.IsCompleted;
            }
            catch { }
            return false;
        }

        /// <summary>Goals and mutator of the chosen mission: the node's main mission goals (done ones struck through), the biome goals of
        /// the chosen danger level, and the node's mutators.</summary>
        /// <summary>The modifiers of a node: a sector node's mutators, a gate's starting mutators (blessing first, then the penalties).</summary>
        internal static Il2CppSystem.Collections.Generic.List<MutatorData> Mutators(MissionNodeState node)
        {
            try
            {
                if (node.Data.Type.ToString() == "GATE" && node.Data.FixedGateData != null && node.Data.FixedGateData.StartingMutators != null) return node.Data.FixedGateData.StartingMutators;
                return node.Data.Mutators;
            }
            catch { return null; }
        }

        internal static void RefreshGoals()
        {
            var node = NodeOf(MissionType, Sector, BiomeIndex);
            var main = new StringBuilder();
            var mut = new StringBuilder();
            if (node != null)
            {
                foreach (var g in node.GetMissionGoalStates()) main.Append(GoalLine(g.Data, g.IsComplete)).Append('\n');
                var nm = Mutators(node);
                if (nm != null)
                    foreach (var m in nm) mut.Append("<b>").Append(Clean(m.GetTitle())).Append("</b>\n").Append(Clean(m.GetDescription())).Append('\n');
            }
            if (mut.Length == 0) mut.Append("None");
            var bio = new StringBuilder();
            var hl = Biomes[BiomeIndex].HazardLevels;
            if (hl != null && Danger < hl.Length && hl[Danger].Goals != null)
                foreach (var g in hl[Danger].Goals) bio.Append(GoalLine(g, BiomeGoalDone(g))).Append('\n');
            if (Values.ContainsKey("fgoals1")) { Values["fgoals1"].text = bio.ToString(); Values["fgoals2"].text = mut.ToString(); }
            Values["goals0"].text = main.ToString();
            Values["goals1"].text = bio.ToString();
            Values["goals2"].text = mut.ToString();
            if (Parts.ContainsKey("grb0")) { FillGoalRows("b", node); FillGoalRows("d", node); FillGoalRows("f", node); }
            if (Values.ContainsKey("bgoals0") && !Lc.Has("-lcnobig")) { Values["bgoals0"].text = main.ToString(); Values["bgoals1"].text = main.ToString(); Values["bgoals2"].text = mut.ToString(); }
        }

        private static void BuildOverview(RectTransform parent)
        {
            var p = Rect(parent, "Overview", 1545, 185, 345, 640, Panel);
            Label(p, "RUN OVERVIEW", 20, 8, 305, 44, 28, Text, TextAlignmentOptions.MidlineLeft, true);
            string[] rows = { "Mission Type", "Sector", "Danger", "Biome", "Players", "Classes", "Subclasses" };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = 64 + i * 78;
                Label(p, rows[i], 20, y, 305, 28, 20, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.MidlineLeft, false);
                Values["ov" + i] = Label(p, "", 20, y + 28, 305, 48, i >= 5 ? 20 : 26, Text, TextAlignmentOptions.TopLeft, true);
            }
        }

        // ---- choices ---------------------------------------------------------------------------------------
        private static void PickClass(PlayerPick pick, string tag, int index)
        {
            Defaults(pick, index);
            if (ShareTwin && pick == P1) Defaults(P2, index);
            RefreshAll();
        }

        private static void PickSub(PlayerPick pick, int subIndex)
        {
            var subs = SubsFor(Classes[pick.ClassIndex]);
            if (subIndex < subs.Count) pick.Sub = subs[subIndex];
            if (ShareTwin && pick == P1) { var s2 = SubsFor(Classes[P2.ClassIndex]); if (subIndex < s2.Count) P2.Sub = s2[subIndex]; }
            RefreshAll();
        }

        internal static string BiomeName(BiomeLevelData b)
        {
            string n = b != null ? b.name : "?";
            return Humanize(n.StartsWith("BiomeLevelData_") ? n.Substring("BiomeLevelData_".Length) : n);
        }

        /// <summary>"SaltPits" -> "Salt Pits", "Driller_StrongArm" -> "Driller Strong Arm".</summary>
        internal static string Humanize(string n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (c == '_') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && char.IsLower(n[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The subclass name as the game shows it (its own title), with the asset name as the fallback.</summary>
        internal static string SubTitle(ClassArtifactData a)
        {
            return a != null && !string.IsNullOrEmpty(a.Title) ? a.Title : SubName(a);
        }

        internal static string SubName(ClassArtifactData a)
        {
            if (a == null) return "-";
            string n = a.name;
            int i = n.IndexOf('_');
            return Humanize(i >= 0 ? n.Substring(i + 1) : n);
        }

        /// <summary>The overview as text (the same strings the screen shows), for tests and logs.</summary>
        internal static string Summary()
        {
            return "type=" + (MissionType == 0 ? "Elimination" : "Escort") + " sector=" + (Sector + 1) + (GateSel ? " gate=yes" : "") + " danger=" + (Danger + 1)
                + " biome=" + BiomeName(Biomes[BiomeIndex]) + " P1=" + Classes[P1.ClassIndex].Dwarf + "/" + SubName(P1.Sub)
                + " P2=" + Classes[P2.ClassIndex].Dwarf + "/" + SubName(P2.Sub);
        }

        /// <summary>Refreshes the texts and translates them in the same frame: the sweep every half second alone showed the English text for a moment (flicker).</summary>
        internal static void RefreshAll()
        {
            RefreshAllCore();
            if (!Lc.Has("-lcnoinstantloc")) Loc.SweepNow(_root != null ? _root.transform : null);
        }

        private static void RefreshAllCore()
        {
            if (_root == null) return;
            foreach (var ek in new[] { "P1edit", "P2edit", "P1fedit", "P2fedit" }) { Button eb; if (Buttons.TryGetValue(ek, out eb) && eb != null) { var ic = eb.transform.Find("PadIcon_A"); if (ic != null) ic.gameObject.SetActive(MenuPad.Split); } }
            try { int pl = PlayerLevel(); Values["plevel"].text = pl >= 0 && !Lc.Has("-lcnoplevel") ? "PLAYER LEVEL " + pl : ""; } catch { }
            if (Mode == "lobby") { RefreshLobby(); return; }
            Normalize();
            for (int i = 0; i < 2; i++) SetOpen("mission" + i, SectorOpen(i, 0));
            for (int i = 0; i < 2; i++) { bool op = SectorOpen(i, 0) && !(i == 1 && Lc.Has("-lcfakeescortlock")); int ml = !op ? MissionUnlockLevel(i) : 0; Values["mission" + i + "cost"].text = op ? "" : ml > 0 ? "Unlocks at player level " + ml : "LOCKED"; }
            for (int i = 0; i < 5; i++) { SetOpen("sector" + i, SectorOpen(MissionType, i)); SetOpen("danger" + i, DangerOpen(MissionType, Sector, BiomeIndex, i)); }
            for (int i = 0; i < 4; i++) SetOpen("gate" + i, GateOpen(MissionType, i));
            for (int i = 0; i < Biomes.Length && i < 6; i++) SetOpen("biome" + i, !GateSel && BiomeOpen(MissionType, Sector, i));
            Values["points"].text = "POINTS " + Points(MissionType);
            Values["biomehead"].text = GateSel ? "BIOME (FIXED BY THE GATE)" : "BIOME";
            // what a locked choice needs, as the game shows it on its locked hexes
            for (int i = 0; i < 5; i++) Values["sector" + i + "cost"].text = !SectorOpen(MissionType, i) ? (Lc.Has("-lcsectortext") ? (i > 0 ? "CLEAR GATE " + i : "") : "LOCKED") : "";
            for (int i = 0; i < 4; i++) { int c = GateCost(MissionType, i); Values["gate" + i + "cost"].text = !Lc.Has("-lcsectortext") ? "" : !GateOpen(MissionType, i) ? (c > 0 && !SectorOpen(MissionType, i) ? "LOCKED" : c > 0 ? c + " PTS" : "LOCKED") : ""; }
            for (int i = 0; i < Biomes.Length && i < 6; i++) { int c = BiomeCost(MissionType, Sector, i); Values["biome" + i + "cost"].text = !GateSel && !BiomeOpen(MissionType, Sector, i) ? (c > 0 ? c + " PTS" : "LOCKED") : ""; }
            foreach (var tag in new[] { "P1", "P2" })
            {
                var pk = tag == "P1" ? P1 : P2;
                for (int i = 0; i < 4; i++) SetOpen(tag + "class" + i, ClassOpen(i));
                var subs = SubsFor(Classes[pk.ClassIndex]);
                for (int i = 0; i < 3 && i < subs.Count; i++) SetOpen(tag + "sub" + i, SubOpen(subs[i]));
            }
            SetOpen("start", Valid());
            RefreshGoals();
            SetMark("mission0", MissionType == 0); SetMark("mission1", MissionType == 1);
            for (int i = 0; i < 5; i++) { SetMark("sector" + i, Sector == i && !GateSel); SetMark("danger" + i, Danger == i); }
            for (int i = 0; i < 4; i++) SetMark("gate" + i, GateSel && Sector == i);
            for (int i = 0; i < Biomes.Length && i < 6; i++) SetMark("biome" + i, BiomeIndex == i);
            RefreshPlayer("P1", P1); RefreshPlayer("P2", P2);

            Values["ov0"].text = MissionType == 0 ? "Elimination" : "Escort";
            Values["ov1"].text = GateSel ? "Gate " + (Sector + 1) : (Sector + 1).ToString();
            Values["ov2"].text = (Danger + 1).ToString();
            Values["ov3"].text = BiomeName(Biomes[BiomeIndex]);
            Values["ov4"].text = "2";
            Values["ov5"].text = Classes[P1.ClassIndex].DisplayName + " / " + Classes[P2.ClassIndex].DisplayName;
            Values["ov6"].text = SubName(P1.Sub) + " / " + SubName(P2.Sub);
            Values["crumb"].text = (MissionType == 0 ? "ELIMINATION" : "ESCORT") + "   >   " + (Mode == "sector" ? "SECTOR" : (GateSel ? "GATE " + (Sector + 1) : "SECTOR " + (Sector + 1)) + (Mode == "danger" ? "   >   " + BiomeName(Biomes[BiomeIndex]).ToUpperInvariant() + "   >   DANGER" : "   >   BIOME"));
            Values["pointsTop"].text = "POINTS " + Points(MissionType);
            RefreshLobby(); RefreshSector(); RefreshBiomePage();
            RefreshPick(0); RefreshPick(1); RefreshFinal();
        }

        private static void RefreshPlayer(string tag, PlayerPick pick)
        {
            for (int i = 0; i < 4; i++) SetMark(tag + "class" + i, pick.ClassIndex == i);
            var subs = SubsFor(Classes[pick.ClassIndex]);
            for (int i = 0; i < 3; i++)
            {
                bool has = i < subs.Count;
                var btn = Buttons[tag + "sub" + i];
                btn.gameObject.SetActive(has);
                if (!has) continue;
                Images[tag + "subicon" + i].sprite = subs[i].Icon;
                SetMark(tag + "sub" + i, subs[i] == pick.Sub);
            }
            var pv = Images[tag + "preview"];
            var sp = Classes[pick.ClassIndex].IllustrationVertical ?? Classes[pick.ClassIndex].Illustration;
            pv.sprite = sp;
            pv.color = sp != null ? Color.white : new Color(1, 1, 1, 0);
            Values[tag + "subname"].text = SubName(pick.Sub);
        }

        /// <summary>Locked choices are greyed out and cannot be clicked.</summary>
        private static void SetOpen(string key, bool open)
        {
            Button b;
            if (!Buttons.TryGetValue(key, out b) || b == null) return;
            b.interactable = open;
            var g = b.GetComponent<CanvasGroup>();
            if (g != null) { g.alpha = open ? 1f : 0.28f; }
        }

        internal static bool IsOpen(string key)
        {
            Button b;
            return Buttons.TryGetValue(key, out b) && b != null && b.interactable;
        }

        internal static bool IsSelected(string key) { Image fr; if (Frames.TryGetValue(key, out fr)) return fr != null && fr.enabled; return Marked(key); }
        internal static bool Marked(string key) { Outline o; return Marks.TryGetValue(key, out o) && o != null && o.enabled; }

        private static void SetMark(string key, bool on)
        {
            Image fr;
            if (Frames.TryGetValue(key, out fr)) { fr.enabled = on; return; }
            Outline o;
            if (Marks.TryGetValue(key, out o) && o != null) o.enabled = on;
        }

        // ---- show / hide -------------------------------------------------------------------------------------
        internal static void Show() { if (_root != null) { _root.SetActive(true); MenuPad.OnShow(); RefreshAll(); Lc.Line("ui: co-op menu shown"); } }
        internal static void Hide() { if (_root != null) { _root.SetActive(false); MenuPad.OnHide(); Lc.Line("ui: co-op menu closed (back)"); } }

        // ---- geometry in the 1920x1080 reference space (every widget is anchored top-left) -------------------------------
        internal static Vector2 RefCenter(Component c)
        {
            var rt = c.GetComponent<RectTransform>();
            float x = 0f, y = 0f;
            for (Transform t = rt; t != null && t.parent != null; t = t.parent)
            {
                var r = t.GetComponent<RectTransform>();
                if (r == null) break;
                x += r.anchoredPosition.x; y += r.anchoredPosition.y;
            }
            return new Vector2(x + rt.rect.width * 0.5f, y - rt.rect.height * 0.5f);
        }

        /// <summary>A thin border around the focused choice (no fill, nothing is washed over the button).</summary>
        internal static RectTransform MakeFrame(Color color)
        {
            if (_root == null) return null;
            var root = new GameObject("PadFocus");
            var rt = root.AddComponent<RectTransform>();
            rt.SetParent(_root.GetComponent<RectTransform>(), false);
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(10f, 10f);
            const float t = 4f;
            AddEdge(rt, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -t), new Vector2(0f, 0f), color);
            AddEdge(rt, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, t), color);
            AddEdge(rt, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(t, 0f), color);
            AddEdge(rt, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-t, 0f), new Vector2(0f, 0f), color);
            foreach (var nm in new[] { "Glow", "Shape" })
            {
                var go = new GameObject(nm); var r = go.AddComponent<RectTransform>(); r.SetParent(rt, false);
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f);
                var im = go.AddComponent<Image>(); im.raycastTarget = false; im.preserveAspect = false; go.SetActive(false);
            }
            for (int bi = 0; bi < 5; bi++)
            {
                var go = new GameObject("Br" + bi); var r = go.AddComponent<RectTransform>(); r.SetParent(rt, false);
                var im = go.AddComponent<Image>(); im.raycastTarget = false; go.SetActive(false);
            }
            return rt;
        }

        /// <summary>Hand test: the game's own menus put small blue brackets around the focused hex or gate. Hexes: four corners (sector: two corners and a line below), gates: two corner pieces and a V.</summary>
        private static void SetBrackets(RectTransform frame, string key, float w, float h, Color col)
        {
            bool on = !Lc.Has("-lcnobrackets") && !string.IsNullOrEmpty(key);
            bool card = on && key.StartsWith("type");
            bool gate = on && key.StartsWith("gat"), hexSec = on && key.StartsWith("sec"), hex = on && (key.StartsWith("bio") || (key.Length == 4 && key[0] == 'P' && key[2] == 'c'));
            bool up = false; if (gate) { int gi; int.TryParse(key.Substring(3), out gi); up = gi % 2 == 1; }
            // slot: sprite, anchor x, anchor y, offset x, offset y, width, height, flip
            object[][] spec = null;
            if (hex) spec = new[] { new object[] { "HexButton_Bracket_01", 0f, 1f, 0.15f * w, 10f, 40f, 28f, false }, new object[] { "HexButton_Bracket_02", 1f, 1f, -0.15f * w, 10f, 40f, 28f, false }, new object[] { "HexButton_2025_Bracket_04", 0f, 0f, 0.15f * w, -10f, 40f, 28f, false }, new object[] { "HexButton_2025_Bracket_03", 1f, 0f, -0.15f * w, -10f, 40f, 28f, false } };
            else if (card) spec = new[] { new object[] { "corner", 0f, 1f, 0f, 0f, 34f, 24f, false, false }, new object[] { "corner", 1f, 1f, 0f, 0f, 34f, 24f, false, true }, new object[] { "corner", 0f, 0f, 0f, 0f, 34f, 24f, true, false }, new object[] { "corner", 1f, 0f, 0f, 0f, 34f, 24f, true, true } };
            else if (hexSec) spec = new[] { new object[] { "HexButton_Bracket_01", 0f, 1f, 0.15f * w, 10f, 40f, 28f, false }, new object[] { "HexButton_Bracket_02", 1f, 1f, -0.15f * w, 10f, 40f, 28f, false }, new object[] { "HexButton_Bracket_03", 0.5f, 0f, 0f, -14f, 100f, 5f, false } };
            else if (gate && !up) spec = new[] { new object[] { "DeltaButton_Bracket_01", 0f, 1f, 0.04f * w, 16f, 52f, 27f, false }, new object[] { "DeltaButton_Bracket_02", 1f, 1f, -0.04f * w, 16f, 52f, 27f, false }, new object[] { "DeltaButton_Bracket_03", 0.5f, 0f, 0f, -8f, 62f, 46f, false } };
            else if (gate) spec = new[] { new object[] { "DeltaButton_Bracket_03", 0.5f, 1f, 0f, 8f, 62f, 46f, true }, new object[] { "DeltaButton_Bracket_01", 0f, 0f, 0.0f, -16f, 52f, 27f, true }, new object[] { "DeltaButton_Bracket_02", 1f, 0f, 0f, -16f, 52f, 27f, true } };
            for (int i = 0; i < 5; i++)
            {
                var t = frame.Find("Br" + i); if (t == null) continue;
                if (spec == null || i >= spec.Length || ((string)spec[i][0] != "corner" && Spr((string)spec[i][0]) == null)) { t.gameObject.SetActive(false); continue; }
                var o = spec[i]; var r = t.GetComponent<RectTransform>(); var im = t.GetComponent<Image>();
                t.gameObject.SetActive(true);
                var an = new Vector2((float)o[1], (float)o[2]); r.anchorMin = an; r.anchorMax = an; r.pivot = an;
                r.anchoredPosition = new Vector2((float)o[3], (float)o[4]); r.sizeDelta = new Vector2((float)o[5], (float)o[6]);
                r.localScale = new Vector3(o.Length > 8 && (bool)o[8] ? -1f : 1f, (bool)o[7] ? -1f : 1f, 1f);
                if ((string)o[0] == "corner") { im.sprite = CornerSprite(); im.color = col.r < 0.5f ? new Color(col.r, col.g, col.b, 1f) : new Color(0.95f, 0.2f, 0.18f, 1f); }      // red for player 1, blue for player 2
                else { im.sprite = Spr((string)o[0]); im.color = new Color(col.r < 0.5f || col.g < 0.9f ? col.r : 1f, col.g, col.b, 1f); }
            }
        }

        private static void AddEdge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color color)
        {
            var go = new GameObject(name);
            var r = go.AddComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = offMin; r.offsetMax = offMax;
            var img = go.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        // ---- the focus frame takes the shape of the icon (hand test: a white rectangle over a hex looked wrong) -----------------------
        // Hexes (biomes, sectors, classes) get the game's own hover frame and a soft glow in the shape of a hex, the gates their own hover triangle.
        // Everything else keeps the thin rectangle. Twin `-lcboxfocus`.
        private static bool ShapeFor(string key, out string sprite, out float dx, out float dy, out float growW, out float growH, out bool flip, out string glow)
        {
            sprite = null; dx = dy = growW = growH = 0f; flip = false; glow = "Hex_fill_blured";
            if (Lc.Has("-lcboxfocus") || string.IsNullOrEmpty(key)) return false;
            if (key.StartsWith("bio")) { glow = "HexButton_C_Frame-Hover"; sprite = "HexButton_C_Frame-Hover"; dx = dy = -8f; growW = growH = 16f; return true; }
            if (key.StartsWith("sec")) { glow = "HexButton_C_Frame-Hover"; sprite = "HexButton_C_Frame-Hover"; dx = dy = -8f; growW = growH = 16f; return true; }
            if (key.Length == 4 && key[0] == 'P' && key[2] == 'c' && char.IsDigit(key[3])) { glow = "HexButton_C_Frame-Hover"; sprite = "HexButton_C_Frame-Hover"; dx = dy = -6f; growW = 12f; growH = -38f; return true; }
            if (key.StartsWith("gat"))
            {
                int i; int.TryParse(key.Substring(3), out i);
                sprite = "DeltaButton_Gate_Hover_0" + (i % 2 == 1 && !Lc.Has("-lcgateflip") ? "2" : "1"); flip = i % 2 == 1 && Lc.Has("-lcgateflip"); glow = sprite;      // hand test: the turned copy of the down gate frame looked wrong on gates 2 and 4; the game has a frame of its own for them (twin -lcgateflip)
                 dx = dy = -4f; growW = growH = 8f; return true;      // the gate's own hover frame (hand test: a tinted big glow looked bad)
            }
            return false;
        }

        internal static void PlaceFrame(RectTransform frame, Component target, string key = null, Color? tint = null)
        {
            var trt = target.GetComponent<RectTransform>();
            var c = RefCenter(target);
            string sp, glowName; float dx, dy, gw, gh; bool flip;
            var shape = frame.Find("Shape"); var glow = frame.Find("Glow");
            bool shaped = ShapeFor(key, out sp, out dx, out dy, out gw, out gh, out flip, out glowName) && (sp == "" || Spr(sp) != null) && shape != null && glow != null;
            foreach (var e in new[] { "Top", "Bottom", "Left", "Right" }) { var et = frame.Find(e); if (et != null) et.gameObject.SetActive(!shaped); }
            if (shape != null) shape.gameObject.SetActive(shaped && sp != "");
            if (glow != null) glow.gameObject.SetActive(shaped);
            if (shaped)
            {
                var si = shape.GetComponent<Image>(); var gi = glow.GetComponent<Image>();
                if (si == null || gi == null) { Lc.LineOnce("ui: focus frame without its images"); return; }
                si.sprite = sp != "" ? Spr(sp) : null; gi.sprite = Spr(glowName) ?? Spr("Hex_fill_blured");
                var topT = frame.Find("Top"); var topI = topT != null ? topT.GetComponent<Image>() : null; var col = topI != null ? topI.color : Color.white;
                si.color = new Color(col.r, col.g, col.b, key != null && key.StartsWith("gat") ? 0.4f : 1f); var gc = Color.Lerp(col, new Color(1f, 0.82f, 0.35f, 1f), col.r > 0.9f && col.g > 0.9f ? 0.7f : 0.2f); gi.color = new Color(gc.r, gc.g, gc.b, sp != "" ? 0.26f : 0.5f);
                shape.localEulerAngles = new Vector3(0f, 0f, flip ? 180f : 0f);
                float w = trt.rect.width + gw, h = trt.rect.height + gh;
                frame.sizeDelta = new Vector2(w, h);
                frame.anchoredPosition = new Vector2(c.x - trt.rect.width * 0.5f + dx, c.y + trt.rect.height * 0.5f - dy);
                var gr = glow.GetComponent<RectTransform>(); float gk = sp != "" ? 0.025f : 0.2f; gr.offsetMin = new Vector2(-w * gk, -h * gk); gr.offsetMax = new Vector2(w * gk, h * gk);
                SetBrackets(frame, key, w, h, col);
                frame.SetAsLastSibling();
                return;
            }
            SetBrackets(frame, null, 0f, 0f, Color.white);
            const float pad = 3f;      // the border sits just outside the button
            frame.sizeDelta = new Vector2(trt.rect.width + 2f * pad, trt.rect.height + 2f * pad);
            frame.anchoredPosition = new Vector2(c.x - trt.rect.width * 0.5f - pad, c.y + trt.rect.height * 0.5f + pad);
            frame.SetAsLastSibling();
        }

        // ---- widget helpers ------------------------------------------------------------------------------------
        private static RectTransform Rect(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align, bool bold)
        {
            var go = new GameObject("Label");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = Loc.T(text); t.fontSize = size; t.color = color; t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            return t;
        }

        private static Button Btn(RectTransform parent, string key, string text, float x, float y, float w, float h, Color color, float size, Action onClick, Color? textColor = null)
        {
            var r = Rect(parent, "Btn_" + key, x, y, w, h, color);
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = r.GetComponent<Image>();
            var cb = btn.colors;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(new Action(() =>
            {
                ClickCount++;
                if (Lc.Has("-lcclicklog")) Lc.Line("ui: click arrived at '" + key + "' interactable=" + btn.interactable);
                if (!btn.interactable) return;      // locked: no click gets through, however it arrives
                try { onClick(); } catch (Exception e) { Lc.Line("ui: click '" + key + "' threw " + e.GetType().Name + ": " + e.Message); }
            }));
            Buttons[key] = btn;
            r.gameObject.AddComponent<CanvasGroup>();
            string hint = PadHint(key);
            float lead = hint != null && hint != "A" && hint != "X" && !Lc.Has("-lcnopadicons") ? Mathf.Min(h * 0.58f, 46f) + 18f : 0f;
            if (!string.IsNullOrEmpty(text)) Label(r, text, lead, 0, w - lead, h, size, textColor ?? Text, TextAlignmentOptions.Midline, true);
            if (lead > 0f) PadIcon(r, hint, 12f, (h - (lead - 18f)) * 0.5f, lead - 18f);
            else if ((hint == "A" || hint == "X") && !Lc.Has("-lcnopadicons")) { float s = Mathf.Min(h * 0.58f, 46f); PadIcon(r, hint, 12f, (h - s) * 0.5f, s); }
            Mark(r.gameObject, key, Gold, 4f);
            return btn;
        }

        // ---- controller hints: BACK is B, every "go on" button is the menu (start) button ---------------------------------------------
        /// <summary>"B" for the back buttons, "MENU" for the buttons that go on (the pad presses them with the menu button), else null.</summary>
        internal static string PadHint(string key)
        {
            if (key == "back" || key.EndsWith("back")) return "B";
            if (key == "revive" && !Lc.Has("-lcrevivebutton")) return "Y";      // hand test: the revive switch is a Y prompt, not a button (twin `-lcrevivebutton`)
            if (key.EndsWith("edit") && !Lc.Has("-lcnoeditpad")) return Lc.Has("-lceditona") ? "A" : "X";      // hand test: CHANGE on A jumped back into the class choice by accident, it is X now (twin `-lceditona`)
            //      // with two pads each presses CHANGE with A on its own side (the icon shows only then, see PadHidden)
            if (key == "start") return "MENU";
            if (key == "confirm" || key == "bnext" || key == "P2ready" || key.EndsWith("next") || key.EndsWith("ok")) return Lc.Has("-lcstartgoeson") ? "MENU" : "A";      // hand test: go on with A, the menu button only starts the run
            return null;
        }

        /// <summary>The buttons a controller does not focus: they are pressed with B (back) or the menu button (go on). The mouse still clicks them.</summary>
        internal static bool PadHidden(string key) { if (Lc.Has("-lcpadfocusall")) return false; if (key.EndsWith("edit")) return PadHint(key) != null && MenuPad.Split; return PadHint(key) != null; }

        private static Sprite _circle;
        private static Sprite Circle()
        {
            if (_circle != null) return _circle;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(c + 0.5f - d)));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        /// <summary>Hand test: the focus brackets of a mission card are children of the card itself, so they follow its zoom (red for player 1, blue for player 2).</summary>
        internal static void CardBrackets(Transform card, bool on, bool p2)
        {
            for (int i = 0; i < 4; i++)
            {
                var t = card.Find("CardBr" + i);
                if (t == null)
                {
                    if (!on) continue;
                    var go = new GameObject("CardBr" + i); var r = go.AddComponent<RectTransform>(); r.SetParent(card, false);
                    // anchored at the card's corner, centred on its own middle so the mirroring does not move it: it reaches inward, 8 units over the edge
                    float sx = i % 2 == 0 ? 1f : -1f, sy = i < 2 ? -1f : 1f, bw = 34f, bh = 24f, ov = 8f;
                    var an = new Vector2(i % 2, i < 2 ? 1f : 0f); r.anchorMin = an; r.anchorMax = an; r.pivot = new Vector2(0.5f, 0.5f);
                    r.sizeDelta = new Vector2(bw, bh); r.anchoredPosition = new Vector2(sx * (bw * 0.5f - ov), sy * (bh * 0.5f - ov));
                    r.localScale = new Vector3(sx, -sy, 1f);
                    var im = go.AddComponent<Image>(); im.raycastTarget = false; im.sprite = CornerSprite();
                    t = go.transform;
                }
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
                var img = t.GetComponent<Image>(); if (img != null && on) img.color = p2 ? new Color(BlueP2.r, BlueP2.g, BlueP2.b, 1f) : new Color(0.95f, 0.2f, 0.18f, 1f);
            }
        }

        private static Sprite _corner;
        /// <summary>A white top-left right-angle bracket (the mission cards are rectangles), drawn here so it can be tinted red or blue.</summary>
        internal static Sprite CornerSprite()
        {
            if (_corner != null) return _corner;
            const int w = 56, h = 40;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Vector2 a = new Vector2(3f, 4f), b = new Vector2(3f, 36f), c = new Vector2(54f, 36f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x, y);
                    float d = Mathf.Min(SegDist(p, a, b), SegDist(p, b, c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(3.2f - d)));
                }
            tex.Apply(); tex.filterMode = FilterMode.Bilinear;
            _corner = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _corner;
        }

        private static Sprite _tri;
        /// <summary>A white triangle pointing down (the gate's lamp).</summary>
        internal static Sprite TriSprite()
        {
            if (_tri != null) return _tri;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float t = Mathf.Clamp01((y - 3f) / 56f), hw = 29f * t, a0 = Mathf.Clamp01(hw + 0.5f - Mathf.Abs(x - 31.5f)) * Mathf.Clamp01(y - 2f) * Mathf.Clamp01(60f - y);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a0));
                }
            tex.Apply(); tex.filterMode = FilterMode.Bilinear;
            _tri = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _tri;
        }

        private static Sprite _check;
        /// <summary>A white check mark (two thick strokes), for "ready".</summary>
        internal static Sprite CheckSprite()
        {
            if (_check != null) return _check;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            Vector2 a = new Vector2(12f, 34f), b = new Vector2(26f, 18f), c = new Vector2(53f, 49f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x, y);
                    float d = Mathf.Min(SegDist(p, a, b), SegDist(p, b, c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(5.5f - d)));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _check = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _check;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>A round controller symbol: red "B", or the grey menu button with its three bars.</summary>
        private static void PadIcon(RectTransform parent, string kind, float x, float y, float s)
        {
            var disc = Rect(parent, "PadIcon_" + kind, x, y, s, s, kind == "B" ? new Color(0.86f, 0.18f, 0.20f, 1f) : kind == "A" ? new Color(0.30f, 0.72f, 0.32f, 1f) : kind == "X" ? new Color(0.22f, 0.46f, 0.92f, 1f) : kind == "Y" ? new Color(0.95f, 0.78f, 0.14f, 1f) : new Color(0.80f, 0.81f, 0.84f, 1f));
            var img = disc.GetComponent<Image>(); img.sprite = Circle(); img.raycastTarget = false;
            if (kind == "B" || kind == "A" || kind == "X" || kind == "Y") { Label(disc, kind, 0, 0, s, s, s * 0.66f, kind == "Y" ? new Color(0.25f, 0.18f, 0f) : Color.white, TextAlignmentOptions.Midline, true); return; }
            for (int i = 0; i < 3; i++)
            {
                var bar = Rect(disc, "Bar" + i, s * 0.24f, s * (0.31f + 0.15f * i), s * 0.52f, s * 0.075f, new Color(0.12f, 0.13f, 0.16f, 1f));
                bar.GetComponent<Image>().raycastTarget = false;
            }
        }

        private static void Mark(GameObject go, string key, Color color, float dist, bool on = false)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor = color; o.effectDistance = new Vector2(dist, -dist);
            o.enabled = on;
            Marks[key] = o;
        }
    }
}
