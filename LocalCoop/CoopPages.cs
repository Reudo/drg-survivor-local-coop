using System;
using System.Collections.Generic;
using System.Text;
using Assets.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>
    /// The mission pages of the co-op menu, drawn with the game's own sprites (names found by `-lcui=statprobe`): the lobby with the two players and the
    /// two mission types, the sector strip with its gates, and the biome page (hexes, goals, danger). Also the stat lines of classes and subclasses.
    /// </summary>
    internal static partial class CoopMenu
    {
        private static Dictionary<string, Sprite> _spr;
        private static readonly Dictionary<string, Image> Frames = new Dictionary<string, Image>();
        private static readonly Dictionary<string, Image> Arts = new Dictionary<string, Image>();
        private static readonly Dictionary<string, GameObject> Locks = new Dictionary<string, GameObject>();

        internal static Sprite Spr(string name)
        {
            if (_spr == null)
            {
                _spr = new Dictionary<string, Sprite>();
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>()) if (s != null && !_spr.ContainsKey(s.name)) _spr[s.name] = s;
                if (Lc.Has("-lcspritedump")) Lc.Line("sprites: " + string.Join(" ", System.Linq.Enumerable.OrderBy(_spr.Keys, k => k)));
            }
            Sprite r; return _spr.TryGetValue(name, out r) ? r : null;
        }

        private static Sprite SprFind(string prefix, params string[] parts)
        {
            Spr("");
            foreach (var kv in _spr)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var p in parts) if (kv.Key.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            }
            return null;
        }

        private static Image Art(RectTransform parent, string name, float x, float y, float w, float h, Sprite sp, Color c)
        {
            var r = Rect(parent, name, x, y, w, h, c);
            var img = r.GetComponent<Image>();
            img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
            if (sp == null) img.color = new Color(c.r, c.g, c.b, 0f);
            return img;
        }

        private static readonly Color Clear0 = new Color(0, 0, 0, 0);

        /// <summary>The sprite the game's own button shows (child image by name), e.g. the mission type cards of its wizard.</summary>
        private static Sprite GameSprite(string button, string child)
        {
            try
            {
                foreach (var b in Resources.FindObjectsOfTypeAll<DRS.UI.UIButton>())
                {
                    if (b.gameObject.name != button) continue;
                    foreach (var im in b.GetComponentsInChildren<Image>(true)) if (im.gameObject.name == child && im.sprite != null) return im.sprite;
                }
            }
            catch (Exception e) { Lc.LineOnce("ui: game sprite " + button + " failed: " + e.GetType().Name); }
            return null;
        }

        /// <summary>A biome that needs a DLC (the owner does not own it, and the game's own menu does not list it): not offered.</summary>
        internal static bool DlcBiome(BiomeLevelData b)
        {
            try
            {
                var pi = b.GetType().GetProperty("RequiresDLC");
                if (pi != null && pi.GetValue(b, null) != null) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Art fitted into a box without distortion, centered.</summary>
        private static Image FitArt(RectTransform parent, string name, float bx, float by, float bw, float bh, Sprite sp)
        {
            float w = bw, h = bh;
            if (sp != null && sp.rect.height > 0f)
            {
                float asp = sp.rect.width / sp.rect.height;
                if (bw / bh > asp) w = bh * asp; else h = bw / asp;
            }
            return Art(parent, name, bx + (bw - w) * 0.5f, by + (bh - h) * 0.5f, w, h, sp, Color.white);
        }

        // ---- stats ---------------------------------------------------------------------------------------------
        private static string StatLabel(string t)
        {
            switch (t)
            {
                case "MAX_HP": return "Max. health";
                case "DODGE": return "Dodge chance";
                case "CRIT_CHANCE": return "Crit chance";
                case "CRIT_DAMAGE": return "Crit damage";
                case "MOVESPEED": return "Move speed";
                case "LIFE_REGEN": return "Health regeneration";
                case "PICKUP_RADIUS": return "Pickup radius";
                case "XP_GAIN": return "XP gain";
                case "MINING_SPEED": return "Mining speed";
                default: return Humanize(t.ToLowerInvariant());
            }
        }

        private static string Pct(float v) { return Mathf.RoundToInt(v * 100f).ToString() + "%"; }

        /// <summary>A class's base stats as the game shows them: max health, dodge, crit chance, crit damage.</summary>
        internal static string ClassStats(ClassData c)
        {
            var sb = new StringBuilder();
            try
            {
                foreach (var m in c.BaseStats.mods)
                {
                    string t = m.statType.ToString();
                    if (t == "MAX_HP") sb.Append(StatLabel(t)).Append(": ").Append(Mathf.RoundToInt(m.value)).Append('\n');
                    else if (t == "DODGE" || t == "CRIT_CHANCE" || t == "CRIT_DAMAGE") sb.Append(StatLabel(t)).Append(": ").Append(Pct(m.value)).Append('\n');
                }
            }
            catch (Exception e) { Lc.LineOnce("ui: class stats failed: " + e.GetType().Name + ": " + e.Message); }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>A subclass's bonuses, e.g. "+10% Move speed".</summary>
        internal static string SubStats(ClassArtifactData a)
        {
            var sb = new StringBuilder();
            try
            {
                if (a != null && a.StatMods != null)
                    foreach (var m in a.StatMods)
                    {
                        string t = m.StatType.ToString();
                        float v = m.value;
                        string num = Mathf.Abs(v) < 1f || t == "DODGE" || t == "CRIT_CHANCE" ? Pct(v) : Mathf.RoundToInt(v).ToString();
                        sb.Append(v >= 0 ? "+" : "").Append(num).Append(' ').Append(StatLabel(t)).Append('\n');
                    }
            }
            catch (Exception e) { Lc.LineOnce("ui: sub stats failed: " + e.GetType().Name + ": " + e.Message); }
            return sb.ToString().TrimEnd('\n');
        }

        // ---- lobby: the players and the mission type -------------------------------------------------------------
        private static void BuildLobby(RectTransform parent)
        {
            var p = Rect(parent, "Lobby", 34, 185, 520, 330, Panel);
            Label(p, "PLAYERS", 24, 14, 400, 44, 32, Gold, TextAlignmentOptions.MidlineLeft, true);
            string[] tags = { "P1", "P2" };
            Color[] cols = { OrangeP1, BlueP2 };
            for (int i = 0; i < 2; i++)
            {
                var row = Rect(p, "Row" + tags[i], 24, 70 + i * 110, 472, 96, Idle);
                var badge = Rect(row, "Badge", 0, 0, 90, 96, cols[i]);
                Label(badge, tags[i], 0, 0, 90, 96, 40, new Color(0.08f, 0.05f, 0f), TextAlignmentOptions.Midline, true);
                Label(row, i == 0 ? "Player 1 (host)" : "Player 2", 110, 8, 350, 44, 30, Text, TextAlignmentOptions.MidlineLeft, true);
                Values["lobby" + tags[i]] = Label(row, "", 110, 54, 350, 36, 22, new Color(0.6f, 0.78f, 0.62f), TextAlignmentOptions.MidlineLeft, false);
            }
            Label(parent, "Choose the mission type. The host sets up the mission, then both players pick their dwarf at the same time.", 34, 530, 520, 130, 22, new Color(0.6f, 0.68f, 0.8f), TextAlignmentOptions.TopLeft, false).gameObject.name = "LobbyHint";

            var cards = Rect(parent, "TypeCards", 700, 185, 900, 780, Clear0);
            cards.GetComponent<Image>().raycastTarget = false;
            string[] names = { "ELIMINATION", "ESCORT" };
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                var b = Btn(cards, "type" + i, "", i * 470, 0, 430, 780, Idle, 30, () => { MissionType = k; GateSel = false; ShowMode("sector"); });
                var sp = GameSprite(i == 0 ? "UIMissionRoadButton_Elim" : "UIMissionRoadButton_Escort", "Illustration") ?? Spr("Btn_Illu_Elimination");
                FitArt(b.transform as RectTransform, "Art", 0, 0, 430, 690, sp);
                Label(b.transform as RectTransform, names[i], 0, 620, 430, 70, 48, Text, TextAlignmentOptions.Midline, true);
                Values["typepts" + i] = Label(b.transform as RectTransform, "", 0, 712, 430, 56, 34, Gold, TextAlignmentOptions.Midline, true);
                { var zg = Art(cards, "ZGlow" + i, i * 470 - 70, -70, 570, 920, Spr("Glow_Round"), new Color(1f, 0.8f, 0.3f, 0.30f)); zg.raycastTarget = false; zg.transform.SetAsFirstSibling(); zg.gameObject.SetActive(false); }
                Locks["type" + i] = Art(b.transform as RectTransform, "Lock", 155, 250, 120, 120, Spr("Icon_Padlock-Large"), Color.white).gameObject;
            }
        }

        private static void RefreshLobby()
        {
            int pads = 0;
            try { pads = UnityEngine.InputSystem.Gamepad.all.Count; } catch { }
            bool remote = VirtualPad.Remote;
            bool two = remote ? pads >= 1 : pads >= 2;
            Values["lobbyP1"].text = remote ? (pads >= 2 ? "Controller 1 connected" : "Keyboard and mouse") : (pads >= 1 ? "Controller 1 connected" : "Keyboard and mouse");
            Values["lobbyP2"].text = two ? (remote ? (pads >= 2 ? "Controller 2 connected (last connected pad)" : "Remote Play controller connected") : "Controller 2 connected") : (remote ? "WAITING FOR THE REMOTE PLAY CONTROLLER" : "NO SECOND CONTROLLER FOUND");
            Values["lobbyP2"].color = two ? new Color(0.6f, 0.78f, 0.62f) : new Color(1f, 0.55f, 0.3f);
            for (int i = 0; i < 2; i++)
            {
                bool open = SectorOpen(i, 0);
                SetOpen("type" + i, open);
                Values["typepts" + i].text = Points(i) + " POINTS";
                if (Locks.ContainsKey("type" + i)) Locks["type" + i].SetActive(!open);
            }
        }

        // ---- sector strip with its gates ----------------------------------------------------------------------------
        private static void BuildSectorPage(RectTransform parent)
        {
            if (Lc.Has("-lcspritelist")) { Spr(""); foreach (var kv in _spr) if (kv.Key.Contains("Gate") || kv.Key.Contains("Delta") || kv.Key.Contains("Hex")) Lc.Line("sprite: " + kv.Key); }
            var page = Rect(parent, "SectorPage", 0, 150, 1920, 800, Clear0);
            page.GetComponent<Image>().raycastTarget = false;
            // the short links between the hexes and the gates (drawn first, so the buttons cover their ends): orange while open, grey while locked
            for (int i = 0; i < 4; i++)
            {
                float sx = 340 + 310 * i, sy = 247, gx = 495 + 310 * i, gy = i % 2 == 0 ? 187 : 307, nx = 340 + 310 * (i + 1);
                for (int part = 0; part < 2; part++)
                {
                    float ax = part == 0 ? sx : gx, ay = part == 0 ? sy : gy, bx = part == 0 ? gx : nx, by = part == 0 ? gy : sy;
                    float mx = (ax + bx) * 0.5f, my = (ay + by) * 0.5f, ang = Mathf.Atan2(by - ay, bx - ax) * 57.29578f;
                    var l = Rect(page, "Link" + i + "_" + part, mx - 45f, my - 5f, 90f, 10f, new Color(1f, 0.58f, 0.10f, 1f));
                    l.GetComponent<Image>().raycastTarget = false;
                    l.pivot = new Vector2(0.5f, 0.5f); l.anchoredPosition = new Vector2(mx, -my); l.localEulerAngles = new Vector3(0f, 0f, -ang);
                    Arts["link" + i + "_" + part] = l.GetComponent<Image>();
                }
            }
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                float x = 220 + 310 * i;          // the game's own sector screen: five hexes in a compact strip, the gates between them
                var b = Btn(page, "sec" + i, "", x, 139, 240, 216, Clear0, 30, () => { Sector = k; GateSel = false; ShowMode("biome"); });
                Arts["sec" + i] = Art(b.transform as RectTransform, "Art", 0, 0, 240, 216, Spr("HexButton_Sector_Default"), Color.white);
                Label(b.transform as RectTransform, "SECTOR", 0, 40, 240, 40, 30, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
                Values["secnum" + i] = Label(b.transform as RectTransform, "0" + (i + 1), 0, 70, 240, 100, 84, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
                Values["sectext" + i] = Label(b.transform as RectTransform, "", 0, 170, 240, 34, 22, new Color(0.35f, 0.35f, 0.35f), TextAlignmentOptions.Midline, true);
                Frames["sec" + i] = Art(b.transform as RectTransform, "Frame", -8, -8, 256, 232, Spr("HexButton_C_Frame-Hover"), Gold);
            }
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                float x = 410 + 310 * i;
                float y = i % 2 == 0 ? 80 : 200;
                var b = Btn(page, "gat" + i, "", x, y, 170, 214, Clear0, 30, () => { Sector = k; GateSel = true; ShowMode("biome"); });
                var art = Art(b.transform as RectTransform, "Art", 0, 0, 170, 214, Spr("DeltaButton_Gate_Default_01"), Color.white);
                if (i % 2 == 1) { art.rectTransform.pivot = new Vector2(0.5f, 0.5f); art.rectTransform.anchoredPosition = new Vector2(85f, -107f); art.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f); }
                Arts["gat" + i] = art;
                bool tri = !Lc.Has("-lcoldgatelamp"); Image glow;
                if (tri)
                {
                    // hand test: only the small dark triangle on the red gate may turn into a blue light: a crisp blue triangle over it with a soft halo behind
                    var halo = Art(b.transform as RectTransform, "Halo", 50, i % 2 == 0 ? 106 : 26, 70, 70, Spr("Glow_Round"), new Color(0.55f, 0.85f, 1f, 0.4f)); halo.raycastTarget = false; halo.enabled = false; Arts["gatglowb" + i] = halo;
                    glow = Art(b.transform as RectTransform, "Glow", 66, i % 2 == 0 ? 124 : 47, 38, 32, CoopMenu.TriSprite(), new Color(0.78f, 0.95f, 1f, 1f));
                    if (i % 2 == 1) { glow.rectTransform.pivot = new Vector2(0.5f, 0.5f); glow.rectTransform.anchoredPosition = new Vector2(85f, -63f); glow.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f); }
                }
                else glow = Art(b.transform as RectTransform, "Glow", 58, i % 2 == 0 ? 120 : 34, 54, 54, Spr("Glow_Round") ?? Spr("ButtonDelta_Light-Glow"), new Color(0.45f, 0.82f, 1f, 0.5f));
                glow.raycastTarget = false; glow.enabled = false; Arts["gatglow" + i] = glow;       // the blue light of a beaten gate: a small lamp on the gate's arrow (hand test: the whole gate in blue was too much)
                Label(b.transform as RectTransform, "GATE", 0, i % 2 == 0 ? 34 : 150, 170, 34, 26, Color.white, TextAlignmentOptions.Midline, true);
                Label(b.transform as RectTransform, (i + 1).ToString(), 0, i % 2 == 0 ? 60 : 84, 170, 90, 70, Color.white, TextAlignmentOptions.Midline, true);
                Values["gattext" + i] = Label(b.transform as RectTransform, "", 0, i % 2 == 0 ? 160 : 30, 170, 30, 20, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.Midline, true);
                Frames["gat" + i] = Art(b.transform as RectTransform, "Frame", -6, -6, 182, 226, Spr("HexButton_C_Frame-Hover"), Gold);
            }
            // the info box below the strip (as on the game's sector screen): which hex or gate is focused and how many goals are left there
            var box = Rect(page, "SectorInfo", 660, 480, 600, 280, new Color(0.04f, 0.04f, 0.07f, 0.92f));
            box.GetComponent<Image>().raycastTarget = false;
            var bar = Rect(box, "Bar", 0, 0, 600, 40, Gold); bar.GetComponent<Image>().raycastTarget = false;
            Values["sinfotitle"] = Label(bar, "", 0, 0, 600, 40, 26, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
            Values["sinfobody"] = Label(box, "", 24, 60, 552, 200, 26, Text, TextAlignmentOptions.TopLeft, false);
        }

        private static string _sectorInfoKey = "";

        /// <summary>Test: the shown goal count for a focus key, next to the game's own counter. Returns "shown/game".</summary>
        internal static string SectorInfoProbe(string key)
        {
            if (!Values.ContainsKey("sinfobody")) return "-";
            SectorInfoTick(key);
            int idx; if (!int.TryParse(key.Substring(3), out idx)) return "-";
            bool gate = key.StartsWith("gat");
            int own = -1;
            try
            {
                if (gate) { var g0 = GateNode(MissionType, idx); if (g0 != null) own = g0.RemainingMissionGoals(); }
                else { var st0 = RoadState(MissionType); if (st0 != null && idx < st0.SectorStates.Count) own = st0.SectorStates[idx].RemainingMisisonGoals(); }
            }
            catch { }
            string t = Values["sinfobody"].text ?? "";
            int sp = t.LastIndexOf(' ');
            int shown = -1; if (sp >= 0) int.TryParse(t.Substring(sp + 1), out shown);
            return shown + "/" + own + "/" + RemainingGoalsMine(idx, gate);
        }

        /// <summary>Goals not finished yet in a sector (all offered biomes) or at its gate.</summary>
        private static int RemainingGoals(int sector, bool gate)
        {
            int n = 0;
            try
            {
                // hand test: the game's own counter also counts goals of a DLC the player does not own (6 shown, 3 playable), so the number
                // shown is the one counted over the biomes this menu offers; the game's value is only logged
                int own = -1;
                try
                {
                    if (gate) { var g0 = GateNode(MissionType, sector); if (g0 != null) own = g0.RemainingMissionGoals(); }
                    else { var st0 = RoadState(MissionType); if (st0 != null && sector < st0.SectorStates.Count) own = st0.SectorStates[sector].RemainingMisisonGoals(); }
                }
                catch { }
                int mine = RemainingGoalsMine(sector, gate);
                Lc.LineOnce("ui: sector info " + (gate ? "gate " : "sector ") + sector + " goals left: game=" + own + " mine=" + mine);
                if (Lc.Has("-lcgamesectorcount") && own >= 0) return own;        // twin: the game's counter (DLC goals included)
                return mine;
            }
            catch { }
            return n;
        }

        private static int RemainingGoalsMine(int sector, bool gate)
        {
            int n = 0;
            try
            {
                if (gate) { var g = GateNode(MissionType, sector); if (g != null) foreach (var s in g.GetMissionGoalStates()) if (!s.IsComplete) n++; return n; }
                for (int i = 0; i < Biomes.Length; i++)
                {
                    if (!Offered(i)) continue;
                    var node = BiomeNode(MissionType, sector, i);
                    if (node != null) foreach (var s in node.GetMissionGoalStates()) if (!s.IsComplete) n++;
                }
            }
            catch { }
            return n;
        }

        /// <summary>Called every frame from the pad code with the focused key ("sec2", "gat1"); the box follows it.</summary>
        internal static void SectorInfoTick(string focus)
        {
            if (Mode != "sector" || !Values.ContainsKey("sinfotitle")) return;
            string key = focus != null && (focus.StartsWith("sec") || focus.StartsWith("gat")) ? focus : (_sectorInfoKey.Length > 0 ? _sectorInfoKey : "sec0");
            if (key == _sectorInfoKey) return;
            _sectorInfoKey = key;
            int idx; if (!int.TryParse(key.Substring(3), out idx)) return;
            bool gate = key.StartsWith("gat");
            Values["sinfotitle"].text = Loc.T(gate ? "GATE" : "SECTOR") + " " + (gate ? (idx + 1).ToString() : "0" + (idx + 1));
            bool open = gate ? GateOpen(MissionType, idx) : SectorOpen(MissionType, idx);
            Values["sinfobody"].text = open ? Loc.T(gate ? "Remaining mission goals at this gate:" : "Remaining mission goals in this sector:") + " " + RemainingGoals(idx, gate) : Loc.T("Locked.");
        }

        private static void RefreshSector()
        {
            for (int i = 0; i < 5; i++)
            {
                bool open = SectorOpen(MissionType, i);
                SetOpenKeep("sec" + i, open);
                Arts["sec" + i].sprite = Spr(open ? "HexButton_Sector_Default" : "HexButton_Sector_NA") ?? Arts["sec" + i].sprite;
                Values["sectext" + i].text = open ? "" : (Lc.Has("-lcsectortext") && i > 0 ? "CLEAR GATE " + i : "LOCKED");      // hand test: "CLEAR GATE 3" ran out of the hex; the game's own screen says only "Locked" (twin `-lcsectortext`)
                Frames["sec" + i].enabled = false;        // the page is a menu, not a choice: a marker left from the last pick looked like a stray hex (at a gate it sat around the triangle)
            }
            for (int i = 0; i < 4; i++)
            {
                bool open = GateOpen(MissionType, i);
                SetOpenKeep("gat" + i, open);
                string spn = open ? "DeltaButton_Gate_Default_01" : "DeltaButton_Gate_NA_01";
                Arts["gat" + i].sprite = Spr(spn) ?? Arts["gat" + i].sprite;
                int c = GateCost(MissionType, i);
                Values["gattext" + i].text = !Lc.Has("-lcsectortext") ? "" : open ? "" : (c > 0 && SectorOpen(MissionType, i) ? c + " PTS" : "LOCKED");     // gates carry no text in the game; a blue light shows a beaten one
                if (Arts.ContainsKey("gatglow" + i)) { bool beaten = false; try { var g = GateNode(MissionType, i); beaten = g != null && g.IsCompleted(); } catch { } if (Lc.Has("-lcgatelamptest") && i < 2) beaten = true; Arts["gatglow" + i].enabled = beaten && !Lc.Has("-lcsectortext"); if (Arts.ContainsKey("gatglowb" + i)) Arts["gatglowb" + i].enabled = Arts["gatglow" + i].enabled; }
                Frames["gat" + i].enabled = false;
                var open1 = SectorOpen(MissionType, i); var open2 = open;
                var onC = new Color(1f, 0.58f, 0.10f, 1f); var offC = new Color(0.55f, 0.55f, 0.58f, 1f);
                if (Arts.ContainsKey("link" + i + "_0")) { Arts["link" + i + "_0"].color = open1 ? onC : offC; Arts["link" + i + "_1"].color = open2 ? onC : offC; }
            }
        }

        /// <summary>Open/locked for sprite buttons: locked ones cannot be clicked, the sprite itself shows the lock (no greying of the whole button).</summary>
        private static void SetOpenKeep(string key, bool open)
        {
            Button b;
            if (Buttons.TryGetValue(key, out b) && b != null) b.interactable = open;
        }

        // ---- biome page: the hexes and the main mission goals of the choice -------------------------------------------
        private static readonly string[] SlotKeys = { "crystal", "magma", "salt", "hollow", "azure" };
        private static readonly Vector2[] Slots = { new Vector2(40, 240), new Vector2(340, 100), new Vector2(640, 240), new Vector2(340, 380), new Vector2(640, 520) };
        private static readonly string[] IlluNames = { "HexButton_2025_Illu_Crystaline", "HexButton_2025_Illu_Magma", "HexButton_2025_Illu_Salt", "HexButton_2025_Illu_Hollow", "HexButton_2025_Illu_Azure" };

        private static int SlotOf(BiomeLevelData b)
        {
            string n = BiomeName(b).ToLowerInvariant();
            for (int i = 0; i < SlotKeys.Length; i++) if (n.StartsWith(SlotKeys[i])) return i;
            return -1;
        }

        private static bool Offered(int i) { return i >= 0 && i < Biomes.Length && !DlcBiome(Biomes[i]) && SlotOf(Biomes[i]) >= 0; }

        private static void BuildBiomePage(RectTransform parent)
        {
            var page = Rect(parent, "BiomePage", 0, 190, 1060, 800, Clear0);
            page.GetComponent<Image>().raycastTarget = false;
            for (int i = 0; i < Biomes.Length; i++)
            {
                Lc.LineOnce("ui: biome " + BiomeName(Biomes[i]) + (DlcBiome(Biomes[i]) ? " needs a DLC: not offered" : ""));
                if (!Offered(i)) continue;
                int k = i;
                int slot = SlotOf(Biomes[i]);
                var b = Btn(page, "bio" + i, "", Slots[slot].x, Slots[slot].y, 300, 272, Clear0, 30, () => { if (!GateSel || Lc.Has("-lcgatebug")) { BiomeIndex = k; GateSel = false; RefreshAll(); } });      // at a gate the biome is fixed: its hex only shows it
                var rt = b.transform as RectTransform;
                Arts["bio" + i] = Art(rt, "Art", 0, 0, 300, 272, Spr(IlluNames[slot]), Color.white);
                Art(rt, "Border", 0, 0, 300, 272, Spr("HexButton_2025_Border_Default"), new Color(1f, 0.72f, 0.12f, 0.9f));
                Frames["bio" + i] = Art(rt, "Frame", -8, -8, 316, 288, Spr("HexButton_C_Frame-Hover"), Color.white);
                Locks["bio" + i] = Art(rt, "Lock", 120, 80, 60, 60, Spr("Icon_Padlock-Large"), Color.white).gameObject;
                Values["bio" + i + "cost"] = Label(rt, "", 0, 215, 300, 40, 28, Gold, TextAlignmentOptions.Midline, true);
                Values["bio" + i + "name"] = Label(rt, BiomeName(Biomes[i]).ToUpperInvariant(), 0, 170, 300, 40, 24, Color.white, TextAlignmentOptions.Midline, true);
                // hand test: the game's own biome hexes show how many of their missions are done, in blue, right on the icon. A plain bar said nothing:
                // one of the game's own goal pips (`BiomeGoalPoint` / `BiomeGoalPointCompleted`) per goal of the node.
                for (int pk = 0; pk < 6; pk++) { var pip = Art(rt, "Pip" + pk, 0, 234, 30, 30, Spr("BiomeGoalPoint"), Color.white); pip.raycastTarget = false; pip.enabled = false; Arts["bio" + i + "pip" + pk] = pip; }
                Values["bio" + i + "progtext"] = Label(rt, "", 80, 212, 140, 24, 22, new Color(0.7f, 0.9f, 1f), TextAlignmentOptions.Midline, true); Values["bio" + i + "progtext"].gameObject.SetActive(false);
            }

            var info = Rect(parent, "BiomeInfo", 1090, 190, 800, 760, Panel);
            var bar = Rect(info, "Bar", 0, 0, 800, 60, Gold);
            Values["btitle"] = Label(bar, "", 0, 0, 800, 60, 34, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
            var lore = Label(info, "", 24, 68, 752, 112, 22, Text, TextAlignmentOptions.TopLeft, false); lore.overflowMode = TextOverflowModes.Ellipsis;
            Values["blore"] = lore;
            Label(info, "RICH DEPOSITS", 24, 184, 360, 52, 28, new Color(0.55f, 0.85f, 0.95f), TextAlignmentOptions.MidlineRight, true);
            for (int i = 0; i < 6; i++) { var ic = Art(info, "Dep" + i, 396 + i * 60, 184, 52, 52, null, Color.white); Arts["dep" + i] = ic; }
            Values["bdanger"] = Label(info, "", 24, 244, 752, 32, 24, new Color(0.55f, 0.85f, 0.95f), TextAlignmentOptions.MidlineLeft, true);
            Values["bstart"] = Label(info, "", 24, 276, 752, 32, 24, Gold, TextAlignmentOptions.MidlineLeft, true);
            Label(info, "MAIN MISSION GOALS", 24, 316, 750, 34, 24, Gold, TextAlignmentOptions.MidlineLeft, true);
            var t = Label(info, "", 24, 352, 752, 120, 24, Text, TextAlignmentOptions.TopLeft, false);
            t.overflowMode = TextOverflowModes.Ellipsis;
            Values["bgoals0"] = t; t.enabled = false;
            BuildGoalRows(info, "b", 24, 352, 752, 38, 41, 22, 3);
            for (int i = 0; i < 3; i++)
            {
                var row = Rect(info, "MutRow" + i, 24, 482 + i * 72, 752, 66, new Color(0.45f, 0.1f, 0.1f, 1f));
                Parts["mut" + i] = row.gameObject; Arts["mutbg" + i] = row.GetComponent<Image>();
                Values["mut" + i + "t"] = Label(row, "", 12, 4, 640, 30, 24, Text, TextAlignmentOptions.MidlineLeft, true);
                Values["mut" + i + "d"] = Label(row, "", 12, 34, 640, 30, 20, Text, TextAlignmentOptions.TopLeft, false);
                Arts["muticon" + i] = Art(row, "Icon", 690, 8, 50, 50, null, Color.white);
            }
            Values["bscore"] = Label(info, "", 24, 706, 752, 46, 30, Gold, TextAlignmentOptions.Midline, true);

            Btn(parent, "bnext", "CONTINUE", 1620, 1000, 270, 60, Gold, 28, () => { RefreshAll(); if (BiomeOpen(MissionType, Sector, BiomeIndex) || GateSel) ShowMode("danger"); }, new Color(0.1f, 0.07f, 0.0f));

            if (Lc.Has("-lcbracketdump")) { string[] bn = { "HexButton_Bracket_01", "HexButton_Bracket_02", "HexButton_Bracket_03", "HexButton_2025_Bracket_03", "HexButton_2025_Bracket_04", "DeltaButton_Bracket_01", "DeltaButton_Bracket_02", "DeltaButton_Bracket_03" }; for (int bi2 = 0; bi2 < bn.Length; bi2++) { var bk = Rect(parent, "BrDump" + bi2, 40 + bi2 * 230, 760, 220, 220, new Color(0.3f, 0.3f, 0.3f, 1f)); bk.GetComponent<Image>().raycastTarget = false; var sp0 = Spr(bn[bi2]); var im2 = Art(bk, "Img", 0, 0, 220, 220, sp0, Color.white); if (sp0 != null) { Lc.Line("bracket " + bn[bi2] + " size " + sp0.rect.width + "x" + sp0.rect.height); } } }
            // hand test: the A-continue box sat only on the biome page; the first page and the sector page get it too (a plain box, A presses the focused card). Twin `-lcnoahint`.
            { var ah = Rect(parent, "AHint", 1620, 1000, 270, 60, Gold); ah.GetComponent<Image>().raycastTarget = false; Label(ah, "CONTINUE", 66, 0, 196, 60, 28, new Color(0.1f, 0.07f, 0.0f), TextAlignmentOptions.Midline, true); PadIcon(ah, "A", 12, 8, 44); }
            // danger page: five levels on the left, the goals of the chosen level on the right
            var dg = Rect(parent, "DangerPage", 34, 190, 760, 760, Clear0);
            dg.GetComponent<Image>().raycastTarget = false;
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                var b = Btn(dg, "ddng" + i, "", 0, i * 150, 740, 130, Idle, 30, () => { Danger = k; RefreshAll(); });
                Label(b.transform as RectTransform, "DANGER " + (i + 1), 30, 0, 450, 130, 44, Text, TextAlignmentOptions.MidlineLeft, true);
                Values["ddng" + i + "text"] = Label(b.transform as RectTransform, "", 400, 0, 320, 130, 26, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.MidlineRight, true);
            }
            var di = Rect(parent, "DangerInfo", 830, 190, 1060, 760, Panel);
            var dbar = Rect(di, "Bar", 0, 0, 1060, 60, Gold);
            Values["dtitle"] = Label(dbar, "", 0, 0, 1060, 60, 34, new Color(0.1f, 0.07f, 0f), TextAlignmentOptions.Midline, true);
            Label(di, "MISSION GOALS", 24, 80, 1000, 40, 26, Gold, TextAlignmentOptions.MidlineLeft, true);
            var g1 = Label(di, "", 24, 128, 1010, 330, 30, Text, TextAlignmentOptions.TopLeft, false); g1.overflowMode = TextOverflowModes.Ellipsis; Values["bgoals1"] = g1; g1.enabled = false;
            BuildGoalRows(di, "d", 24, 128, 1010, 60, 68, 28, 3);
            Label(di, "MUTATOR", 24, 480, 1000, 40, 26, Gold, TextAlignmentOptions.MidlineLeft, true);
            var g2 = Label(di, "", 24, 528, 1010, 210, 28, Text, TextAlignmentOptions.TopLeft, false); g2.overflowMode = TextOverflowModes.Ellipsis; Values["bgoals2"] = g2;
        }

        /// <summary>The last page: the mission of the run (main goals, biome goals, mutator) at the right edge, as on the game's own mission screen.</summary>
        private static void BuildFinalGoals(RectTransform parent)
        {
            var p = Rect(parent, "FinalGoals", 1500, 185, 390, 780, Panel);
            Label(p, "MAIN MISSION GOALS", 18, 10, 354, 34, 24, Gold, TextAlignmentOptions.MidlineLeft, true);
            BuildGoalRows(p, "f", 12, 52, 366, 96, 104, 20, 3);
            for (int i = 0; i < 3; i++) { var v = Values["grf" + i]; v.enableWordWrapping = true; v.overflowMode = TextOverflowModes.Ellipsis; }
            Label(p, "BIOME GOALS", 18, 372, 354, 34, 24, Gold, TextAlignmentOptions.MidlineLeft, true);
            var g1 = Label(p, "", 18, 410, 354, 170, 20, Text, TextAlignmentOptions.TopLeft, false); g1.overflowMode = TextOverflowModes.Ellipsis; Values["fgoals1"] = g1;
            Label(p, "MUTATOR", 18, 590, 354, 34, 24, Gold, TextAlignmentOptions.MidlineLeft, true);
            var g2 = Label(p, "", 18, 628, 354, 140, 20, Text, TextAlignmentOptions.TopLeft, false); g2.overflowMode = TextOverflowModes.Ellipsis; Values["fgoals2"] = g2;
        }

        // ---- mission goals as rows with a done / open icon ----------------------------------------------------------------
        private static void BuildGoalRows(RectTransform parent, string prefix, float x, float y, float w, float h, float step, float font, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var row = Rect(parent, "GoalRow" + prefix + i, x, y + i * step, w, h, new Color(0.13f, 0.17f, 0.26f, 1f));
                row.GetComponent<Image>().raycastTarget = false;
                Parts["gr" + prefix + i] = row.gameObject; Arts["grbg" + prefix + i] = row.GetComponent<Image>();
                Arts["gricon" + prefix + i] = Art(row, "Icon", 8, (h - h * 0.72f) * 0.5f, h * 0.72f, h * 0.72f, Spr("BiomeGoalPoint"), Color.white);
                Values["gr" + prefix + i] = Label(row, "", h * 0.72f + 20, 0, w - h * 0.72f - 30, h, font, Text, TextAlignmentOptions.MidlineLeft, true);
                Values["gr" + prefix + i].enableWordWrapping = false; Values["gr" + prefix + i].overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        /// <summary>The goals of a node, each in its row: a ring for an open goal, a checked ring and a green row for a finished one.</summary>
        internal static void FillGoalRows(string prefix, MissionNodeState node)
        {
            int n = 0;
            try
            {
                if (node != null)
                    foreach (var g in node.GetMissionGoalStates())
                    {
                        if (!Parts.ContainsKey("gr" + prefix + n)) break;
                        Parts["gr" + prefix + n].SetActive(true);
                        bool done = g.IsComplete;
                        Arts["grbg" + prefix + n].color = done ? new Color(0.12f, 0.3f, 0.17f, 1f) : new Color(0.13f, 0.17f, 0.26f, 1f);
                        Arts["gricon" + prefix + n].sprite = Spr(done ? "BiomeGoalPointCompleted" : "BiomeGoalPoint");
                        Values["gr" + prefix + n].text = Clean(g.Data.GetFormattedDescriptionText());
                        n++;
                    }
            }
            catch (Exception e) { Lc.LineOnce("ui: goal rows failed: " + e.GetType().Name + ": " + e.Message); }
            while (Parts.ContainsKey("gr" + prefix + n)) { Parts["gr" + prefix + n].SetActive(false); n++; }
        }

        private static Dictionary<string, Sprite> _craft;

        private static Sprite CraftIcon(string currency)
        {
            if (_craft == null)
            {
                _craft = new Dictionary<string, Sprite>();
                Spr("");
                foreach (var kv in _spr) if (kv.Key.StartsWith("Icons_Craft_", StringComparison.OrdinalIgnoreCase)) _craft[kv.Key.Substring(12).Replace("_", "").ToLowerInvariant()] = kv.Value;
            }
            Sprite r; return _craft.TryGetValue(currency.Replace("_", "").ToLowerInvariant(), out r) ? r : null;
        }

        /// <summary>The biome's description, rich deposits and high score, as the game's own biome page shows them.</summary>
        private static void RefreshBiomeInfo()
        {
            var bi = Biomes[BiomeIndex];
            try
            {
                var d = bi.BiomeData;
                if (d != null && !string.IsNullOrEmpty(d.DisplayName)) Values["btitle"].text = (GateSel ? "GATE " + (Sector + 1) + ": " : "") + d.DisplayName.ToUpperInvariant();
                Values["blore"].text = d != null ? Clean(d.Lore) : "";
                int n = 0;
                var lists = new[] { d != null ? d.PrimaryCraftingMaterials : null, d != null ? d.SecondaryCraftingMaterials : null };
                foreach (var arr in lists)
                    if (arr != null)
                        foreach (var cur in arr)
                            if (n < 6)
                            {
                                var sp = CraftIcon(cur.ToString());
                                Arts["dep" + n].sprite = sp;
                                Arts["dep" + n].color = sp != null ? Color.white : new Color(1, 1, 1, 0);
                                n++;
                            }
                for (; n < 6; n++) Arts["dep" + n].color = new Color(1, 1, 1, 0);
            }
            catch (Exception e) { Lc.LineOnce("ui: biome info failed: " + e.GetType().Name + ": " + e.Message); }
            try
            {
                var node = NodeOf(MissionType, Sector, BiomeIndex);
                Values["bdanger"].text = node != null && node.MinHazIndex > 0 ? "Danger " + (node.MinHazIndex + 1) + "+" : "";
                string start = "";
                if (GateSel && node != null && node.Data.FixedGateData != null && node.Data.FixedGateData.StarterArtifacts != null)
                {
                    var names = new List<string>();
                    foreach (var art in node.Data.FixedGateData.StarterArtifacts) names.Add(Clean(art.Title));
                    if (names.Count > 0) start = "Starts with: " + string.Join(", ", names.ToArray());
                }
                Values["bstart"].text = start;
                int r = 0;
                var muts = node != null ? Mutators(node) : null;
                if (muts != null)
                    foreach (var m in muts)
                    {
                        if (r >= 3) break;
                        Parts["mut" + r].SetActive(true);
                        Arts["mutbg" + r].color = m.Tier > 0 ? new Color(0.45f, 0.1f, 0.1f, 1f) : new Color(0.12f, 0.4f, 0.14f, 1f);
                        Values["mut" + r + "t"].text = Clean(m.GetTitle());
                        Values["mut" + r + "d"].text = Clean(m.GetDescription());
                        var ic = Arts["muticon" + r]; ic.sprite = m.Icon; ic.color = m.Icon != null ? Color.white : new Color(1, 1, 1, 0);
                        r++;
                    }
                for (; r < 3; r++) Parts["mut" + r].SetActive(false);
            }
            catch (Exception e) { Lc.LineOnce("ui: biome mutators failed: " + e.GetType().Name + ": " + e.Message); }
            try
            {
                Services();
                int sc = 0;
                bool has = _save != null && _save.TryGetBiomeScore((EMainMissionType)MissionType, bi, out sc);
                Values["bscore"].text = has ? "HIGH SCORE   " + sc.ToString("N0", new System.Globalization.CultureInfo("de-DE")) : "";
            }
            catch (Exception e) { Lc.LineOnce("ui: biome score failed: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static int ProgDone, ProgTotal;       // test: of the focused hex

        /// <summary>The blue "missions done" mark on a biome hex: a bar and "done/total" (the goals of that node). Twin `-lcnobiomeprogress`.</summary>
        private static void BiomeProgress(int i)
        {
            try
            {
                var n = BiomeNode(MissionType, Sector, i);
                int total = 0, done = 0;
                if (n != null) foreach (var s in n.GetMissionGoalStates()) { total++; if (s.IsComplete) done++; }
                bool on = total > 0 && !Lc.Has("-lcnobiomeprogress");
                Values["bio" + i + "progtext"].text = on ? done + "/" + total : "";
                for (int k = 0; k < 6; k++)
                {
                    var pip = Arts["bio" + i + "pip" + k]; bool shown = on && k < total && k < 6; pip.enabled = shown;
                    if (!shown) continue;
                    pip.sprite = Spr(k < done ? "BiomeGoalPointCompleted" : "BiomeGoalPoint") ?? pip.sprite;
                    pip.rectTransform.anchoredPosition = new Vector2(150f + (k - (total - 1) * 0.5f) * 34f - 15f, -234f);
                }
                if (!on) return;
                if (i == BiomeIndex) { ProgDone = done; ProgTotal = total; }
            }
            catch (Exception e) { Lc.LineOnce("ui: biome progress failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static void RefreshBiomePage()
        {
            string bn = BiomeName(Biomes[BiomeIndex]).ToUpperInvariant();
            Values["btitle"].text = GateSel ? "GATE " + (Sector + 1) + ": " + bn : bn;
            Values["dtitle"].text = bn + ", DANGER " + (Danger + 1);
            RefreshBiomeInfo();
            for (int i = 0; i < Biomes.Length; i++)
            {
                if (!Offered(i)) continue;
                bool has = BiomeNode(MissionType, Sector, i) != null;
                bool show = has && (!GateSel || i == BiomeIndex);
                Buttons["bio" + i].gameObject.SetActive(show);
                if (!show) continue;
                bool open = GateSel ? true : BiomeOpen(MissionType, Sector, i);
                SetOpenKeep("bio" + i, open);
                Arts["bio" + i].color = open ? Color.white : new Color(0.35f, 0.35f, 0.38f, 1f);
                Locks["bio" + i].SetActive(!open);
                int c = BiomeCost(MissionType, Sector, i);
                Values["bio" + i + "cost"].text = open ? "" : (c > 0 ? c + " POINTS" : "LOCKED");
                Frames["bio" + i].enabled = BiomeIndex == i;
                BiomeProgress(i);
            }
            for (int i = 0; i < 5; i++)
            {
                bool open = DangerOpen(MissionType, Sector, BiomeIndex, i);
                SetOpen("ddng" + i, open);
                SetMark("ddng" + i, Danger == i);
                Values["ddng" + i + "text"].text = open ? "" : "LOCKED";
            }
        }
    }
}
