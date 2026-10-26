using System;
using Steamworks;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Experiment: can the game itself start Steam Remote Play Together under its own AppID, without the Spacewar trick?
    /// The store page does not list the feature, but the game's Steamworks.NET has the public calls
    /// ISteamRemotePlay::BSendRemotePlayTogetherInvite and ISteamFriends::ActivateGameOverlayRemotePlayTogetherInviteDialog.
    /// Whether Steam accepts them for an app without the feature is unknown (no source found); this answers it.
    ///  -lcrpprobe         : logs Steam's state (running, logged on, AppID, overlay, sessions); RESULT rpprobe PASS when Steam answers
    ///                       for the game's own AppID. Sends nothing.
    ///  -lcnorpsteam       : twin, skips the Steam calls (RESULT must be FAIL)
    ///  -lcrpdialog        : opens Steam's Remote Play Together invite dialog in the overlay (the owner picks the friend); F8 opens it again
    ///  -lcrpinvite=<id64> : sends the invite to that friend directly and logs Steam's answer
    /// Remote Play sessions (count, device of the guest) are logged whenever they change. With -lcrpdialog / -lcrpinvite the game stays open.
    /// </summary>
    internal static class RpInvite
    {
        private const uint GameAppId = 2321470;     // DRG Survivor (steam_appid.txt of the test copy)
        private const uint SpacewarAppId = 480;
        private const float ProbeAt = 25f;          // seconds after launch: Steam is initialised by the game long before the main menu
        private static int _state = -1;              // -1 not decided, 0 off, 1 waiting, 2 probed
        private static float _pollAt;
        private static uint _sessions = uint.MaxValue;

        internal static void Tick()
        {
            if (_state == 0) return;
            if (_state < 0)
            {
                bool on = Lc.Has("-lcrpprobe") || Lc.Has("-lcrpdialog") || Lc.Str("-lcrpinvite=").Length > 0;
                _state = on ? 1 : 0;
                if (!on) return;
                Lc.Line("rp: experiment on, probe at " + ProbeAt + " s");
            }
            float now = Time.realtimeSinceStartup;
            if (_state == 1)
            {
                if (now < ProbeAt) return;
                _state = 2;
                bool ok = Probe();
                if (ok && Lc.Has("-lcrpdialog")) Dialog();
                string to = Lc.Str("-lcrpinvite=");
                if (ok && to.Length > 0) Invite(to);
                if (Lc.Has("-lcrpprobe") && !Lc.Has("-lcrpdialog") && to.Length == 0)
                {
                    Lc.QuitSent = true;
                    Lc.Line("RESULT rpprobe " + (ok ? "PASS" : "FAIL"));
                    Application.Quit();
                }
                return;
            }
            try { var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.f8Key.wasPressedThisFrame && Lc.Has("-lcrpdialog")) Dialog(); } catch { }
            if (now < _pollAt || Lc.Has("-lcnorpsteam")) return;
            _pollAt = now + 2f;
            Sessions();
            if (Lc.Has("-lcrpdialog")) InviteFile();
        }

        /// <summary>
        /// Without the overlay (game not started by Steam) the dialog cannot open. Then rp-friends.txt in the game folder lists the friends
        /// who are online, and a SteamID64 written into rp-invite.txt is invited directly (the file is deleted after reading).
        /// </summary>
        private static void InviteFile()
        {
            string dir = Lc.Dir;
            if (!_friendsWritten)
            {
                _friendsWritten = true;
                try
                {
                    var sb = new System.Text.StringBuilder("# friends online (SteamID64, state, name); write one SteamID64 into rp-invite.txt to invite\n");
                    int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                    for (int i = 0; i < n; i++)
                    {
                        var f = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                        var st = SteamFriends.GetFriendPersonaState(f);
                        if (st == EPersonaState.k_EPersonaStateOffline) continue;
                        sb.Append(f.m_SteamID).Append('\t').Append(st).Append('\t').Append(SteamFriends.GetFriendPersonaName(f)).Append('\n');
                    }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "rp-friends.txt"), sb.ToString());
                    Lc.Line("rp: rp-friends.txt written");
                }
                catch (Exception e) { Lc.Line("rp: friend list failed " + e.GetType().Name + ": " + e.Message); }
            }
            try
            {
                string p = System.IO.Path.Combine(dir, "rp-invite.txt");
                if (!System.IO.File.Exists(p)) return;
                string id = System.IO.File.ReadAllText(p).Trim();
                System.IO.File.Delete(p);
                Invite(id);
            }
            catch (Exception e) { Lc.LineOnce("rp: rp-invite.txt failed " + e.GetType().Name + ": " + e.Message); }
        }
        private static bool _friendsWritten;

        private static bool Probe()
        {
            if (Lc.Has("-lcnorpsteam")) { Lc.Line("rp: CHECK FAIL steam answers for the game's AppID (twin -lcnorpsteam: Steam not asked)"); return false; }
            try
            {
                bool running = SteamAPI.IsSteamRunning();
                bool logged = SteamUser.BLoggedOn();
                uint app = SteamUtils.GetAppID().m_AppId;
                bool overlay = SteamUtils.IsOverlayEnabled();
                uint sessions = SteamRemotePlay.GetSessionCount();
                int friends = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                Lc.Line("rp: steam running=" + running + " loggedOn=" + logged + " appId=" + app + " overlay=" + overlay + " sessions=" + sessions
                    + " friends=" + friends + " env SteamAppId=" + (Environment.GetEnvironmentVariable("SteamAppId") ?? "-"));
                // Hand test: under 2321470 Steam refuses the invite (returns false, nothing arrives); under Spacewar (480, through its launch options) it arrives.
                bool ok = running && logged && (app == GameAppId || app == SpacewarAppId);
                Lc.Line("rp: CHECK " + (ok ? "OK" : "FAIL") + " steam answers for the game's AppID " + GameAppId + " or Spacewar " + SpacewarAppId);
                return ok;
            }
            catch (Exception e)
            {
                Lc.Line("rp: CHECK FAIL steam answers for the game's AppID (" + e.GetType().Name + ": " + e.Message + ")");
                return false;
            }
        }

        private static void Dialog()
        {
            try
            {
                SteamFriends.ActivateGameOverlayRemotePlayTogetherInviteDialog(CSteamID.Nil);
                Lc.Line("rp: invite dialog requested (overlay=" + SteamUtils.IsOverlayEnabled() + ")");
            }
            catch (Exception e) { Lc.Line("rp: invite dialog failed " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Invite(string id)
        {
            ulong v;
            if (!ulong.TryParse(id, out v)) { Lc.Line("rp: -lcrpinvite needs a SteamID64, got '" + id + "'"); return; }
            try
            {
                bool sent = SteamRemotePlay.BSendRemotePlayTogetherInvite(new CSteamID(v));
                Lc.Line("rp: BSendRemotePlayTogetherInvite returned " + sent);
            }
            catch (Exception e) { Lc.Line("rp: invite failed " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Logs the guests of Remote Play Together sessions when their number changes (device type only, no names).</summary>
        private static void Sessions()
        {
            try
            {
                uint n = SteamRemotePlay.GetSessionCount();
                if (n == _sessions) return;
                _sessions = n;
                var sb = new System.Text.StringBuilder("rp: sessions now " + n);
                for (int i = 0; i < n; i++)
                {
                    var sid = SteamRemotePlay.GetSessionID(i);
                    sb.Append(" | #").Append(i).Append(' ').Append(SteamRemotePlay.GetSessionClientFormFactor(sid));
                }
                Lc.Line(sb.ToString());
            }
            catch (Exception e) { Lc.LineOnce("rp: session poll failed " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
