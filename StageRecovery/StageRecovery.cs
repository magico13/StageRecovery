using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

namespace StageRecovery
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
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
        

        //Fired when the mod loads each scene
        public void Awake()
        {
            Debug.Log("[SR] Awake Start");
            instance = this;

            //Needed to instantiate the Blizzy Toolbar button
            if (ToolbarManager.ToolbarAvailable && Settings.Instance != null && Settings.Instance.UseToolbarMod)
            {
                Settings.Instance.gui.AddToolbarButton();
            }

            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene))
            {
                return;
            }
        }

        private void OnGUI()
        {
            if (Settings.Instance != null && Settings.Instance.gui != null)
            {
                Settings.Instance.gui.SetGUIPositions(Settings.Instance.gui.DrawGUIs);
            }
        }

        //When the scene changes and the mod is destroyed
        public void OnDestroy()
        {
            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene) || Settings.Instance == null || Settings.Instance.gui == null)
            {
                return;
            }

            //Remove the button from the stock toolbar
            if (Settings.Instance.gui.SRButtonStock != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(Settings.Instance.gui.SRButtonStock);
            }
            //Remove the button from Blizzy's toolbar
            if (Settings.Instance.gui.SRToolbarButton != null)
            {
                Settings.Instance.gui.SRToolbarButton.Destroy();
            }
        }

        //Fired when the mod loads each scene
        public void Start()
        {
            Debug.Log("[SR] Start start");
            if (Settings.Instance != null)
            {
                Settings.Instance.gui.hideAll();
            }

            //If we're in the MainMenu, don't do anything
            if (forbiddenScenes.Contains(HighLogic.LoadedScene))
            {
                return;
            }

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

                cutoffAlt = ComputeCutoffAlt(Planetarium.fetch.Home)+1000;
                Debug.Log("[SR] Determined cutoff altitude to be " + cutoffAlt);

                //Register with the RecoveryController (do we only do this once?)
                Debug.Log("[SR] RecoveryController registration success: " + RecoveryControllerWrapper.RegisterMod("StageRecovery"));

                //Set the eventAdded flag to true so this code doesn't run again
                eventAdded = true;
            }
            //Load the settings from file
            Settings.Instance.Load();
            //Confine the RecoveryModifier to be between 0 and 1
            if (Settings.Instance.RecoveryModifier > 1)
            {
                Settings.Instance.RecoveryModifier = 1;
            }

            if (Settings.Instance.RecoveryModifier < 0)
            {
                Settings.Instance.RecoveryModifier = 0;
            }
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
                {
                    TryWatchVessel(v);
                }
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

        public void GameSceneLoadEvent(GameScenes newScene)
        {
            sceneChangeComplete = false;
            if (newScene != GameScenes.FLIGHT)
            {
                clampsRecovered.Clear();
            }
        }

        private List<Vessel> clampsRecovered = new List<Vessel>();
        public void VesselUnloadEvent(Vessel vessel)
        {
            //If we're disabled, just return
            if (!Settings.Instance.SREnabled)
            {
                return;
            }

            //If the vessel or the protovessel are null then we surely can't do anything with them
            if (vessel == null || vessel.protoVessel == null)
            {
                return;
            }

            ProtoVessel pv = vessel.protoVessel;

            //If we aren't supposed to recover clamps, then don't try.
            if (Settings.Instance.RecoverClamps)
            {
                //If we've already recovered the clamps, then no need to try again
                if (clampsRecovered.Find(a => a.id == vessel.id) != null)
                {
                    return;
                }

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
            if (Settings.Instance.RecoverKerbals && Settings.Instance.PreRecover && pv.GetVesselCrew().Count > 0)
            {
                //Check if the conditions for vessel destruction are met
                if (vessel != FlightGlobals.ActiveVessel && !vessel.isEVA && vessel.mainBody == Planetarium.fetch.Home 
                    && pv.situation != Vessel.Situations.LANDED && vessel.altitude < cutoffAlt && vessel.altitude > 0
                    && (FlightGlobals.ActiveVessel.transform.position - vessel.transform.position).sqrMagnitude > Math.Pow(vessel.vesselRanges.GetSituationRanges(Vessel.Situations.FLYING).pack, 2) - 250)
                {
                    Debug.Log("[SR] Vessel " + pv.vesselName + " is going to be destroyed. Pre-recovering!"); //Kerbal death should be handled by SR instead

                    RecoverVessel(vessel, true);
                }
                else
                {
                    TryWatchVessel(vessel);
                }
            }
        }

        public void FixedUpdate()
        {
            if (!sceneChangeComplete)
            {
                return;
            }
            //For each vessel in the watchlist, check to see if it reaches an atm density of 0.01 and if so, pre-recover it
            foreach (Guid id in new List<Guid>(StageWatchList))
            {
                Vessel vessel = FlightGlobals.Vessels.Find(v => v.id == id);
                if (vessel == null)
                {
                    StageWatchList.Remove(id);
                    continue;
                }
                if ((!vessel.loaded || vessel.packed) && vessel.mainBody == Planetarium.fetch.Home && vessel.altitude < cutoffAlt && vessel.altitude > 0 
                    && (FlightGlobals.ActiveVessel.transform.position - vessel.transform.position).sqrMagnitude > Math.Pow(vessel.vesselRanges.GetSituationRanges(Vessel.Situations.FLYING).pack, 2)-250)
                {
                    if (!SRShouldRecover(vessel))
                    {
                        StageWatchList.Remove(id);
                        continue;
                    }
                    Debug.Log($"[SR] Vessel {vessel.vesselName} ({id}) is about to be destroyed at altitude {vessel.altitude}. Pre-recovering vessel.");

                    RecoverVessel(vessel, true);

                    StageWatchList.Remove(id);
                }
            }
        }

        public static float ComputeCutoffAlt(CelestialBody body, float stepSize=100)
        {
            float alt = (float)body.atmosphereDepth;
            while (alt > 0)
            {
                double pres = body.GetPressure(alt);
                if (pres < 1.0)
                {
                    alt -= stepSize;
                }
                else
                {
                    break;
                }
            }
            return alt;
        }

        public static bool TryWatchVessel(Vessel ves)
        {
            if (FMRS_Enabled(false)) //If FMRS is active then we don't watch any vessels (we don't care if it's watching for chutes at all, we just need to know if it's on)
            {
                return false;
            }

            //If the vessel is around the home planet and the periapsis is below 23km, then we add it to the watch list
            //must have crew as well
            if (ves != null && FlightGlobals.ActiveVessel != ves && ves.situation != Vessel.Situations.LANDED 
                && ves.situation != Vessel.Situations.PRELAUNCH && ves.situation != Vessel.Situations.SPLASHED 
                && ves.protoVessel.GetVesselCrew().Count > 0 && ves.orbit != null && ves.mainBody == Planetarium.fetch.Home 
                && ves.orbit.PeA < cutoffAlt && !ves.isEVA && ves.altitude > 0)
            {
                if (instance.StageWatchList.Contains(ves.id))
                {
                    return true;
                }

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
            {
                return 0;
            }

            Funding.Instance.AddFunds(toAdd, TransactionReasons.VesselRecovery);
            Debug.Log("[SR] Adding funds: " + toAdd + ", New total: " + Funding.Instance.Funds);
            return (Funding.Instance.Funds);
        }

        public static int BuildingUpgradeLevel(SpaceCenterFacility facility)
        {
            int lvl = 0;
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Settings.Instance.UseUpgrades)
            {
                lvl = (int)Math.Round((ScenarioUpgradeableFacilities.GetFacilityLevelCount(facility) * ScenarioUpgradeableFacilities.GetFacilityLevel(facility)));
            }
            else
            {
                lvl = ScenarioUpgradeableFacilities.GetFacilityLevelCount(facility); //returns 2 for VAB in Sandbox
            }
            return lvl;
        }

        //Function to estimate the final velocity given a stage's mass and parachute info
        public static double VelocityEstimate(double mass, double chuteAreaTimesCd)
        {
            if (chuteAreaTimesCd <= 0)
            {
                return 200;
            }

            if (mass <= 0)
            {
                return 0;
            }

            CelestialBody home = Planetarium.fetch.Home;

            return Math.Sqrt((2000 * mass * 9.81) / (home.GetDensity(home.GetPressure(0), home.GetTemperature(0)) * chuteAreaTimesCd));
            //This is according to the formulas used by Stupid_Chris in the Real Chute drag calculator program included with Real Chute. Source: https://github.com/StupidChris/RealChute/blob/master/Drag%20Calculator/RealChute%20drag%20calculator/RCDragCalc.cs

        }

        //Helper function that I found on StackExchange that helps immensly with dealing with Reflection. I'm not that good at reflection (accessing other mod's functions and data)
        public static object GetMemberInfoValue(System.Reflection.MemberInfo member, object sourceObject)
        {
            object newVal;
            if (member is System.Reflection.FieldInfo)
            {
                newVal = ((System.Reflection.FieldInfo)member).GetValue(sourceObject);
            }
            else
            {
                newVal = ((System.Reflection.PropertyInfo)member).GetValue(sourceObject, null);
            }

            return newVal;
        }

        /// <summary>
        /// Check to see if FMRS is installed and enabled
        /// </summary>
        /// <param name="parachuteSetting">If true, check if the defer parachutes to SR is set (and enabled). Otherwise return only the enabled state.
        /// Defaults to true.</param>

        public static bool FMRS_Enabled(bool parachuteSetting=true)
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
                if (FMRSType == null)
                {
                    return false;
                }

                UnityEngine.Object FMRSUtilClass = GameObject.FindObjectOfType(FMRSType);
                bool enabled = (bool)GetMemberInfoValue(FMRSType.GetMember("_SETTING_Enabled")[0], FMRSUtilClass);
                if (enabled)
                {
                    enabled = (bool)GetMemberInfoValue(FMRSType.GetMember("_SETTING_Armed")[0], FMRSUtilClass);
                }

                //if we are checking the parachute setting is set
                if (enabled && parachuteSetting)
                {
                    enabled = (bool)GetMemberInfoValue(FMRSType.GetMember("_SETTING_Parachutes")[0], null); //this setting is a static
                    if (enabled)
                    {
                        enabled = !(bool)GetMemberInfoValue(FMRSType.GetMember("_SETTING_Defer_Parachutes_to_StageRecovery")[0], null); //this setting is a static
                        //we "not" it because if they're deferring to us then it's the same as them being disabled (when not considering crew or probes)
                    }
                }

                return enabled;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        //The main show. The VesselDestroyEvent is activated whenever KSP destroys a vessel. We only care about it in a specific set of circumstances
        private void VesselDestroyEvent(Vessel v)
        {
            //If we're disabled, just return
            if (!Settings.Instance.SREnabled)
            {
                return;
            }

            if (!sceneChangeComplete)
            {
                return;
            }

            //If FlightGlobals is null, just return. We can't do anything
            if (FlightGlobals.fetch == null)
            {
                return;
            }

            //If the protoVessel is null, we can't do anything so just return
            if (v.protoVessel == null)
            {
                return;
            }

            //Check if we should even recover it
            if (!SRShouldRecover(v))
            {
                return;
            }

            //Our criteria for even attempting recovery. Broken down: vessel exists, hasn't had recovery attempted, isn't the active vessel, is around Kerbin, is either unloaded or packed, altitude is within atmosphere,
            //is flying or sub orbital, and is not an EVA (aka, Kerbals by themselves)
            if (v != null && !RecoverAttemptLog.ContainsKey(v.id) && !(HighLogic.LoadedSceneIsFlight && v.isActiveVessel) && (v.mainBody == Planetarium.fetch.Home) && (!v.loaded || v.packed) && (v.altitude < v.mainBody.atmosphereDepth) &&
               (v.situation == Vessel.Situations.FLYING || v.situation == Vessel.Situations.SUB_ORBITAL || v.situation == Vessel.Situations.ORBITING) && !v.isEVA && v.altitude > 100)
            {
                RecoverVessel(v, false);
            }
        }

        private static void RecoverVessel(Vessel v, bool preRecovery)
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
            if (OnlyBlacklistedItems)
            {
                return;
            }

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
            Stage.Process(preRecovery);
            //Fire the pertinent RecoveryEvent (success or failure). Aka, make the API do its work
            Stage.FireEvent();
            //Add the Stage to the correct list of stages. Either the Recovered Stages list or the Destroyed Stages list, for display on the main gui
            Stage.AddToList();
            //Post a message to the stock message system, if people are still using that.
            Stage.PostStockMessage();
            //Remove all crew on the vessel
            Stage.RemoveCrew();

            if (preRecovery)
            {
                //remove the vessel so it doesn't get destroyed
                v.Die();
            }

            //Fire the event stating we are done processing
            APIManager.instance.OnRecoveryProcessingFinish.Fire(v);
        }

        public static double ProcessPartList(List<Part> vesselParts)
        {
            double totalMass = 0;
            vesselParts.ForEach(p => totalMass += p.mass + p.GetResourceMass());
            double chuteArea = GetChuteArea(vesselParts);
            return VelocityEstimate(totalMass, chuteArea);
        }

        public static double ProcessPartList(List<ProtoPartSnapshot> vesselParts)
        {
            double totalMass = 0;
            vesselParts.ForEach(p => totalMass += p.mass + GetResourceMass(p.resources));
            double chuteArea = GetChuteArea(vesselParts);
            return VelocityEstimate(totalMass, chuteArea);
        }

        public static double GetChuteArea(List<ProtoPartSnapshot> protoParts)
        {
            double RCParameter = 0;
            bool realChuteInUse = false;
            try
            {
                foreach (ProtoPartSnapshot p in protoParts)
                {
                    if (p.modules.Exists(ppms => ppms.moduleName == "RealChuteModule"))
                    {
                        if (!realChuteInUse)
                        {
                            RCParameter = 0;
                        }
                        //First off, get the PPMS since we'll need that
                        ProtoPartModuleSnapshot realChute = p.modules.First(mod => mod.moduleName == "RealChuteModule");
                        //Assuming that's not somehow null, then we continue
                        if (realChute != null) //Some of this was adopted from DebRefund, as Vendan's method of handling multiple parachutes is better than what I had.
                        {
                            //We make a list of ConfigNodes containing the parachutes (usually 1, but now there can be any number of them)
                            //We get that from the PPMS 
                            ConfigNode rcNode = new ConfigNode();
                            realChute.Save(rcNode);

                            //It's existence means that RealChute is installed and in use on the craft (you could have it installed and use stock chutes, so we only check if it's on the craft)
                            realChuteInUse = true;

                            RCParameter += ProcessRealchute(rcNode);
                        }
                    }
                    else if (p.modules.Exists( ppms => ppms.moduleName == "RealChuteFAR")) //RealChute Lite for FAR
                    {
                        if (!realChuteInUse)
                        {
                            RCParameter = 0;
                        }

                        ProtoPartModuleSnapshot realChute = p.modules.First(mod => mod.moduleName == "RealChuteFAR");
                        float diameter = 0.0F; //realChute.moduleValues.GetValue("deployedDiameter")

                        if (realChute.moduleRef != null)
                        {
                            try
                            {
                                diameter = realChute.moduleRef.Fields.GetValue<float>("deployedDiameter");
                                Debug.Log($"[SR] Diameter is {diameter}.");
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("[SR] Exception while finding deployedDiameter for RealChuteFAR module on moduleRef.");
                                Debug.LogException(e);
                            }
                        }
                        else
                        {

                            Debug.Log("[SR] moduleRef is null, attempting workaround to find diameter.");
                            object dDefault = p.partInfo.partPrefab.Modules["RealChuteFAR"]?.Fields?.GetValue("deployedDiameter"); //requires C# 6
                            if (dDefault != null)
                            {
                                diameter = Convert.ToSingle(dDefault);
                                Debug.Log($"[SR] Workaround gave a diameter of {diameter}.");
                            }
                            else
                            {
                                Debug.Log("[SR] Couldn't get default value, setting to 0 and calling it a day.");
                                diameter = 0.0F;
                            }

                        }
                        float dragC = 1.0f; //float.Parse(realChute.moduleValues.GetValue("staticCd"));
                        RCParameter += (dragC * Mathf.Pow(diameter, 2) * Math.PI / 4.0);

                        realChuteInUse = true;
                    }
                    else if (!realChuteInUse && p.modules.Exists(ppms => ppms.moduleName == "ModuleParachute"))
                    {
                        //Credit to m4v and RCSBuildAid: https://github.com/m4v/RCSBuildAid/blob/master/Plugin/CoDMarker.cs
                        Part part = p.partRef ?? p.partPrefab; //the part reference, or the part prefab
                        DragCubeList dragCubes = part.DragCubes;
                        dragCubes.SetCubeWeight("DEPLOYED", 1);
                        dragCubes.SetCubeWeight("SEMIDEPLOYED", 0);
                        dragCubes.SetCubeWeight("PACKED", 0);
                        dragCubes.SetOcclusionMultiplier(0);
                        Quaternion rotation = Quaternion.LookRotation(Vector3d.up);
                        try
                        {
                            rotation = Quaternion.LookRotation(part.partTransform?.InverseTransformDirection(Vector3d.up) ?? Vector3d.up);
                        }
                        catch (Exception)
                        {
                            //Debug.LogException(e);
                        }
                        dragCubes.SetDragVectorRotation(rotation);
                    }
                    if (!realChuteInUse)
                    {
                        Part part = p.partRef ?? p.partPrefab; //the part reference, or the part prefab
                        DragCubeList dragCubes = part.DragCubes;
                        dragCubes.ForceUpdate(false, true);
                        dragCubes.SetDragWeights();
                        dragCubes.SetPartOcclusion();

                        Vector3 dir = Vector3d.up;
                        dragCubes.SetDrag(dir, 0.03f); //mach 0.03, or about 10m/s

                        double dragCoeff = dragCubes.AreaDrag * PhysicsGlobals.DragCubeMultiplier;

                        RCParameter += (dragCoeff * PhysicsGlobals.DragMultiplier);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[SR] Error occured while trying to determine total chute area.");
                Debug.LogException(e);
            }
            return RCParameter;
        }

        public static double GetChuteArea(List<Part> parts)
        {
            double RCParameter = 0;
            bool realChuteInUse = false;
            try
            {
                foreach (Part p in parts)
                {
                    //Make a list of all the Module Names for easy checking later. This can be avoided, but is convenient.
                    List<string> ModuleNames = new List<string>();
                    foreach (PartModule pm in p.Modules)
                    {
                        ModuleNames.Add(pm.moduleName);
                    }

                    if (ModuleNames.Contains("RealChuteModule"))
                    {
                        if (!realChuteInUse)
                        {
                            RCParameter = 0;
                        }
                        //First off, get the PPMS since we'll need that
                        PartModule realChute = p.Modules["RealChuteModule"];
                        //Assuming that's not somehow null, then we continue
                        if (realChute != null) //Some of this was adopted from DebRefund, as Vendan's method of handling multiple parachutes is better than what I had.
                        {
                            //We make a list of ConfigNodes containing the parachutes (usually 1, but now there can be any number of them)
                            //We get that from the PPMS 
                            ConfigNode rcNode = new ConfigNode();
                            realChute.Save(rcNode);
                            
                            //It's existence means that RealChute is installed and in use on the craft (you could have it installed and use stock chutes, so we only check if it's on the craft)
                            realChuteInUse = true;

                            RCParameter += ProcessRealchute(rcNode);
                        }
                    }
                    else if (ModuleNames.Contains("RealChuteFAR")) //RealChute Lite for FAR
                    {
                        if (!realChuteInUse)
                        {
                            RCParameter = 0;
                        }

                        PartModule realChute = p.Modules["RealChuteFAR"];
                        float diameter = 0.0F; //realChute.moduleValues.GetValue("deployedDiameter")

                        if (realChute != null)
                        {
                            try
                            {
                                diameter = realChute.Fields.GetValue<float>("deployedDiameter");
                                Debug.Log($"[SR] Diameter is {diameter}.");
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("[SR] Exception while finding deployedDiameter for RealChuteFAR module on module.");
                                Debug.LogException(e);
                            }
                        }
                        else
                        {

                            Debug.Log("[SR] moduleRef is null, attempting workaround to find diameter.");
                            object dDefault = p.partInfo.partPrefab.Modules["RealChuteFAR"]?.Fields?.GetValue("deployedDiameter"); //requires C# 6
                            if (dDefault != null)
                            {
                                diameter = Convert.ToSingle(dDefault);
                                Debug.Log($"[SR] Workaround gave a diameter of {diameter}.");
                            }
                            else
                            {
                                Debug.Log("[SR] Couldn't get default value, setting to 0 and calling it a day.");
                                diameter = 0.0F;
                            }

                        }
                        float dragC = 1.0f; //float.Parse(realChute.moduleValues.GetValue("staticCd"));
                        RCParameter += (dragC * Mathf.Pow(diameter, 2) * Math.PI / 4.0);

                        realChuteInUse = true;
                    }
                    else if (!realChuteInUse && ModuleNames.Contains("ModuleParachute"))
                    {
                        //Credit to m4v and RCSBuildAid: https://github.com/m4v/RCSBuildAid/blob/master/Plugin/CoDMarker.cs
                        Part part = p ?? p.partInfo.partPrefab; //the part, or the part prefab
                        DragCubeList dragCubes = part.DragCubes;
                        dragCubes.SetCubeWeight("DEPLOYED", 1);
                        dragCubes.SetCubeWeight("SEMIDEPLOYED", 0);
                        dragCubes.SetCubeWeight("PACKED", 0);
                        dragCubes.SetOcclusionMultiplier(0);
                        Quaternion rotation = Quaternion.LookRotation(Vector3d.up);
                        try
                        {
                            rotation = Quaternion.LookRotation(part.partTransform?.InverseTransformDirection(Vector3d.up) ?? Vector3d.up);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                        dragCubes.SetDragVectorRotation(rotation);
                    }
                    if (!realChuteInUse)
                    {
                        Part part = p ?? p.partInfo.partPrefab; //the part reference, or the part prefab
                        DragCubeList dragCubes = part.DragCubes;
                        dragCubes.ForceUpdate(false, true);
                        dragCubes.SetDragWeights();
                        dragCubes.SetPartOcclusion();

                        Vector3 dir = Vector3d.up;
                        try
                        {
                            dir = -part.partTransform?.InverseTransformDirection(Vector3d.down) ?? Vector3d.up;
                        }
                        catch (Exception e)
                        {
                            //Debug.LogException(e);
                            Debug.Log("[SR] The expected excpetion is still present. "+e.Message);
                        }
                        dragCubes.SetDrag(dir, 0.03f); //mach 0.03, or about 10m/s

                        double dragCoeff = dragCubes.AreaDrag * PhysicsGlobals.DragCubeMultiplier;

                        RCParameter += (dragCoeff * PhysicsGlobals.DragMultiplier);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[SR] Error occured while trying to determine total chute area.");
                Debug.LogException(e);
            }
            return RCParameter;
        }

        private static double GetResourceMass(List<ProtoPartResourceSnapshot> resources)
        {
            double mass = 0;
            //Loop through the available resources
            foreach (ProtoPartResourceSnapshot resource in resources)
            {
                //Extract the amount information
                double amount = resource.amount;
                //Using the name of the resource, find it in the PartResourceLibrary
                PartResourceDefinition RD = PartResourceLibrary.Instance.GetDefinition(resource.resourceName);
                //The mass of that resource is the amount times the density
                mass += amount * RD.density;
            }
            //Return the total mass
            return mass;
        }

        private static double ProcessRealchute(ConfigNode node)
        {
            double RCParameter = 0;

            //This is where the Reflection starts. We need to access the material library that RealChute has, so we first grab it's Type
            Type matLibraryType = null;
            AssemblyLoader.loadedAssemblies.TypeOperation(t =>
            {
                if (t.FullName == "RealChute.Libraries.MaterialsLibrary.MaterialsLibrary")
                {
                    matLibraryType = t;
                }
            });


            ConfigNode[] parachutes = node.GetNodes("PARACHUTE");
            //We then act on each individual parachute in the module
            foreach (ConfigNode chute in parachutes)
            {
                //First off, the diameter of the parachute. From that we can (later) determine the Vt, assuming a circular chute
                float diameter = float.Parse(chute.GetValue("deployedDiameter"));
                //The name of the material the chute is made of. We need this to get the actual material object and then the drag coefficient
                string mat = chute.GetValue("material");

                //This grabs the method that RealChute uses to get the material. We will invoke that with the name of the material from before.
                System.Reflection.MethodInfo matMethod = matLibraryType.GetMethod("GetMaterial", new Type[] { typeof(string) });
                //In order to invoke the method, we need to grab the active instance of the material library
                object MatLibraryInstance = matLibraryType.GetProperty("Instance").GetValue(null, null);

                //With the library instance we can invoke the GetMaterial method (passing the name of the material as a parameter) to receive an object that is the material
                object materialObject = matMethod.Invoke(MatLibraryInstance, new object[] { mat });
                //With that material object we can extract the dragCoefficient using the helper function above.
                float dragC = Convert.ToSingle(GetMemberInfoValue(materialObject.GetType().GetMember("DragCoefficient")[0], materialObject));
                //Now we calculate the RCParameter. Simple addition of this doesn't result in perfect results for Vt with parachutes with different diameter or drag coefficients
                //But it works perfectly for multiple identical parachutes (the normal case)
                RCParameter += (dragC * Mathf.Pow(diameter, 2) * Math.PI / 4.0);

            }
            return RCParameter;
        }

        /// <summary>
        /// Checks whether StageRecovery should recover the vessel
        /// </summary>
        /// <param name="vessel">The vessel to check</param>
        /// <returns>True if SR should handle it, false otherwise</returns>
        private static bool SRShouldRecover(Vessel vessel)
        {
            //Check if the stage was claimed by another mod
            string controllingMod = RecoveryControllerWrapper.ControllingMod(vessel);
            Debug.Log("[SR] Controlling mod is " + (controllingMod ?? "null"));
            if (HighLogic.LoadedSceneIsFlight) //outside of the flight scene we're gonna handle everything
            {
                if (string.IsNullOrEmpty(controllingMod) || string.Equals(controllingMod, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (FMRS_Enabled(false))
                    { //FMRS is installed and is active, but we aren't sure if they're handling chutes yet
                        Debug.Log("[SR] FMRS is active...");
                        if (!FMRS_Enabled(true))
                        { //FMRS is active, but isn't handling parachutes or deferred it to us. So if there isn't crew or a form of control, then we handle it
                            Debug.Log("[SR] But FMRS isn't handling chutes...");
                            if ((vessel.protoVessel.wasControllable) || vessel.protoVessel.GetVesselCrew().Count > 0)
                            { //crewed or was controlled, so FMRS will get it
                                Debug.Log("[SR] But this stage has control/kerbals, so have fun FMRS!");
                                return false;
                            }
                            Debug.Log("[SR] So we've got this stage! Maybe next time FMRS.");
                            // if we've gotten here, FMRS probably isn't handling the craft and we should instead.
                        }
                        else
                        { //FRMS is active, is handling chutes, and hasn't deferred it to us. We aren't gonna handle this case at all
                            Debug.Log("[SR] And FMRS is handling everything, have fun!");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.Log("[SR] FMRS is not active.");
                    }
                }
                else if (string.Equals(controllingMod, "StageRecovery", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[SR] Vessel specified StageRecovery as its processor.");
                    return true;
                }
                else //another mod has requested full control over recovery of the vessel
                {
                    Debug.Log($"[SR] Vessel specified '{controllingMod}' as its processor.");
                    return false;
                }
            }
            return true;
        }
    }
}

/*
Copyright (C) 2017  Michael Marvin

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