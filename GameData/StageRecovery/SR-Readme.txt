Installation:
Merge the GameData folder with the one located in the KSP directory. Delete the existing StageRecovery folder when updating to ensure compatibility.


Forum: http://forum.kerbalspaceprogram.com/threads/86677-0-24-2-StageRecovery-Recover-Funds-from-Dropped-Stages-v1-4-1-%288-22-14%29
Source: https://github.com/magico13/StageRecovery
Issue Tracker: https://github.com/magico13/StageRecovery/issues

Reporting bugs:
Please include the output_log.txt file from the KSP_Data folder if using 32 bit or the KSP_x64_Data folder if using 64 bit. Report the bug, with directions on how to reproduce it, on either the forum page or issue tracker, listed above.


Changelog:
1.5.3 - (12/22/2014)
 - Fixed issue with losing experience on recovery. Kerbals now gain experience as appropriate for landing on Kerbin.

1.5.2.1 - (12/15/2014)
- Made min TWR setting functional
- Fixed issue with calculating parachute drag values that caused parachute recovery to not function.

1.5.2 - (12/15/2014)
- Compatibility update for KSP 0.90
- Automatic recovery of launch clamps when they are unloaded.
- Right clicking on a stage in the flight GUI will now delete it.
- Added indicator to flight GUI showing which stage is selected.
- Several bug fixes.
- Contains a bug where kerbals will lose experience if they are in the craft when it's "recovered". Will be fixed soon.

1.5.1 - (10/07/2014)
- Compatibility update for KSP 0.25

1.5.0 - (09/06/2014)
- Added Ignore List. Any stages made up entirely of parts in the ignore list won't attempt to be recovered.
- Reworked the flight-GUI a bit. Made it smaller, draggable, and minimizes to just the list until a stage is selected. Hopefully even less intrusive now.
- Found a general solution to the fuel use problem for powered recovery. Can now handle engines that require any fuel amounts without being CPU intensive.
- Forced no checking on scene change. Should fix erroneous messages appearing on scene change for some users.

1.4.3 - (08/30/2014)
- Should have been 0.01 pressure. I'm sorry about the second update! :(

1.4.2 - (08/30/2014)
- Changed recovery code to check for altitudes above 100 meters and pressures above 0.1 instead of just searching for below 35km.
- Fixed issue with displaying orbital velocity vector instead of speed in Flight GUI.
- Was returning funds even for stages that had burned up, fixed now.

1.4.1 - (08/22/2014)
- Added error catching to recovery code. Even if there's a bug, it shouldn't break your game now.
- Removed a bunch of debug code from the Powered Recovery code.
- Remembered to include the license in the download.

1.4.0 - (08/18/2014)
- Powered recovery. Controlled stages can be landed with their engines. Requirements will be listed in a separate section.
- Editor helper now shows results for current fuel levels and with empty fuel levels.
- Several small improvements to flight GUI (wording and such).
- Several bug fixes for Vt calculation and with stock parachutes and crashes.

1.3.0 - (08/05/2014)
- New Flight GUI which presents all Recovered and Destroyed Stages since Flight was started in a convenient, easy to use dialog.
- Editor Helper: Click the SR icon to see what the terminal velocity, recovery status, and percent recovered will be for the current vessel (including current fuel)
- Fixed an issue with calculation of terminal velocities. I was missing a square root.

1.2.1 - (08/02/2014)
- Fixed small issue with recovery when below the low cutoff velocity
- Hopefully averted issue with negative refunds (minimum per part should be zero)

1.2.0 - (08/02/2014)
- Added Varaible Recovery Rate model where the recovery percentage is determined by the velocity between two cutoffs. A more detailed explanation can be found here https://github.com/magico13/StageRecovery/issues/1
- Updated the API, see explanation later in the OP.
- Optional Blizzy toolbar support
- Non-ablative shielding counts as 400 ablative shielding instead of decreasing the burnChance to 0.

1.1.4 - (07/28/2014)
- Added in game settings menu (space center scene)
- Changed recovery code to calculate exclusively in m/s. Cutoff velocity is configurable between 2 and 12 m/s
- Added EXPERIMENTAL Deadly Reentry support. It's based on velocity percentages above the DeadlyReentryMaxVelocity. 2% chance of failure per 1% speed exceeded. Each 1% of heat shield removes 1% chance of failure.
- Changed recovery of kerbals and science to enabled by default.

1.1.3 - (07/22/2014) - Added API. Made it so Kerballed command pods will also increase recovery value to 100% of Stock.

1.1.2 - (07/20/2014) - Fixed a small issue that would cause recovery to fail if multiple identical parts were on the same recovered vessel.

1.1.1 - (07/19/2014) - Added science recovery, 100% if probe core attached, and messages.

1.1.0 - (07/18/2014) - Added ability to recover Kerbals (disabled by default). Limited recovery modifier to between 0 and 1. Updated to work with latest RealChutes.

1.0.1 - (07/18/2014) - If you make something configurable, you should make sure it actually does something. Derp.

1.0 - (07/18/2014) - Initial release