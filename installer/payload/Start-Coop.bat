@echo off
rem Starts Deep Rock Galactic: Survivor as LOCAL CO-OP, through Steam (overlay and Remote Play work that way).
rem A normal start from Steam stays single player: BepInEx is off there and is only switched on by --doorstop-enabled true.
rem The game must be closed; if it is already running, Steam only brings its window to the front.
rem For Remote Play Together with a friend start "Spacewar" in Steam instead (see README.txt).
start "" "steam://run/2321470//--doorstop-enabled true -lccoop"
