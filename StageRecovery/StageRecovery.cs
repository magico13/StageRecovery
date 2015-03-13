using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP;
using UnityEngine;
using System.Collections;

namespace StageRecovery
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class StageRecovery : MonoBehaviour
    {
        //Flag that says whether the VesselDestroyEvent has been added, so we don't accidentally add it twice.
        private static bool eventAdded = false;
        private static bool sceneChangeComplete = false;

        //List of scenes where we shouldn't run the mod. I toyed with runOnce, but couldn't get it working
        private static List<GameScenes> forbiddenScenes = new List<GameScenes> { GameScenes.LOADING, GameScenes.LOADINGBUFFER, GameScenes.CREDITS, GameScenes.MAINMENU, GameScenes.SETTINGS };

        //Needed to instantiate the Blizzy Toolbar button
        internal StageRecovery()
        {
            if (ToolbarManager.ToolbarAvailable && Settings.instance != null)
                Settings.instance.gui.AddToolbarButton();
        }

        //Fired when the mod loads each scene
        public void Awake()
        {
            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene))
                return;

            //Create a new Settings instance if one doesn't exist
            if (Settings.instance == null)
                Settings.instance = new Settings();

            //Needed to start doing things with GUIs
            RenderingManager.AddToPostDrawQueue(0, OnDraw);
        }

        //Also needed for GUIs. Not sure why, but this is how KCT was given to me so that's the method I use
        private void OnDraw()
        {
            Settings.instance.gui.SetGUIPositions(OnWindow);
        }

        //Once again, GUIs
        private void OnWindow(int windowID)
        {
            Settings.instance.gui.DrawGUIs(windowID);
        }

        //When the scene changes and the mod is destroyed
        public void OnDestroy()
        {
            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene) || Settings.instance == null || Settings.instance.gui == null)
                return;

            //Remove the button from the stock toolbar
            if (Settings.instance.gui.SRButtonStock != null)
                ApplicationLauncher.Instance.RemoveModApplication(Settings.instance.gui.SRButtonStock);
            //Remove the button from Blizzy's toolbar
            if (Settings.instance.gui.SRToolbarButton != null)
                Settings.instance.gui.SRToolbarButton.Destroy();
        }

        //Fired when the mod loads each scene
        public void Start()
        {
            if (Settings.instance != null)
                Settings.instance.gui.hideAll();

            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene))
                return;

            //If the event hasn't been added yet, run this code (adds the event and the stock button)
            if (!eventAdded)
            {
                GameEvents.onGameSceneLoadRequested.Add(GameSceneLoadEvent);
                //Add the VesselDestroyEvent to the listeners
                GameEvents.onVesselDestroy.Add(VesselDestroyEvent);
                //Add the event that listens for unloads (for removing launch clamps)
                GameEvents.onVesselGoOnRails.Add(VesselUnloadEvent);
                //GameEvents..Add(DecoupleEvent);
                //If Blizzy's toolbar isn't available, use the stock one
                if (!ToolbarManager.ToolbarAvailable)
                    GameEvents.onGUIApplicationLauncherReady.Add(Settings.instance.gui.OnGUIAppLauncherReady);
                //Set the eventAdded flag to true so this code doesn't run again
                eventAdded = true;
            }
            //Load the settings from file
            Settings.instance.Load();
            //Confine the RecoveryModifier to be between 0 and 1
            if (Settings.instance.RecoveryModifier > 1) Settings.instance.RecoveryModifier = 1;
            if (Settings.instance.RecoveryModifier < 0) Settings.instance.RecoveryModifier = 0;
            //Save the settings file (in case it doesn't exist yet). I suppose this is somewhat unnecessary if the file exists
            Settings.instance.Save();

            //Load and resave the BlackList. The save ensures that the file will be created if it doesn't exist.
            Settings.instance.BlackList.Load();
            Settings.instance.BlackList.Save();

            if (!HighLogic.LoadedSceneIsFlight)
            {
                Settings.instance.ClearStageLists();
            }

            sceneChangeComplete = true;
        }

        public void DecoupleEvent(EventReport s)
        {
            Debug.Log("[SR] Decoupled and made vessel " + s.origin.vessel.vesselName);
        }

        public void GameSceneLoadEvent(GameScenes newScene)
        {
            sceneChangeComplete = false;
            if (newScene != GameScenes.FLIGHT)
                clampsRecovered.Clear();
        }

        private List<Vessel> clampsRecovered = new List<Vessel>();
        public void VesselUnloadEvent(Vessel vessel)
        {
            //If we're disabled, just return
            if (!Settings.instance.SREnabled)
                return;

            //If we aren't supposed to recover clamps, then don't try.
            if (!Settings.instance.RecoverClamps)
                return;

            //If the vessel or the protovessel are null then we surely can't do anything with them
            if (vessel == null || vessel.protoVessel == null)
                return;

            //If we've already recovered the clamps, then no need to try again
            if (clampsRecovered.Find(a => a.id == vessel.id) != null)
                return;

            //Assign the pv variable to the protovessel, then look for if the root is a clamp
            ProtoVessel pv = vessel.protoVessel;
            if (pv.protoPartSnapshots.Count > 0 && pv.protoPartSnapshots[0].modules.Exists(m => m.moduleName == "LaunchClamp"))
            {
                //We look for the launchclamp module, which will hopefully cover FASA and stock.
                Debug.Log("[SR] Recovering a clamp!");
                //Add it to the recovered clamps list so we don't try to recover it again
                clampsRecovered.Add(vessel);
                float totalRefund = 0;
                //Loop over all the parts and calculate their cost (we recover at 100% since we're at the launchpad/runway)
                foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    float out1, out2;
                    totalRefund += ShipConstruction.GetPartCosts(pps, pps.partInfo, out out1, out out2);
                }
                //Add dem funds to da total. Get dem funds!
                AddFunds(totalRefund);
                //Fire the successful recovery event. Even though this isn't a stage we still need to do this for things like KCT to recover the parts. 
                //Can be averted with stock functions if I can get them working properly
                APIManager.instance.RecoverySuccessEvent.Fire(vessel, new float[] {100, totalRefund, 0}, "SUCCESS");
                //And then we try a bunch of things to make sure the clamps are removed (remove it from the flight state, kill it, and destroy it)
                HighLogic.CurrentGame.flightState.protoVessels.Remove(pv);
                vessel.Die();
                Destroy(vessel);
                //So, question for myself. Would it be better to try to manually fire the recovery events? Would that really be worth anything?
            }
        }


        //Small function to add funds to the game and write a log message about it.
        //Returns the new total.
        public static double AddFunds(double toAdd)
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                return 0;
            Funding.Instance.AddFunds(toAdd, TransactionReasons.VesselRecovery);
            Debug.Log("[SR] Adding funds: " + toAdd + ", New total: " + Funding.Instance.Funds);
            return (Funding.Instance.Funds);
        }


        public static int BuildingUpgradeLevel(SpaceCenterFacility facility)
        {
            int lvl = 0;
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Settings.instance.UseUpgrades)
            {
                lvl = (int)ScenarioUpgradeableFacilities.GetFacilityLevel(facility);
            }
            else
            {
                lvl = ScenarioUpgradeableFacilities.GetFacilityLevelCount(facility);
            }
            return lvl;
        }

        //Helper function that I found on StackExchange that helps immensly with dealing with Reflection. I'm not that good at reflection (accessing other mod's functions and data)
        public static object GetMemberInfoValue(System.Reflection.MemberInfo member, object sourceObject)
        {
            object newVal;
            if (member is System.Reflection.FieldInfo)
                newVal = ((System.Reflection.FieldInfo)member).GetValue(sourceObject);
            else
                newVal = ((System.Reflection.PropertyInfo)member).GetValue(sourceObject, null);
            return newVal;
        }

        //Check to see if FMRS is installed and enabled
        public static bool FMRS_Enabled()
        {
            try
            {
                Type FMRSType = AssemblyLoader.loadedAssemblies
                            .Select(a => a.assembly.GetExportedTypes())
                            .SelectMany(t => t)
                            .FirstOrDefault(t => t.FullName == "FMRS.FMRS_Util");
                if (FMRSType == null) return false;

                UnityEngine.Object FMRSUtilClass = GameObject.FindObjectOfType(FMRSType);
                bool enabled = (bool)GetMemberInfoValue(FMRSType.GetMember("_SETTING_Enabled")[0], FMRSUtilClass);
                if (enabled)
                    enabled = (bool)GetMemberInfoValue(FMRSType.GetMember("_SETTING_Armed")[0], FMRSUtilClass);

                return enabled;
            }
            catch
            {
                return false;
            }
        }

        //The main show. The VesselDestroyEvent is activated whenever KSP destroys a vessel. We only care about it in a specific set of circumstances
        private void VesselDestroyEvent(Vessel v)
        {
            //If we're disabled, just return
            if (!Settings.instance.SREnabled)
                return;

            if (!sceneChangeComplete)
                return;

            //If FlightGlobals is null, just return. We can't do anything
            if (FlightGlobals.fetch == null)
                return;

            //If the protoVessel is null, we can't do anything so just return
            if (v.protoVessel == null)
                return;

            if (FMRS_Enabled())
            {//If the vessel is controlled or has a RealChute Module, FMRS will handle it
                if ((v.protoVessel.wasControllable) || v.protoVessel.protoPartSnapshots.Find(p => p.modules != null && p.modules.Find(m => m.moduleName == "RealChuteModule") != null) != null)
                {
                    return;
                }
                //If there's crew onboard, FMRS will handle that too
                foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                {
                    if (pps.protoCrewNames.Count > 0)
                        return;
                }

                // if we've gotten here, FMRS probably isn't handling the craft and we should instead.
            }

            //Our criteria for even attempting recovery. Broken down: vessel exists, isn't the active vessel, is around Kerbin, is either unloaded or packed, altitude is less than 35km,
            //is flying or sub orbital, and is not an EVA (aka, Kerbals by themselves)
            if (v != null && !(HighLogic.LoadedSceneIsFlight && v.isActiveVessel) && v.mainBody.bodyName == "Kerbin" && (!v.loaded || v.packed) && Math.Exp(-v.altitude / (v.mainBody.atmosphereScaleHeight*1000)) >= 0.009 &&
               (v.situation == Vessel.Situations.FLYING || v.situation == Vessel.Situations.SUB_ORBITAL) && !v.isEVA && v.altitude > 100)
            {
                bool OnlyBlacklistedItems = true;
                foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                {
                    if (!Settings.instance.BlackList.Contains(pps.partInfo.title))
                    {
                        OnlyBlacklistedItems = false;
                        break;
                    }
                }
                if (OnlyBlacklistedItems) return;
                //Create a new RecoveryItem. Calling this calculates everything regarding the success or failure of the recovery. We need it for display purposes in the main gui
                RecoveryItem Stage = new RecoveryItem(v);
                //Fire the pertinent RecoveryEvent (success or failure). Aka, make the API do its work
                Stage.FireEvent();
                //Add the Stage to the correct list of stages. Either the Recovered Stages list or the Destroyed Stages list, for display on the main gui
                Stage.AddToList();
                //Post a message to the stock message system, if people are still using that.
                Stage.PostStockMessage();
            }
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