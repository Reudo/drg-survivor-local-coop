DEEP ROCK GALACTIC: SURVIVOR - LOCAL CO-OP  (private test)
==========================================================
(Deutsche Fassung: LIES-MICH.txt)

What it is
----------
Two dwarves in one game (one game process, no network code): dwarf 1 and dwarf 2, each with their own controller, shared camera, shared XP,
separate wallets, level-ups and shop one after the other. The main menu gets a "LOCAL CO-OP" entry.

Install
-------
1. Close the game.
2. Extract the ZIP COMPLETELY (anywhere; keep the payload folder) and double-click Install-LocalCoop.bat (no admin rights needed). It
   - finds the Steam game folder itself (otherwise it asks for the path),
   - backs up your save to  Documents\DRG-Survivor-Saves-Backup-<time>,
   - installs BepInEx only if the game folder has none yet,
   - switches the BepInEx console off (a second game window blocks the guest's input in Remote Play Together),
   - moves the old network mod (DrgSurvivorCoopProbe.dll, LiteNetLib.dll) to BepInEx\plugins-disabled,
   - copies LocalCoop.dll to BepInEx\plugins,
   - switches BepInEx OFF for normal starts (doorstop_config.ini: enabled = false),
   - writes Start-Coop.bat into the game folder and a shortcut on the desktop,
   - sets up Remote Play Together if Spacewar is installed (see below); if not, it offers to open Spacewar's Steam install page.
   Everything is written to localcoop-install.log in the game folder.
3. The FIRST co-op start takes 1 to 3 minutes longer (BepInEx builds its files); after that it starts at normal speed.

Play
----
- Single player:   as always from Steam ("Play"). The mod is off then; the game is the unchanged game.
- Co-op, one PC:   desktop shortcut "DRG Survivor Co-op" (= Start-Coop.bat in the game folder). It starts the game through Steam with the launch
                   options  --doorstop-enabled true -lccoop.  Steam must be running (it is started otherwise).
- Co-op online with a friend (Steam Remote Play Together): start "Spacewar" in Steam (desktop shortcut "DRG Survivor Co-op (Remote Play)"),
                   not the game itself. DRG Survivor opens with LOCAL CO-OP. Then Shift+Tab -> Friends -> Remote Play Together.
- Check that the mod was on: lc.log in the game folder exists only after a co-op start; its first line names the launch options.

Remote Play Together (online with a friend) - the Spacewar trick
----------------------------------------------------------------
The Steam store page of the game does not list "Remote Play Together", so Steam does not offer the invite for the game itself. Spacewar (Valve's
sample app, steam://install/480, about 50 MB, free) offers it. The trick: Spacewar's exe is replaced by a 5 KB launcher that starts
DRG Survivor with the co-op options (--doorstop-enabled true -lccoop -lcremote; player 2 is the most recently connected gamepad).
The installer does this if Spacewar is installed. Otherwise: install Spacewar, close the game, run Enable-RemotePlayTrick.bat in the game folder.
Undo: Enable-RemotePlayTrick-Undo.bat (Spacewar's own exe is kept as <name>.lcbackup; Steam's "Verify integrity" restores it as well).

Three things that must be right, or the guest cannot play:
1. BepInEx console OFF: in BepInEx\config\BepInEx.cfg, [Logging.Console] has "Enabled = false" (the installer sets it). With the console, which is a second
   window of the game, Steam shows the guest "Input is temporarily disabled while the host is busy".
2. In the Steam overlay (Shift+Tab) open the controller settings and change the profile to "Gamepad". Without it the game sees no controller.
3. Start Spacewar, not the game, and invite the friend after the game has reached its main menu.
If Steam updates or verifies Spacewar, it may put its own exe back; run Enable-RemotePlayTrick.bat again then.
Status: the invite works and a full 5-stage run was played this way (owner test). Whether the guest's controller operates every screen
(lobby, level-up, shop, end screen) has not been tested systematically.

Playing
-------
- Main menu -> LOCAL CO-OP: lobby (two players, elimination or escort mission), sector/gate, biome, danger level, then both players choose
  class and subclass at the same time (player 1 left, player 2 right), player 2 presses READY, the host presses START RUN.
- Level-up and shop: player 1 first, then player 2. Level-ups show whose turn it is in the top right corner, the shop in its title ("PLAYER n" above it, title in the player's colour); the other player's controller cannot confirm then.

Uninstall
---------
Double-click Uninstall-LocalCoop.bat in the game folder. It removes the plugin, Start-Coop.bat, the shortcuts, the Remote Play trick (Spacewar's exe
is restored), the install log and the co-op logs, puts doorstop_config.ini, the BepInEx console setting and the old network mod back, and removes
asks whether BepInEx should be removed too (default yes if the installer brought it; if it was there before the answer defaults to no and the number of other plugin files that would go with it is shown). Afterwards it deletes the uninstaller itself, so nothing is left behind. Saves are never touched.

Known gaps
----------
See TEST-LISTE.txt (at the bottom). Co-op runs count in your normal profile (hence the backup at installation).
Problems: send lc.log from the game folder plus 2 to 3 sentences on what worked and what did not.

Settings
--------
localcoop-settings.txt in <game folder>\BepInEx\config (created at the first co-op start; an older file from the game folder is moved there, missing lines are added; # starts a comment). Currently the revive health and one experiment:
  ReviveMode = percent | flat      (default percent)
  ReviveHealthPercent = 20         (percent of the dwarf's max HP)
  ReviveHealthFlat = 20            (hit points)
  ReviveSeconds = 3                (seconds the other dwarf has to stand within 3 m; 0.5 to 60)
  SecondDrone = off | on           (experimental: player 2 gets a companion drone of its own; default off, both share one)
  Debug = off | on                 (extra trace lines in lc.log for bug reports; default off)
  DebugGodMode = off | on          (only while Debug = on: neither dwarf can die; default off)
Changes apply at the next start of the game. Invalid values are ignored (the default stays) and named in lc.log. Uninstall removes the file.

Co-op rules (what two dwarves share)
------------------------------------
- Revive (button REVIVE on the last page before the mission, default on): a dwarf that dies is down, the run only ends when both are down. The other
  dwarf stands within 3 m for 3 s (ReviveSeconds) and the downed one gets up with the health from the settings file.
- Artifacts: every artifact one dwarf gets, the other gets too (e.g. max HP; the Red Sugar artifact raises both dwarves' max HP for every pickup).
- General stat upgrades (level-up cards such as crit chance or move speed) count for both dwarves and are tagged COOP BONUS. Weapons and weapon
  upgrades stay with the dwarf that chose them.
- Loot crate (e.g. after the first boss): the host opens it, then player 2 gets a round of its own.
- Pause menu: it shows the dwarf whose pause button opened it (player 2: Start button) with "PLAYER n" in the player's colour above the title; only that controller drives it; in player 2's level-up round the Details window shows player 2's weapons.
- Level-ups: when several levels are due they alternate (player 1, player 2, player 1, ...). The headline says whose turn it is ("PLAYER 1 ..." orange, "PLAYER 2 ..." blue) and that player's pad buzzes once. General upgrades light the arrows on both equipment bars.
- Drop pod: at the end of a stage the pod leaves only when both dwarves are inside (a dwarf that is down does not count); a dwarf inside cannot be hurt. Arriving, both dwarves ride the pod down and walk down the ramp together.
- Minimap: a blue dot for player 2 next to the host's red one.
