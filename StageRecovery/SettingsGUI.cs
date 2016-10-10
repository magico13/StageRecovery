using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StageRecovery
{
    //This class controls all the GUI elements for the in-game settings menu
    public class SettingsGUI
    {
        public FlightGUI flightGUI = new FlightGUI();

        public EditorGUI editorGUI = new EditorGUI();

        //The window is only shown when this is true
        private bool showWindow, showBlacklist;

        //The width of the window, for easy changing later if need be
        private static int windowWidth = 200;
        //The main Rect object that the window occupies
        public Rect mainWindowRect = new Rect(0, 0, windowWidth, 1);
        public Rect blacklistRect = new Rect(0, 0, 360, 1);

        //Temporary holders for the settings. They are only copied to the settings when the Save button is pressed.
        //Floats, ints, and other numbers are best represented as strings until the settings are saved (then you parse them)
        //The reason for this is that you can't enter decimal values easily since typing "2." gets changed to "2" when to do a toString() ("25" then "2.5" will work though)
        private string DRMaxVel, minTWR;
        //The exception is for sliders
        private float recMod, cutoff, lowCut, highCut, globMod;
        //Booleans are cool though. In fact, they are prefered (since they work well with toggles)
        private bool enabled, recoverSci, recoverKerb, showFail, showSuccess, flatRate, poweredRecovery, recoverClamps, useUpgrades, useToolbar;

        private Vector2 scrollPos;

        //The stock button. Used if Blizzy's toolbar isn't installed.
        public ApplicationLauncherButton SRButtonStock = null;
        //This function is used to add the button to the stock toolbar
        public void OnGUIAppLauncherReady()
        {
            if (ToolbarManager.ToolbarAvailable && Settings.Instance.UseToolbarMod)
                return;
            if (Settings.Instance.HideButton) //If told to hide the button, then don't show the button. Blizzy's can do this automatically.
                return;
            bool vis;
            if (ApplicationLauncher.Ready && (SRButtonStock == null || !ApplicationLauncher.Instance.Contains(SRButtonStock, out vis))) //Add Stock button
            {
                SRButtonStock = ApplicationLauncher.Instance.AddModApplication(
                    ShowWindow,
                    hideAll,
                    OnHoverOn,
                    OnHoverOff,
                    null,
                    null,
                    (ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.MAPVIEW),
                    GameDatabase.Instance.GetTexture("StageRecovery/icon", false));
            }
        }

        //This is all for Blizzy's toolbar
        public IButton SRToolbarButton = null;
        public void AddToolbarButton()
        {
            SRToolbarButton = ToolbarManager.Instance.add("StageRecovery", "MainButton");
            SRToolbarButton.Visibility = new GameScenesVisibility(new GameScenes[] { GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.EDITOR });
            SRToolbarButton.TexturePath = "StageRecovery/icon_blizzy";
            SRToolbarButton.ToolTip = "StageRecovery";
            SRToolbarButton.OnClick += ((e) =>
            {
                onClick();
            });
        }

        //This method is used when the toolbar button is clicked. It alternates between showing the window and hiding it.
        public void onClick()
        {
            if (Settings.Instance.Clicked && (showWindow || flightGUI.showFlightGUI || editorGUI.showEditorGUI))
                hideAll();
            else
                ShowWindow();
        }

        //When the button is hovered over, show the flight GUI if in flight
        public void OnHoverOn()
        {
            if (HighLogic.LoadedSceneIsFlight)
                flightGUI.showFlightGUI = true;
        }

        //When the button is no longer hovered over, hide the flight GUI if it wasn't clicked
        public void OnHoverOff()
        {
            if (HighLogic.LoadedSceneIsFlight && !Settings.Instance.Clicked)
                flightGUI.showFlightGUI = false;
        }

        //This shows the correct window depending on the current scene
        public void ShowWindow()
        {
            Settings.Instance.Clicked = true;
            if (HighLogic.LoadedSceneIsFlight)
            {
                flightGUI.showFlightGUI = true;
            }
            else if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                ShowSettings();
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                EditorCalc();
            }
        }

        //Does stuff to draw the window.
        public void SetGUIPositions(GUI.WindowFunction OnWindow)
        {
            if (showWindow)
                mainWindowRect = GUILayout.Window(8940, mainWindowRect, DrawSettingsGUI, "StageRecovery", HighLogic.Skin.window);
            if (flightGUI.showFlightGUI)
                flightGUI.flightWindowRect = GUILayout.Window(8940, flightGUI.flightWindowRect, flightGUI.DrawFlightGUI, "StageRecovery", HighLogic.Skin.window);
            if (showBlacklist)
                blacklistRect = GUILayout.Window(8941, blacklistRect, DrawBlacklistGUI, "Ignore List", HighLogic.Skin.window);
            if (editorGUI.showEditorGUI)
                editorGUI.EditorGUIRect = GUILayout.Window(8940, editorGUI.EditorGUIRect, editorGUI.DrawEditorGUI, "StageRecovery", HighLogic.Skin.window);
        }

        //More drawing window stuff. I only half understand this. It just works.
        public void DrawGUIs(int windowID)
        {
            if (showWindow)
                DrawSettingsGUI(windowID);
            if (flightGUI.showFlightGUI)
                flightGUI.DrawFlightGUI(windowID);
            if (showBlacklist)
                DrawBlacklistGUI(windowID);
            if (editorGUI.showEditorGUI)
                editorGUI.DrawEditorGUI(windowID);
        }

        //Hide all the windows. We only have one so this isn't super helpful, but alas.
        public void hideAll()
        {
            showWindow = false;
            flightGUI.showFlightGUI = false;
            editorGUI.showEditorGUI = false;
            showBlacklist = false;
            Settings.Instance.Clicked = false;
            editorGUI.UnHighlightAll();
        }

        //Resets the windows. Hides them and resets the Rect object. Not really needed, but it's here
        public void reset()
        {
            hideAll();
            mainWindowRect = new Rect(0, 0, windowWidth, 1);
            flightGUI.flightWindowRect = new Rect((Screen.width - 768) / 2, (Screen.height - 540) / 2, 768, 540);
            editorGUI.EditorGUIRect = new Rect(Screen.width / 3, Screen.height / 3, 200, 1);
            blacklistRect = new Rect(0, 0, 360, 1);
        }

        private string tempListItem = "";
        private void DrawBlacklistGUI(int windowID)
        {
            GUILayout.BeginVertical();
            scrollPos = GUILayout.BeginScrollView(scrollPos, HighLogic.Skin.textArea, GUILayout.Height(Screen.height / 4));
            foreach (string s in Settings.Instance.BlackList.ignore)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(s);
                if (GUILayout.Button("Remove", GUILayout.ExpandWidth(false)))
                {
                    Settings.Instance.BlackList.Remove(s);
                    break;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            tempListItem = GUILayout.TextField(tempListItem);
            if (GUILayout.Button("Add", GUILayout.ExpandWidth(false)))
            {
                Settings.Instance.BlackList.Add(tempListItem);
                tempListItem = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                Settings.Instance.BlackList.Save();
                showBlacklist = false;
            }
            if (GUILayout.Button("Cancel"))
            {
                Settings.Instance.BlackList.Load();
                showBlacklist = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            if (!Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))
                GUI.DragWindow();
        }

        //This function will show the settings window and copy the current settings into their holders
        public void ShowSettings()
        {
            enabled = Settings.Instance.SREnabled;
            recMod = Settings.Instance.RecoveryModifier;
            cutoff = Settings.Instance.CutoffVelocity;
            DRMaxVel = Settings.Instance.DeadlyReentryMaxVelocity.ToString();
            recoverSci = Settings.Instance.RecoverScience;
            recoverKerb = Settings.Instance.RecoverKerbals;
            showFail = Settings.Instance.ShowFailureMessages;
            showSuccess = Settings.Instance.ShowSuccessMessages;
            flatRate = Settings.Instance.FlatRateModel;
            lowCut = Settings.Instance.LowCut;
            highCut = Settings.Instance.HighCut;
            poweredRecovery = Settings.Instance.PoweredRecovery;
            recoverClamps = Settings.Instance.RecoverClamps;
            minTWR = Settings.Instance.MinTWR.ToString();
            useUpgrades = Settings.Instance.UseUpgrades;
            useToolbar = Settings.Instance.UseToolbarMod;
            globMod = Settings.Instance.GlobalModifier;
            showWindow = true;
        }

        //The function that actually draws all the gui elements. I use GUILayout for doing everything because it's easy to use.
        private void DrawSettingsGUI(int windowID)
        {
            //We start by begining a vertical segment. All new elements will be placed below the previous one.
            GUILayout.BeginVertical();

            //Whether the mod is enabled or not
            enabled = GUILayout.Toggle(enabled, " Mod Enabled");

            //A global modifier that affects returns
            GUILayout.Label("Global Modifier: "+Math.Round(100*globMod) + "%");
            globMod = (float)Math.Round(GUILayout.HorizontalSlider(globMod, 0, 1), 3);

            //We can toggle the Flat Rate Model on and off with a toggle
            flatRate = GUILayout.Toggle(flatRate, flatRate ? "Flat Rate Model" : "Variable Rate Model");
            //If Flat Rate is on we show this info
            if (flatRate)
            {
                //First off is a label saying what the modifier is (in percent)
                GUILayout.Label("Recovery Modifier: " + Math.Round(100 * recMod) + "%");
                //Then we have a slider that goes between 0 and 1 that sets the recMod
                recMod = GUILayout.HorizontalSlider(recMod, 0, 1);
                //We round the recMod for two reasons: it looks better and it makes it easier to select specific values. 
                //In this case it limits it to whole percentages
                recMod = (float)Math.Round(recMod, 2);

                //We do a similar thing for the cutoff velocity, limiting it to between 2 and 12 m/s
                GUILayout.Label("Cutoff Velocity: " + cutoff + "m/s");
                cutoff = GUILayout.HorizontalSlider(cutoff, 2, 12);
                cutoff = (float)Math.Round(cutoff, 1);
            }
            //If we're using the Variable Rate Model we have to show other info
            else
            {
                //Like for the flat rate recovery modifier and cutoff, we present a label and a slider for the low cutoff velocity
                GUILayout.Label("Low Cutoff Velocity: " + lowCut + "m/s");
                lowCut = GUILayout.HorizontalSlider(lowCut, 0, 10);
                lowCut = (float)Math.Round(lowCut, 1);

                //And another slider for the high cutoff velocity (with limits between lowCut and 16)
                GUILayout.Label("High Cutoff Velocity: " + highCut + "m/s");
                highCut = GUILayout.HorizontalSlider(highCut, lowCut + 0.1f, 16);
                highCut = (float)Math.Max(Math.Round(highCut, 1), lowCut + 0.1);
            }

            //We begin a horizontal, meaning new elements will be placed to the right of previous ones
            GUILayout.BeginHorizontal();
            //First element is a label
            GUILayout.Label("DR Velocity");
            //Followed by a text field where we can set the DRMaxVel value (as a string for the moment)
            DRMaxVel = GUILayout.TextField(DRMaxVel, 6);
            //Ending the horizontal means new elements will now be placed below previous ones (so these two will be side by side with things above and below too)
            //Make sure to End anything you Begin!
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Powered TWR");
            minTWR = GUILayout.TextField(minTWR, 4);
            GUILayout.EndHorizontal();

            //The rest are just toggles and are put one after the other
            recoverSci = GUILayout.Toggle(recoverSci, "Recover Science");
            recoverKerb = GUILayout.Toggle(recoverKerb, "Recover Kerbals");
            showFail = GUILayout.Toggle(showFail, "Failure Messages");
            showSuccess = GUILayout.Toggle(showSuccess, "Success Messages");
            poweredRecovery = GUILayout.Toggle(poweredRecovery, "Try Powered Recovery");
            recoverClamps = GUILayout.Toggle(recoverClamps, "Recover Clamps");
            useUpgrades = GUILayout.Toggle(useUpgrades, "Tie Into Upgrades");
            useToolbar = GUILayout.Toggle(useToolbar, "Use Toolbar Mod");

            if (GUILayout.Button("Edit Ignore List"))
            {
                showBlacklist = true;
            }

            //We then provide a single button to save the settings. The window can be closed by clicking on the toolbar button, which cancels any changes
            if (GUILayout.Button("Save"))
            {
                //When the button is clicked then this all is executed.
                //This all sets the settings to the GUI version's values
                Settings.Instance.SREnabled = enabled;
                Settings.Instance.FlatRateModel = flatRate;
                Settings.Instance.LowCut = lowCut;
                Settings.Instance.HighCut = highCut;
                Settings.Instance.RecoveryModifier = recMod;
                Settings.Instance.CutoffVelocity = cutoff;
                //Strings must be parsed into the correct type. Using TryParse returns a bool stating whether it was sucessful. The value is saved in the out if it works
                //Otherwise we set the value to the default
                if (!float.TryParse(DRMaxVel, out Settings.Instance.DeadlyReentryMaxVelocity))
                    Settings.Instance.DeadlyReentryMaxVelocity = 2000f;
                Settings.Instance.RecoverScience = recoverSci;
                Settings.Instance.RecoverKerbals = recoverKerb;
                Settings.Instance.ShowFailureMessages = showFail;
                Settings.Instance.ShowSuccessMessages = showSuccess;
                Settings.Instance.PoweredRecovery = poweredRecovery;
                Settings.Instance.RecoverClamps = recoverClamps;
                Settings.Instance.UseUpgrades = useUpgrades;
                Settings.Instance.UseToolbarMod = useToolbar;
                if (!float.TryParse(minTWR, out Settings.Instance.MinTWR))
                    Settings.Instance.MinTWR = 1.0f;
                Settings.Instance.GlobalModifier = globMod;
                //Finally we save the settings to the file
                Settings.Instance.Save();
            }

            //The last GUI element is added, so now we close the Vertical with EndVertical(). If you don't close all the things you open, the GUI will not display any elements
            GUILayout.EndVertical();

            //This last thing checks whether the right mouse button or middle mouse button are clicked on the window. If they are, we ignore it, otherwise we GUI.DragWindow()
            //Calling that allows the window to be moved by clicking it (anywhere empty on the window) with the left mouse button and dragging it to wherever you want.
            if (!Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))
                GUI.DragWindow();
        }

        public void EditorCalc()
        {
            if (EditorLogic.fetch.ship.parts.Count > 0)
            {
                editorGUI.BreakShipIntoStages();
                editorGUI.HighlightAll();
                editorGUI.showEditorGUI = true;
                editorGUI.EditorGUIRect.height = 1; //reset the height
            }
        }

    }
}
