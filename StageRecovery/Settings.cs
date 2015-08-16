using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StageRecovery
{
    //The settings class. It handles all interactions with the settings file and is related to the GUI for changing the settings.
    public class Settings
    {
        //This is the Settings instance. Only one exists and it's how we interact with the settings
        public static Settings instance;
        //This is the instance of the SettingsGUI, where we can change settings in game. This is how we interact with that class.
        public SettingsGUI gui = new SettingsGUI();
        //The path for the settings file (Config.txt)
        protected String filePath = KSPUtil.ApplicationRootPath + "GameData/StageRecovery/Config.txt";
        //The persistent values are saved to the file and read in by them. They are saved as Name = Value and separated by new lines
        [Persistent] public float RecoveryModifier, DeadlyReentryMaxVelocity, CutoffVelocity, LowCut, HighCut, MinTWR, DistanceOverride;
        [Persistent] public bool SREnabled, RecoverScience, RecoverKerbals, ShowFailureMessages, ShowSuccessMessages, FlatRateModel, PoweredRecovery, RecoverClamps, UseUpgrades, UseToolbarMod;

        public bool Clicked = false;
        public List<RecoveryItem> RecoveredStages, DestroyedStages;
        public IgnoreList BlackList = new IgnoreList();


        //The constructor for the settings class. It sets the values to default (which are then replaced when Load() is called)
        public Settings()
        {
            SREnabled = true;
            RecoveryModifier = 0.75f;
            RecoverKerbals = true;
            RecoverScience = true;
            ShowFailureMessages = true;
            ShowSuccessMessages = true;
            DeadlyReentryMaxVelocity = 2000f;
            CutoffVelocity = 10f;
            FlatRateModel = false;
            LowCut = 6f;
            HighCut = 12f;
            PoweredRecovery = true;
            RecoverClamps = true;
            MinTWR = 1.0f;
            UseUpgrades = true;
            UseToolbarMod = true;
            DistanceOverride = -1.0f;

            RecoveredStages = new List<RecoveryItem>();
            DestroyedStages = new List<RecoveryItem>();
        }

        //Loads the settings from the file
        public void Load()
        {
            if (System.IO.File.Exists(filePath))
            {
                ConfigNode cnToLoad = ConfigNode.Load(filePath);
                ConfigNode.LoadObjectFromConfig(this, cnToLoad);
            }
        }

        //Saves the settings to the file
        public void Save()
        {
            ConfigNode cnTemp = ConfigNode.CreateConfigFromObject(this, new ConfigNode());
            cnTemp.Save(filePath);
        }

        public void ClearStageLists()
        {
            RecoveredStages.Clear();
            DestroyedStages.Clear();
            gui.flightGUI.NullifySelected();
        }
    }

    public class IgnoreList
    {
        //Set the default ignore items (fairings, escape systems, flags, and asteroids (which are referred to as potatoroids))
        public List<string> ignore = new List<string> {"fairing", "escape system", "flag", "potato"};
        string filePath = KSPUtil.ApplicationRootPath + "GameData/StageRecovery/ignore.txt";
        public void Load()
        {
            if (System.IO.File.Exists(filePath))
            {
                ignore = System.IO.File.ReadAllLines(filePath).ToList();
            }
        }

        public void Save()
        {
            System.IO.File.WriteAllLines(filePath, ignore.ToArray());
        }

        public bool Contains(string item)
        {
            if (ignore.Count == 0) Load();
            return ignore.FirstOrDefault(s => item.ToLower().Contains(s)) != null;
        }

        public void Add(string item)
        {
            if (!ignore.Contains(item.ToLower()))
                ignore.Add(item.ToLower());
        }

        public void Remove(string item)
        {
            if (ignore.Contains(item.ToLower()))
                ignore.Remove(item.ToLower());
        }
    }

    //This class controls all the GUI elements for the in-game settings menu
    public class SettingsGUI
    {
        public FlightGUI flightGUI = new FlightGUI();

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
        private float recMod, cutoff, lowCut, highCut;
        //Booleans are cool though. In fact, they are prefered (since they work well with toggles)
        private bool enabled, recoverSci, recoverKerb, showFail, showSuccess, flatRate, poweredRecovery, recoverClamps, useUpgrades, useToolbar;

        private Vector2 scrollPos;

        //The stock button. Used if Blizzy's toolbar isn't installed.
        public ApplicationLauncherButton SRButtonStock = null;
        //This function is used to add the button to the stock toolbar
        public void OnGUIAppLauncherReady()
        {
            if (ToolbarManager.ToolbarAvailable && Settings.instance.UseToolbarMod)
                return;
            bool vis;
            if (ApplicationLauncher.Ready && (SRButtonStock == null || !ApplicationLauncher.Instance.Contains(SRButtonStock, out vis))) //Add Stock button
            {
                SRButtonStock = ApplicationLauncher.Instance.AddModApplication(
                    ShowWindow,
                    hideAll,
                    OnHoverOn,
                    OnHoverOff,
                    DummyVoid,
                    DummyVoid,
                    (ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.MAPVIEW),
                    GameDatabase.Instance.GetTexture("StageRecovery/icon", false));
            }
        }
        void DummyVoid() { }

        //This is all for Blizzy's toolbar
        public IButton SRToolbarButton = null;
        public void AddToolbarButton()
        {
            SRToolbarButton = ToolbarManager.Instance.add("StageRecovery", "MainButton");
            SRToolbarButton.Visibility = new GameScenesVisibility(new GameScenes[] {GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.EDITOR});
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
            if (Settings.instance.Clicked && (showWindow || flightGUI.showFlightGUI))
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
            if (HighLogic.LoadedSceneIsFlight && !Settings.instance.Clicked)
                flightGUI.showFlightGUI = false;
        }

        //This shows the correct window depending on the current scene
        public void ShowWindow()
        {
            Settings.instance.Clicked = true;
            if (HighLogic.LoadedSceneIsFlight)
                flightGUI.showFlightGUI = true;
            else if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                ShowSettings();
            else if (HighLogic.LoadedSceneIsEditor)
                EditorCalc();
        }

        //Does stuff to draw the window.
        public void SetGUIPositions(GUI.WindowFunction OnWindow)
        {
            if (showWindow) mainWindowRect = GUILayout.Window(8940, mainWindowRect, DrawSettingsGUI, "StageRecovery", HighLogic.Skin.window);
            if (flightGUI.showFlightGUI) flightGUI.flightWindowRect = GUILayout.Window(8940, flightGUI.flightWindowRect, flightGUI.DrawFlightGUI, "StageRecovery", HighLogic.Skin.window);
            if (showBlacklist) blacklistRect = GUILayout.Window(8941, blacklistRect, DrawBlacklistGUI, "Ignore List", HighLogic.Skin.window);
        }

        //More drawing window stuff. I only half understand this. It just works.
        public void DrawGUIs(int windowID)
        {
            if (showWindow) DrawSettingsGUI(windowID);
            if (flightGUI.showFlightGUI) flightGUI.DrawFlightGUI(windowID);
            if (showBlacklist) DrawBlacklistGUI(windowID);
        }

        //Hide all the windows. We only have one so this isn't super helpful, but alas.
        public void hideAll()
        {
            showWindow = false;
            flightGUI.showFlightGUI = false;
            showBlacklist = false;
            Settings.instance.Clicked = false;
        }

        //Resets the windows. Hides them and resets the Rect object. Not really needed, but it's here
        public void reset()
        {
            hideAll();
            mainWindowRect = new Rect(0, 0, windowWidth, 1);
            flightGUI.flightWindowRect = new Rect((Screen.width - 768) / 2, (Screen.height - 540) / 2, 768, 540);
            blacklistRect = new Rect(0, 0, 360, 1);
        }

        private string tempListItem = "";
        private void DrawBlacklistGUI(int windowID)
        {
            GUILayout.BeginVertical();
            scrollPos = GUILayout.BeginScrollView(scrollPos, HighLogic.Skin.textArea, GUILayout.Height(Screen.height / 4));
            foreach (string s in Settings.instance.BlackList.ignore)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(s);
                if (GUILayout.Button("Remove", GUILayout.ExpandWidth(false)))
                {
                    Settings.instance.BlackList.Remove(s);
                    break;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            tempListItem = GUILayout.TextField(tempListItem);
            if (GUILayout.Button("Add", GUILayout.ExpandWidth(false)))
            {
                Settings.instance.BlackList.Add(tempListItem);
                tempListItem = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                Settings.instance.BlackList.Save();
                showBlacklist = false;
            }
            if (GUILayout.Button("Cancel"))
            {
                Settings.instance.BlackList.Load();
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
            enabled = Settings.instance.SREnabled;
            recMod = Settings.instance.RecoveryModifier;
            cutoff = Settings.instance.CutoffVelocity;
            DRMaxVel = Settings.instance.DeadlyReentryMaxVelocity.ToString();
            recoverSci = Settings.instance.RecoverScience;
            recoverKerb = Settings.instance.RecoverKerbals;
            showFail = Settings.instance.ShowFailureMessages;
            showSuccess = Settings.instance.ShowSuccessMessages;
            flatRate = Settings.instance.FlatRateModel;
            lowCut = Settings.instance.LowCut;
            highCut = Settings.instance.HighCut;
            poweredRecovery = Settings.instance.PoweredRecovery;
            recoverClamps = Settings.instance.RecoverClamps;
            minTWR = Settings.instance.MinTWR.ToString();
            useUpgrades = Settings.instance.UseUpgrades;
            useToolbar = Settings.instance.UseToolbarMod;
            showWindow = true;
        }

        //The function that actually draws all the gui elements. I use GUILayout for doing everything because it's easy to use.
        private void DrawSettingsGUI(int windowID)
        {
            //We start by begining a vertical segment. All new elements will be placed below the previous one.
            GUILayout.BeginVertical();

            //Whether the mod is enabled or not
            enabled = GUILayout.Toggle(enabled, " Mod Enabled");

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
                highCut = GUILayout.HorizontalSlider(highCut, lowCut+0.1f, 16);
                highCut = (float)Math.Max(Math.Round(highCut, 1), lowCut+0.1);
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
                Settings.instance.SREnabled = enabled;
                Settings.instance.FlatRateModel = flatRate;
                Settings.instance.LowCut = lowCut;
                Settings.instance.HighCut = highCut;
                Settings.instance.RecoveryModifier = recMod;
                Settings.instance.CutoffVelocity = cutoff;
                //Strings must be parsed into the correct type. Using TryParse returns a bool stating whether it was sucessful. The value is saved in the out if it works
                //Otherwise we set the value to the default
                if (!float.TryParse(DRMaxVel, out Settings.instance.DeadlyReentryMaxVelocity))
                    Settings.instance.DeadlyReentryMaxVelocity = 2000f;
                Settings.instance.RecoverScience = recoverSci;
                Settings.instance.RecoverKerbals = recoverKerb;
                Settings.instance.ShowFailureMessages = showFail;
                Settings.instance.ShowSuccessMessages = showSuccess;
                Settings.instance.PoweredRecovery = poweredRecovery;
                Settings.instance.RecoverClamps = recoverClamps;
                Settings.instance.UseUpgrades = useUpgrades;
                Settings.instance.UseToolbarMod = useToolbar;
                if (!float.TryParse(minTWR, out Settings.instance.MinTWR))
                    Settings.instance.MinTWR = 1.0f;
                //Finally we save the settings to the file
                Settings.instance.Save();
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
            float Vt = DetermineVtEditor(false);
            StringBuilder msg = new StringBuilder();
            bool recovered = false;
            if (Settings.instance.FlatRateModel)
                recovered = Vt < Settings.instance.CutoffVelocity;
            else
                recovered = Vt < Settings.instance.HighCut;

            msg.AppendLine("-- Current Fuel Values --");
            if (recovered) msg.AppendLine("Status: Recovered");
            else msg.AppendLine("Status: Destroyed");

            float recoveryPercent = 0;
            if (recovered && Settings.instance.FlatRateModel) recoveryPercent = 1;
            else if (recovered && !Settings.instance.FlatRateModel) recoveryPercent = RecoveryItem.GetVariableRecoveryValue(Vt);

            msg.AppendLine("Terminal velocity: " + Math.Round(Vt, 1) + " m/s");
            msg.AppendLine("Percent recovered: " + Math.Round(100 * recoveryPercent, 2) + "%");

            float fullVt = Vt;
            Vt = DetermineVtEditor(true);
            if (fullVt != Vt)
            {
                msg.AppendLine("\n-- Empty Values --");
                recovered = false;
                if (Settings.instance.FlatRateModel)
                    recovered = Vt < Settings.instance.CutoffVelocity;
                else
                    recovered = Vt < Settings.instance.HighCut;

                if (recovered) msg.AppendLine("Status: Recovered");
                else msg.AppendLine("Status: Destroyed");

                recoveryPercent = 0;
                if (recovered && Settings.instance.FlatRateModel) recoveryPercent = 1;
                else if (recovered && !Settings.instance.FlatRateModel) recoveryPercent = RecoveryItem.GetVariableRecoveryValue(Vt);

                msg.AppendLine("Terminal velocity: " + Math.Round(Vt, 1) + " m/s");
                msg.AppendLine("Percent recovered: " + Math.Round(100 * recoveryPercent, 2) + "%");
            }
            MessageSystemButton.MessageButtonColor color = recovered ? MessageSystemButton.MessageButtonColor.BLUE : MessageSystemButton.MessageButtonColor.RED;
            MessageSystem.Message m = new MessageSystem.Message("StageRecovery Editor Help", msg.ToString(), color, MessageSystemButton.ButtonIcons.MESSAGE);
            MessageSystem.Instance.AddMessage(m);
            
            if (SRButtonStock != null) SRButtonStock.SetFalse();
        }

        public float DetermineVtEditor(bool empty)
        {
            float Vt = float.MaxValue;
            List<Part> parts = EditorLogic.fetch.ship.Parts;
            bool realChuteInUse = false;
            float totalMass = 0;
            //float dragCoeff = 0;
            double totalParachuteArea = 0;
            float RCParameter = 0;
            foreach (Part part in parts)
            {
                totalMass += part.mass;
                if (!empty) totalMass += part.GetResourceMass();
              /*  bool hasRealChute = part.Modules.Contains("RealChuteModule");
                bool hasChute = part.Modules.Contains("ModuleParachute");
                if (hasRealChute) realChuteInUse = true;*/

                if (part.Modules.Contains("RealChuteModule"))
                {
                    PartModule realChute = part.Modules["RealChuteModule"];
                    ConfigNode rcNode = new ConfigNode();
                    realChute.Save(rcNode);

                    //This is where the Reflection starts. We need to access the material library that RealChute has, so we first grab it's Type
                    Type matLibraryType = AssemblyLoader.loadedAssemblies
                        .SelectMany(a => a.assembly.GetExportedTypes())
                        .SingleOrDefault(t => t.FullName == "RealChute.Libraries.MaterialsLibrary");

                    //We make a list of ConfigNodes containing the parachutes (usually 1, but now there can be any number of them)
                    //We get that from the PPMS 
                    ConfigNode[] parachutes = rcNode.GetNodes("PARACHUTE");
                    //We then act on each individual parachute in the module
                    foreach (ConfigNode chute in parachutes)
                    {
                        //First off, the diameter of the parachute. From that we can (later) determine the Vt, assuming a circular chute
                        float diameter = float.Parse(chute.GetValue("deployedDiameter"));
                        //The name of the material the chute is made of. We need this to get the actual material object and then the drag coefficient
                        string mat = chute.GetValue("material");
                        //This grabs the method that RealChute uses to get the material. We will invoke that with the name of the material from before.
                        System.Reflection.MethodInfo matMethod = matLibraryType.GetMethod("GetMaterial", new Type[] { mat.GetType() });
                        //In order to invoke the method, we need to grab the active instance of the material library
                        object MatLibraryInstance = matLibraryType.GetProperty("instance").GetValue(null, null);
                        //With the library instance we can invoke the GetMaterial method (passing the name of the material as a parameter) to receive an object that is the material
                        object materialObject = matMethod.Invoke(MatLibraryInstance, new object[] { mat });
                        //With that material object we can extract the dragCoefficient using the helper function above.
                        float dragC = (float)StageRecovery.GetMemberInfoValue(materialObject.GetType().GetMember("dragCoefficient")[0], materialObject);
                        //Now we calculate the RCParameter. Simple addition of this doesn't result in perfect results for Vt with parachutes with different diameter or drag coefficients
                        //But it works perfectly for mutiple identical parachutes (the normal case)
                        RCParameter += dragC * (float)Math.Pow(diameter, 2);
                    }
                    realChuteInUse = true;
                }
                else if (part.Modules.Contains("RealChuteFAR")) //RealChute Lite for FAR
                {
                    PartModule realChute = part.Modules["RealChuteFAR"];
                    float diameter = (float)realChute.Fields.GetValue("deployedDiameter");
                    // = float.Parse(realChute.moduleValues.GetValue("deployedDiameter"));
                    float dragC = 1.0f; //float.Parse(realChute.moduleValues.GetValue("staticCd"));
                    RCParameter += dragC * (float)Math.Pow(diameter, 2);

                    realChuteInUse = true;
                }
                else if (!realChuteInUse && part.Modules.Contains("ModuleParachute"))
                {
                    ModuleParachute mp = (ModuleParachute)part.Modules["ModuleParachute"];
                    //dragCoeff += part.mass * mp.fullyDeployedDrag;
                    totalParachuteArea += mp.areaDeployed;
                }
               /* else
                {
                    //dragCoeff += part.mass * part.maximum_drag;
                    totalParachuteArea += 0.1;
                }*/
            }
            /*if (!realChuteInUse)
            {
                //This all follows from the formulas on the KSP wiki under the atmosphere page. http://wiki.kerbalspaceprogram.com/wiki/Atmosphere
                //Divide the current value of the dragCoeff by the total mass. Now we have the actual drag coefficient for the vessel
                //dragCoeff = dragCoeff / (totalMass);
                //Calculate Vt by what the wiki says
                //Vt = (float)(Math.Sqrt((250 * 6.674E-11 * 5.2915793E22) / (3.6E11 * 1.22309485 * dragCoeff)));
                //float adjuster = 0;
                //ConfigNode aNode = ConfigNode.Load(KSPUtil.ApplicationRootPath + "GameData/StageRecovery/adjust.txt");
                //adjuster = float.Parse(aNode.GetValue("adjuster"));
                //Vt = (float)Math.Sqrt((8000 * totalMass * 9.8) / (1.223 * adjuster * totalParachuteArea));
               // Debug.Log(totalParachuteArea);
                Vt = (float)(63*Math.Pow(totalMass / totalParachuteArea, 0.4));
            }*/
            //Otherwise we're using RealChutes and we have a bit different of a calculation
            if (realChuteInUse)
            {
                //This is according to the formulas used by Stupid_Chris in the Real Chute drag calculator program included with Real Chute. Source: https://github.com/StupidChris/RealChute/blob/master/Drag%20Calculator/RealChute%20drag%20calculator/RCDragCalc.cs
                Vt = (float)Math.Sqrt(((8000 * totalMass * 9.8) / (1.223 * Math.PI * RCParameter)));
            }
            else if (totalParachuteArea != 0)
            {
                Vt = (float)(63 * Math.Pow(totalMass / totalParachuteArea, 0.4));
            }
            else
            {
                Vt = 200;
            }
            return Vt;
        }

    }
}
/*
Copyright (C) 2014  Michael Marvin

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/