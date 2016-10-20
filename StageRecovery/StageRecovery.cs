using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

namespace StageRecovery
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class StageRecovery : MonoBehaviour
    {
        public static StageRecovery instance;
        //Flag that says whether the VesselDestroyEvent has been added, so we don't accidentally add it twice.
        private static bool eventAdded = false;
        private static bool sceneChangeComplete = false;

        private List<RecoveryItem> RecoveryQueue = new List<RecoveryItem>(); //Vessels added to this are pre-recovered
        private List<Guid> StageWatchList = new List<Guid>(); //Vessels added to this list are watched for pre-recovery
        private static Dictionary<Guid, double> RecoverAttemptLog = new Dictionary<Guid, double>(); //Vessel guid <-> UT at time of recovery. For checking for duplicates. UT is so we can clear if we revert. 
            //We persist this throughout a whole gaming session just so it isn't wiped out by scene changes


        private static double cutoffAlt = 23000;

        //List of scenes where we shouldn't run the mod. I toyed with runOnce, but couldn't get it working
        private static List<GameScenes> forbiddenScenes = new List<GameScenes> { GameScenes.LOADING, GameScenes.LOADINGBUFFER, GameScenes.CREDITS, GameScenes.MAINMENU, GameScenes.SETTINGS };

        //Needed to instantiate the Blizzy Toolbar button
        internal StageRecovery()
        {
            if (ToolbarManager.ToolbarAvailable && Settings.Instance != null && Settings.Instance.UseToolbarMod)
                Settings.Instance.gui.AddToolbarButton();
        }

        //Fired when the mod loads each scene
        public void Awake()
        {
            instance = this;

            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene))
                return;

            //Create a new Settings instance if one doesn't exist
            //if (Settings.Instance == null)
            //    Settings.Instance = new Settings();

            //Needed to start doing things with GUIs
            //RenderingManager.AddToPostDrawQueue(0, OnDraw);
        }

        private void OnGUI()
        {
            OnDraw();
        }

        //Also needed for GUIs. Not sure why, but this is how KCT was given to me so that's the method I use
        private void OnDraw()
        {
            if (Settings.Instance != null && Settings.Instance.gui != null)
                Settings.Instance.gui.SetGUIPositions(OnWindow);
        }

        //Once again, GUIs
        private void OnWindow(int windowID)
        {
            Settings.Instance.gui.DrawGUIs(windowID);
        }

        //When the scene changes and the mod is destroyed
        public void OnDestroy()
        {
            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene) || Settings.Instance == null || Settings.Instance.gui == null)
                return;

            //Remove the button from the stock toolbar
            if (Settings.Instance.gui.SRButtonStock != null)
                ApplicationLauncher.Instance.RemoveModApplication(Settings.Instance.gui.SRButtonStock);
            //Remove the button from Blizzy's toolbar
            if (Settings.Instance.gui.SRToolbarButton != null)
                Settings.Instance.gui.SRToolbarButton.Destroy();
        }

        //Fired when the mod loads each scene
        public void Start()
        {
            if (Settings.Instance != null)
                Settings.Instance.gui.hideAll();

            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene))
                return;

            //If the event hasn't been added yet, run this code (adds the event and the stock button)
            if (!eventAdded)
            {
                GameEvents.onGameSceneLoadRequested.Add(GameSceneLoadEvent);
                //Add the VesselDestroyEvent to the listeners
                //GameEvents.onVesselDestroy.Add(VesselDestroyEvent);
                GameEvents.onVesselWillDestroy.Add(VesselDestroyEvent);

                //Add the event that listens for unloads (for removing launch clamps)
                GameEvents.onVesselGoOnRails.Add(VesselUnloadEvent);
                //GameEvents..Add(DecoupleEvent);
                //If Blizzy's toolbar isn't available, use the stock one
                //if (!ToolbarManager.ToolbarAvailable)
                GameEvents.onGUIApplicationLauncherReady.Add(Settings.Instance.gui.OnGUIAppLauncherReady);

                cutoffAlt = ComputeCutoffAlt(Planetarium.fetch.Home, 0.01F)+100;
                Debug.Log("[SR] Determined cutoff altitude to be " + cutoffAlt);

                //Set the eventAdded flag to true so this code doesn't run again
                eventAdded = true;
            }
            //Load the settings from file
            Settings.Instance.Load();
            //Confine the RecoveryModifier to be between 0 and 1
            if (Settings.Instance.RecoveryModifier > 1) Settings.Instance.RecoveryModifier = 1;
            if (Settings.Instance.RecoveryModifier < 0) Settings.Instance.RecoveryModifier = 0;
            //Save the settings file (in case it doesn't exist yet). I suppose this is somewhat unnecessary if the file exists
            Settings.Instance.Save();

            //Load and resave the BlackList. The save ensures that the file will be created if it doesn't exist.
            Settings.Instance.BlackList.Load();
            Settings.Instance.BlackList.Save();

            if (!HighLogic.LoadedSceneIsFlight)
            {
                Settings.Instance.ClearStageLists();
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                foreach (Vessel v in FlightGlobals.Vessels)
                    WatchVessel(v);
            }

            //Remove anything that happens in the future
            List<Guid> removeList = new List<Guid>();
            double currentUT = Planetarium.GetUniversalTime();
            foreach (KeyValuePair<Guid, double> logItem in RecoverAttemptLog)
            {
                if (logItem.Value >= currentUT)
                {
                    removeList.Add(logItem.Key);
                }
            }
            foreach (Guid removeItem in removeList)
            {
                RecoverAttemptLog.Remove(removeItem);
            }
            //end future removal

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
            if (!Settings.Instance.SREnabled)
                return;

            //If the vessel or the protovessel are null then we surely can't do anything with them
            if (vessel == null || vessel.protoVessel == null)
                return;

            ProtoVessel pv = vessel.protoVessel;

            //If we aren't supposed to recover clamps, then don't try.
            if (Settings.Instance.RecoverClamps)
            {
                //If we've already recovered the clamps, then no need to try again
                if (clampsRecovered.Find(a => a.id == vessel.id) != null)
                    return;

                //Assign the pv variable to the protovessel, then look for if the root is a clamp
                
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
                    APIManager.instance.RecoverySuccessEvent.Fire(vessel, new float[] { 100, totalRefund, 0 }, "SUCCESS");
                    //And then we try a bunch of things to make sure the clamps are removed (remove it from the flight state, kill it, and destroy it)
                    HighLogic.CurrentGame.flightState.protoVessels.Remove(pv);
                    vessel.Die();
                    Destroy(vessel);
                    //So, question for myself. Would it be better to try to manually fire the recovery events? Would that really be worth anything?
                }
            }
            
            //If it's a stage that will be destroyed, we need to manually recover the Kerbals
            if (Settings.Instance.RecoverKerbals && pv.GetVesselCrew().Count > 0)
            {
                //Check if the conditions for vessel destruction are met
                if (vessel != FlightGlobals.ActiveVessel && !vessel.isEVA && vessel.mainBody == Planetarium.fetch.Home && pv.situation != Vessel.Situations.LANDED && vessel.atmDensity >= 0.01) //unloading in > 0.01 atm and not landed //pv.altitude < vessel.mainBody.atmosphereDepth
                {
                    Debug.Log("[SR] Vessel " + pv.vesselName + " is going to be destroyed. Recovering Kerbals!"); //Kerbal death should be handled by SR instead
                    RecoveryItem recItem = new RecoveryItem(vessel);

                    //Pre-recover the Kerbals
                    recItem.PreRecoverKerbals();

                    //Add the ship to the RecoveryQueue to be handled by the OnDestroy event
                    instance.RecoveryQueue.Add(recItem);
                }
                else
                    WatchVessel(vessel);
            }
        }

        public void FixedUpdate()
        {
            //For each vessel in the watchlist, check to see if it reaches an atm density of 0.01 and if so, pre-recover it
            foreach (Guid id in new List<Guid>(StageWatchList))
            {
                Vessel vessel = FlightGlobals.Vessels.Find(v => v.id == id);
                if (vessel == null)
                {
                    StageWatchList.Remove(id);
                    continue;
                }
                if ((!vessel.loaded || vessel.packed) && vessel.altitude < cutoffAlt)
                {
                    Debug.Log("[SR] Vessel " + vessel.vesselName + " (" + id + ") is about to be destroyed. Pre-recovering Kerbals.");
                    RecoveryItem recItem = new RecoveryItem(vessel);

                    //Pre-recover the Kerbals
                    recItem.PreRecoverKerbals();

                    //Add the ship to the RecoveryQueue to be handled by the VesselDestroy event
                    instance.RecoveryQueue.Add(recItem);

                   // Debug.Log("[SR] Current RecoveryQueue size: " + instance.RecoveryQueue.Count);

                    StageWatchList.Remove(id);
                }
            }
        }

        public static float ComputeCutoffAlt(CelestialBody body, float cutoffDensity, float stepSize=100)
        {
            //This unfortunately doesn't seem to be coming up with the right altitude for Kerbin (~23km, it finds ~27km)
            double dens = 0;
            float alt = (float)body.atmosphereDepth;
            while (alt > 0)
            {
                dens = body.GetDensity(FlightGlobals.getStaticPressure(alt, body), body.atmosphereTemperatureCurve.Evaluate(alt)); //body.atmospherePressureCurve.Evaluate(alt)
                //Debug.Log("[SR] Alt: " + alt + " Pres: " + dens);
                if (dens < cutoffDensity)
                    alt -= stepSize;
                else
                    break;
            }
            return alt;
        }

        public static bool WatchVessel(Vessel ves)
        {
            if (FMRS_Enabled()) //If FMRS is active then we don't watch any vessels
                return false;

            //If the vessel is around the home planet and the periapsis is below 23km, then we add it to the watch list
            if (ves != null && FlightGlobals.ActiveVessel != ves && ves.situation != Vessel.Situations.LANDED && ves.situation != Vessel.Situations.PRELAUNCH && ves.situation != Vessel.Situations.SPLASHED && ves.protoVessel.GetVesselCrew().Count > 0 && ves.orbit != null && ves.mainBody == Planetarium.fetch.Home && ves.orbit.PeA < cutoffAlt && !ves.isEVA)
            {
                if (instance.StageWatchList.Contains(ves.id))
                    return true;

                instance.StageWatchList.Add(ves.id);
                Debug.Log("[SR] Added vessel " + ves.vesselName + " (" + ves.id + ") to watchlist.");
                return true;
            }
            
            return false;
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
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Settings.Instance.UseUpgrades)
            {
                lvl = (int)(2 * ScenarioUpgradeableFacilities.GetFacilityLevel(facility));
            }
            else
            {
                //lvl = ScenarioUpgradeableFacilities.GetFacilityLevelCount(facility);
                lvl = 2;
            }
            return lvl;
        }

        //Function to estimate the final velocity given a stage's mass and parachute info
        public static double VelocityEstimate(double mass, double chuteAreaTimesCd)
        {
            if (chuteAreaTimesCd <= 0)
                return 200;
            if (mass <= 0)
                return 0;

            CelestialBody home = Planetarium.fetch.Home;

            return Math.Sqrt((2000 * mass * 9.81) / (home.GetDensity(home.GetPressure(0), home.GetTemperature(0)) * chuteAreaTimesCd));
            //This is according to the formulas used by Stupid_Chris in the Real Chute drag calculator program included with Real Chute. Source: https://github.com/StupidChris/RealChute/blob/master/Drag%20Calculator/RealChute%20drag%20calculator/RCDragCalc.cs

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
				Type FMRSType = null;
				AssemblyLoader.loadedAssemblies.TypeOperation(t =>
				{
					if (t.FullName == "FMRS.FMRS_Util")
					{
						FMRSType = t;
					}
				});
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
            if (!Settings.Instance.SREnabled)
                return;

            if (!sceneChangeComplete)
                return;

            //If FlightGlobals is null, just return. We can't do anything
            if (FlightGlobals.fetch == null)
                return;

            //If the protoVessel is null, we can't do anything so just return
            if (v.protoVessel == null)
                return;

            if (HighLogic.LoadedSceneIsFlight && FMRS_Enabled())
            {//If the vessel is controlled or has a RealChute Module, FMRS will handle it
                if ((v.protoVessel.wasControllable) || v.protoVessel.protoPartSnapshots.Find(p => p.modules != null && p.modules.Find(m => m.moduleName == "RealChuteModule") != null) != null || v.protoVessel.GetVesselCrew().Count > 0)
                {
                    return;
                }
                //If there's crew onboard, FMRS will handle that too
                // if we've gotten here, FMRS probably isn't handling the craft and we should instead.
            }

            //Our criteria for even attempting recovery. Broken down: vessel exists, hasn't had recovery attempted, isn't the active vessel, is around Kerbin, is either unloaded or packed, altitude is within atmosphere,
            //is flying or sub orbital, and is not an EVA (aka, Kerbals by themselves)
            if (v != null && !RecoverAttemptLog.ContainsKey(v.id) && !(HighLogic.LoadedSceneIsFlight && v.isActiveVessel) && (v.mainBody == Planetarium.fetch.Home) && (!v.loaded || v.packed) && (v.altitude < v.mainBody.atmosphereDepth) &&
               (v.situation == Vessel.Situations.FLYING || v.situation == Vessel.Situations.SUB_ORBITAL || v.situation == Vessel.Situations.ORBITING) && !v.isEVA && v.altitude > 100)
            {
                //Indicate that we've at least attempted recovery of this vessel
                RecoverAttemptLog.Add(v.id, Planetarium.GetUniversalTime());

                bool OnlyBlacklistedItems = true;
                foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                {
                    if (!Settings.Instance.BlackList.Contains(pps.partInfo.title))
                    {
                        OnlyBlacklistedItems = false;
                        break;
                    }
                }
                if (OnlyBlacklistedItems) return;

                //If we got this far, we can assume we're going to be attempting to recover the vessel, so we should fire the processing event
                APIManager.instance.OnRecoveryProcessingStart.Fire(v);

                //Create a new RecoveryItem. Calling this calculates everything regarding the success or failure of the recovery. We need it for display purposes in the main gui
                Debug.Log("[SR] Searching in RecoveryQueue (" + instance.RecoveryQueue.Count + ") for " + v.id);
                RecoveryItem Stage;
                if (instance.RecoveryQueue.Count > 0 && instance.RecoveryQueue.Exists(ri => ri.vessel.id == v.id))
                {
                    Stage = instance.RecoveryQueue.Find(ri => ri.vessel.id == v.id);
                    instance.RecoveryQueue.Remove(Stage);
                    Debug.Log("[SR] Found vessel in the RecoveryQueue.");
                }
                else
                {
                    Stage = new RecoveryItem(v);
                }
                Stage.Process();
                //Fire the pertinent RecoveryEvent (success or failure). Aka, make the API do its work
                Stage.FireEvent();
                //Add the Stage to the correct list of stages. Either the Recovered Stages list or the Destroyed Stages list, for display on the main gui
                Stage.AddToList();
                //Post a message to the stock message system, if people are still using that.
                Stage.PostStockMessage();

                APIManager.instance.OnRecoveryProcessingFinish.Fire(v);
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