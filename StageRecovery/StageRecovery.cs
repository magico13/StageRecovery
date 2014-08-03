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

        //Needed to instantiate the Blizzy Toolbar button
        internal StageRecovery()
        {
            if (ToolbarManager.ToolbarAvailable)
                Settings.instance.gui.AddToolbarButton();
        }

        //Fired when the mod loads each scene
        public void Awake()
        {
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

        //When the scene changes and the mod destroyed
        public void OnDestroy()
        {
            //Remove the button from the stock toolbar
            if (Settings.instance.gui.SRButtonStock != null)
                ApplicationLauncher.Instance.RemoveModApplication(Settings.instance.gui.SRButtonStock);
            //Remove the button from Blizzy's toolbar
            if (Settings.instance.gui.SRToolbarButton != null)
                Settings.instance.gui.SRToolbarButton.Destroy();
        }

        //Fired when the mod loads
        public void Start()
        {
            //If we're in the MainMenu, don't do anything
            if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                return;

            //If the event hasn't been added yet, run this code (adds the event and the stock button)
            if (!eventAdded)
            {
                //Add the VesselDestroyEvent to the listeners
                GameEvents.onVesselDestroy.Add(VesselDestroyEvent);
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
        }

        //Small function to add funds to the game and write a log message about it.
        //Returns the new total.
        public double AddFunds(double toAdd)
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                return 0;
            Debug.Log("[SR] Adding funds: " + toAdd + ", New total: " + (Funding.Instance.Funds + toAdd));
            return (Funding.Instance.Funds += toAdd);
        }

        //This determines what the recovery percentage should be and calculates all of the returns (per part too)
        //This does a lot more than it was originally supposed to and I should probably rewrite some of it.
        public float GetRecoveryValueForParachutes(Vessel v, StringBuilder msg, float Vt)
        {
            //The protovessel is suprisingly more useful than the vessel (for instance, the Vessel.parts is null when unloaded, but the ProtoVessel.protoPartSnapshots are not null)
            ProtoVessel pv = v.protoVessel;
            
            //Holders for the number of parts recovered (name:qty) and each one's modified costs (name:cost)
            Dictionary<string, int> PartsRecovered = new Dictionary<string, int>();
            Dictionary<string, float> Costs = new Dictionary<string, float>();
            //Holders for the total returns for fuel and for parts
            float FuelReturns = 0, DryReturns = 0;
            //Holder for the recovery modifier, an additional percentage of the distance percentage (from speed losses)
            float RecoveryMod = 0;
            //If we're using the Flat Rate model then we need to check for control
            if (Settings.instance.FlatRateModel)
            {
                //Assume uncontrolled until proven controlled
                bool stageControllable = false;
                //Cycle through all of the parts on the ship (well, ProtoPartSnaphsots)
                foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    //Search through the Modules on the part for one called ModuleCommand and check if the crew count in the part is greater than or equal to the minimum required for control
                    if (pps.modules.Find(module => (module.moduleName == "ModuleCommand" && ((ModuleCommand)module.moduleRef).minimumCrew <= pps.protoModuleCrew.Count)) != null)
                    {
                        Debug.Log("[SR] Stage is controlled!");
                        //Congrats, the stage is controlled! We can stop looking now.
                        stageControllable = true;
                        break;
                    }
                }
                //This is a fun trick for one-liners. The RecoveryMod is equal to 1 if stageControllable==true or the RecoveryModifier saved in the settings if that's false.
                RecoveryMod = stageControllable ? 1.0f : Settings.instance.RecoveryModifier;
            }
            //If we're not using Flat Rate (thus using Variable Rate) then we have to do a bit more work to get the RecoveryMod
            else
                RecoveryMod = GetVariableRecoveryValue(Vt);

            //Calculate the distance from KSC in meters
            double distanceFromKSC = SpaceCenter.Instance.GreatCircleDistance(SpaceCenter.Instance.cb.GetRelSurfaceNVector(pv.latitude, pv.longitude));
            //Calculate the max distance from KSC (half way around a circle the size of Kerbin)
            double maxDist = SpaceCenter.Instance.cb.Radius * Math.PI;
            //Get the reduction in returns due to distance (1 at KSC, .1 at maxDist supposedly)
            float distancePercent = Mathf.Lerp(0.98f, 0.1f, (float)(distanceFromKSC / maxDist));
            //Combine the modifier from the velocity and the modifier from distance together
            float recoveryPercent = RecoveryMod * distancePercent;
            //Holder for the total funds returned (hmm, that's just the FuelReturns+DryReturns, so it isn't strictly necessary
            float totalReturn = 0;
            //Cycle through all the parts again
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                //Holders for the "out" below
                float dryCost, fuelCost;
                //Stock function for taking a ProtoPartSnapshot and the corresponding AvailablePart (aka, partInfo) and determining the value 
                //of the fuel contained and base part. Whole thing returns the combined total, but we'll do that manually
                ShipConstruction.GetPartCosts(pps, pps.partInfo, out dryCost, out fuelCost);
                //Set the dryCost to 0 if it's less than 0 (also could be done with dryCost = Math.Max(0, dyrCost);)
                dryCost = dryCost < 0 ? 0 : dryCost;
                //Same for the fuelCost
                fuelCost = fuelCost < 0 ? 0 : fuelCost;
                //The totalReturn (before recovery modifiers) is the sum of the current totalReturn plus the part cost and fuel cost
                totalReturn += dryCost + fuelCost;

                //FuelReturns gets the fuelCost modified by the recoveryPercent added to it
                FuelReturns += fuelCost*recoveryPercent;
                //Repeat for the DryReturns
                DryReturns += dryCost*recoveryPercent;
                
                //Now we add the parts to the Dictionaries for display later
                //If the part title (the nice common name, like "Command Pod Mk1" as opposed to the name which is "mk1pod") isn't in the dictionary, add a new element
                if (!PartsRecovered.ContainsKey(pps.partInfo.title))
                {
                    //Add the title and qty=1 to the PartsRecovered
                    PartsRecovered.Add(pps.partInfo.title, 1);
                    //And the title and modified dryCost to the Costs
                    Costs.Add(pps.partInfo.title, dryCost*recoveryPercent);
                }
                else
                {
                    //If it is in the dictionary already, just increment the qty. We already know the cost.
                    ++PartsRecovered[pps.partInfo.title];
                }

            }
            //Save the total return before modifiers for display purposes
            float totalBeforeReturn = (float)Math.Round(totalReturn, 2);
            //Modify the return by the recoveryPercent
            totalReturn *= recoveryPercent;
            //Round it to two decimals
            totalReturn = (float)Math.Round(totalReturn, 2);
            //Fire some log messages about the return, including the name, percent returned, distance, and funds returned (out of the total possible)
            Debug.Log("[SR] '"+pv.vesselName+"' being recovered by SR. Percent returned: " + 100 * recoveryPercent + "%. Distance from KSC: " + Math.Round(distanceFromKSC / 1000, 2) + " km");
            Debug.Log("[SR] Funds being returned: " + totalReturn + "/" + totalBeforeReturn);

            //Start adding some in-game display messages about the return
            msg.AppendLine("Stage '" + pv.vesselName + "' recovered " + Math.Round(distanceFromKSC / 1000, 2) + " km from KSC");
            msg.AppendLine("Parts recovered:");
            //List all the parts recovered, their quantities, and the total funds returned for the parts
            for (int i = 0; i < PartsRecovered.Count; i++ )
            {
                msg.AppendLine(PartsRecovered.Values.ElementAt(i) + " x " + PartsRecovered.Keys.ElementAt(i)+": "+(PartsRecovered.Values.ElementAt(i) * Costs.Values.ElementAt(i)));
            }
            //List the percent returned and break it down into distance and speed percentages
            msg.AppendLine("Recovery percentage: " + Math.Round(100 * recoveryPercent, 1) + "% (" + Math.Round(100 * distancePercent, 1) + "% distance, " + Math.Round(100 * RecoveryMod, 1)+"% speed)");
            //List the total refunds for parts, fuel, and the combined total
            msg.AppendLine("Total refunded for parts: " + DryReturns);
            msg.AppendLine("Total refunded for fuel: " + FuelReturns);
            msg.AppendLine("Total refunds: " + totalReturn);
            
            //Return the totalReturn
            return totalReturn;
        }

        //Helper function that I found on StackExchange that helps immensly with dealing with Reflection. I'm not that good at reflection (accessing other mod's functions and data)
        public object GetMemberInfoValue(System.Reflection.MemberInfo member, object sourceObject)
        {
            object newVal;
            if (member is System.Reflection.FieldInfo)
                newVal = ((System.Reflection.FieldInfo)member).GetValue(sourceObject);
            else
                newVal = ((System.Reflection.PropertyInfo)member).GetValue(sourceObject, null);
            return newVal;
        }

        //The main show. The VesselDestroyEvent is activated whenever KSP destroys a vessel. We only care about it in a specific set of circumstances
        private void VesselDestroyEvent(Vessel v)
        {
            //If FlightGlobals is null, just return. We can't do anything
            if (FlightGlobals.fetch == null)
                return;

            //If the protoVessel is null, we can't do anything so just return
            if (v.protoVessel == null)
                return;

            //Our criteria for even attempting recovery. Broken down: vessel exists, isn't the active vessel, is around Kerbin, is either unloaded or packed, altitude is less than 35km,
            //is flying or sub orbital, and is not an EVA (aka, Kerbals by themselves)
            if (v != null && !(HighLogic.LoadedSceneIsFlight && v.isActiveVessel) && v.mainBody.bodyName == "Kerbin" && (!v.loaded || v.packed) && v.altitude < 35000 &&
               (v.situation == Vessel.Situations.FLYING || v.situation == Vessel.Situations.SUB_ORBITAL) && !v.isEVA)
            {
                //Holders for various things.
                //Total vessel mass
                double totalMass = 0;
                //Stock drag coefficient for terminal velocity (Vt) calculations
                double dragCoeff = 0;
                //If RealChute parachutes are on the vessel
                bool realChuteInUse = false;
                //A parameter for determining the Vt with RealChutes (RC)
                float RCParameter = 0f;

                //Pack all the parts. I got this from MCE and everything works so I haven't tried removing it.
                if (!v.packed)
                    foreach (Part p in v.Parts)
                        p.Pack();

                //Check to see if Deadly Reentry is installed (check the loaded assemblies for DeadlyReentry.ReentryPhysics (namespace.class))
                bool DeadlyReentryInstalled = AssemblyLoader.loadedAssemblies
                        .Select(a => a.assembly.GetExportedTypes())
                        .SelectMany(t => t)
                        .FirstOrDefault(t => t.FullName == "DeadlyReentry.ReentryPhysics") != null;

                //Holder for the chance of burning up in atmosphere (through my non-scientific calculations)
                float burnChance = 0f;
                //If DR is installed, the DRMaxVelocity setting is above 0, and the orbital speed is above the DRMaxV setting then we calculate the burnChance
                if (DeadlyReentryInstalled && Settings.instance.DeadlyReentryMaxVelocity > 0 && v.obt_speed > Settings.instance.DeadlyReentryMaxVelocity)
                {
                    //the burnChance is 2% per 1% that the orbital velocity is above the DRMaxV
                    burnChance = (float)(2 * ((v.obt_speed / Settings.instance.DeadlyReentryMaxVelocity) - 1));
                    //Log a message alerting us to the speed and the burnChance
                    Debug.Log("[SR] DR velocity exceeded (" + v.obt_speed + "/" + Settings.instance.DeadlyReentryMaxVelocity + ") Chance of burning up: " + burnChance);
                }

                //Holders for the total amount of ablative shielding available, and the maximum total
                float totalHeatShield = 0f, maxHeatShield = 0f;
                
                //Here's the meat of the method. Search through the ProtoPartSnapshots and their modules for the ones we're looking for (parachutes and heat shields)
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                {
                    //Make a list of all the Module Names for easy checking later. This can be avoided, but is convenient.
                    List<string> ModuleNames = new List<string>();
                    foreach (ProtoPartModuleSnapshot ppms in p.modules)
                    {
                        ModuleNames.Add(ppms.moduleName);
                    }
                    //Add the part mass to the total.
                    totalMass += p.mass;
                    //Assume the part isn't a parachute until proven a parachute
                    bool isParachute = false;
                    //For instance, by having the ModuleParachute module
                    if (ModuleNames.Contains("ModuleParachute"))
                    {
                        //Cast the module as a ModuleParachute (find it in the module list by checking for a module with the name ModuleParachute
                        //We need the PartModule (aka, moduleRef), not the ProtoPartModuleSnapshot. This could probably be done a different way with the PPMS
                        ModuleParachute mp = (ModuleParachute)p.modules.First(mod => mod.moduleName == "ModuleParachute").moduleRef;
                        //Add the part mass times the fully deployed drag (typically 500) to the dragCoeff variable (you'll see why later)
                        dragCoeff += p.mass * mp.fullyDeployedDrag;
                        //This is most definitely a parachute part
                        isParachute = true;
                    }
                    //If the part has the RealChuteModule, we have to do some tricks to access it
                    if (ModuleNames.Contains("RealChuteModule"))
                    {
                        //First off, get the PPMS since we'll need that
                        ProtoPartModuleSnapshot realChute = p.modules.First(mod => mod.moduleName == "RealChuteModule");
                        //Assuming that's not somehow null, then we continue
                        if ((object)realChute != null) //Some of this was adopted from DebRefund, as Vendan's method of handling multiple parachutes is better than what I had.
                        {
                            //This is where the Reflection starts. We need to access the material library that RealChute has, so we first grab it's Type
                            Type matLibraryType = AssemblyLoader.loadedAssemblies
                                .SelectMany(a => a.assembly.GetExportedTypes())
                                .SingleOrDefault(t => t.FullName == "RealChute.Libraries.MaterialsLibrary");

                            //We make a list of ConfigNodes containing the parachutes (usually 1, but now there can be any number of them)
                            //We get that from the PPMS 
                            ConfigNode[] parachutes = realChute.moduleValues.GetNodes("PARACHUTE");
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
                                float dragC = (float)GetMemberInfoValue(materialObject.GetType().GetMember("dragCoefficient")[0], materialObject);
                                //Now we calculate the RCParameter. Simple addition of this doesn't result in perfect results for Vt with parachutes with different diameter or drag coefficients
                                //But it works perfectly for mutiple identical parachutes (the normal case)
                                RCParameter += dragC * (float)Math.Pow(diameter, 2);

                            }
                            //This is a parachute also
                            isParachute = true;
                            //It's existence means that RealChute is installed and in use on the craft (you could have it installed and use stock chutes, so we only check if it's on the craft)
                            realChuteInUse = true;
                        }
                    }
                    //If there's a chance that the craft will burn up then we also check for heat shields
                    if (burnChance > 0 && ModuleNames.Contains("ModuleHeatShield"))
                    {
                        //Grab the heat shield module
                        ProtoPartModuleSnapshot heatShield = p.modules.First(mod => mod.moduleName == "ModuleHeatShield");
                        //Determine what type of shielding is in use
                        String ablativeType = heatShield.moduleValues.GetValue("ablative");
                        //Hopefully it's AblativeShielding, because that's what we want
                        if (ablativeType == "AblativeShielding")
                        {
                            //Determine the amount of shielding remaining
                            float shieldRemaining = float.Parse(p.resources.Find(r => r.resourceName == ablativeType).resourceValues.GetValue("amount"));
                            //And the maximum amount of shielding
                            float maxShield = float.Parse(p.resources.Find(r => r.resourceName == ablativeType).resourceValues.GetValue("maxAmount"));
                            //Add those to the totals for the craft
                            totalHeatShield += shieldRemaining;
                            maxHeatShield += maxShield;
                        }
                        else //Non-ablative shielding. Add a semi-random amount of shielding.
                        {
                            //We add 400 to each. This is so there's still a chance of failure
                            totalHeatShield += 400;
                            maxHeatShield += 400;
                        }
                        //Log that we found a heat shield
                        Debug.Log("[SR] Heat Shield found");

                    }
                    //If the part isn't a parachute (no ModuleParachute or RealChuteModule)
                    if (!isParachute)
                    {
                        //If the part reference isn't null, find the maximum drag paramater. Multiply that by the mass (KSP has stupid aerodynamics)
                        if (p.partRef != null)
                            dragCoeff += p.mass * p.partRef.maximum_drag;
                        //Otherwise we assume it's a 0.2 drag. We could probably determine the exact value from the config node
                        else
                            dragCoeff += p.mass * 0.2;
                    }
                }

                //Assume we're not going to burn up until proven that we will
                bool burnIt = false;
                //Well, we can't burn up unless the chance of doing so is greater than 0
                if (burnChance > 0)
                {
                    //If there's heatshields on the vessel then reduce the chance by the current total/the max. Aka, up to 100%
                    if (maxHeatShield > 0)
                        burnChance -= (totalHeatShield / maxHeatShield);
                    //Pick a random number between 0 and 1
                    System.Random rand = new System.Random();
                    double choice = rand.NextDouble();
                    //If that's less than or equal to the chance of burning, then we burn (25% chance = 0.25, random must be below 0.25)
                    burnIt = (choice <= burnChance);
                    //Once again, more log messages to help with debugging of people's issues
                    Debug.Log("[SR] Burn chance: " + burnChance + " rand: " + choice + " burning? " + burnIt);
                }

                //Holder for the terminal velocity. Assume it's infinity until proven otherwise
                double Vt = double.MaxValue;
                //If we're using stock chutes then calculate it the stock way
                if (!realChuteInUse)
                {
                    //This all follows from the formulas on the KSP wiki under the atmosphere page. http://wiki.kerbalspaceprogram.com/wiki/Atmosphere
                    //Divide the current value of the dragCoeff by the total mass. Now we have the actual drag coefficient for the vessel
                    dragCoeff = dragCoeff / (totalMass);
                    //Calculate Vt by what the wiki says
                    Vt = Math.Sqrt((250 * 6.674E-11 * 5.2915793E22) / (3.6E11 * 1.22309485 * dragCoeff));
                    //Let the log know that we're using stock and what the drag and Vt are
                    Debug.Log("[SR] Using Stock Module! Drag: " + dragCoeff + " Vt: " + Vt);
                }
                //Otherwise we're using RealChutes and we have a bit different of a calculation
                else
                {
                    //This is according to the formulas used by Stupid_Chris in the Real Chute drag calculator program included with Real Chute. Source: https://github.com/StupidChris/RealChute/blob/master/Drag%20Calculator/RealChute%20drag%20calculator/RCDragCalc.cs
                    Vt = (800 * totalMass * 9.8) / (1.223 * Math.PI) * Math.Pow(RCParameter, -1);
                    //More log messages! Using RC and the Vt.
                    Debug.Log("[SR] Using RealChute Module! Vt: " + Vt);
                }
                
                //Get the list of part names and the quantities. We'll need it.
                Dictionary<string, int> RecoveredPartsForEvent = RecoveredPartsFromVessel(v);
                //This is the message that will actually be displayed in-game
                StringBuilder msg = new StringBuilder();
                //Recovery successful if the Vt is below the Cutoff Velocity for the flat rate model or the High Cutoff for the variable rate model. Unless it burns up that is.
                if (((Settings.instance.FlatRateModel && Vt < Settings.instance.CutoffVelocity) || 
                    (!Settings.instance.FlatRateModel && Vt < Settings.instance.HighCut)) && !burnIt)
                {
                    //A lot of the recovery code was moved into different functions to make this less massive of a method, but that actually just made things harder.
                    //As such, most of the behind the scenes recovery stuff is called from the DoRecovery method
                    DoRecovery(v, msg, (float)Vt);

                    //If we're allowed to show success messages then we actually post the message. Otherwise we did all the msg.AppendLines for nothing
                    if (Settings.instance.ShowSuccessMessages)
                    {
                        //By this point all the real work is done. Now we just display a bit of information
                        msg.AppendLine("\nAdditional Information:");
                        //Display which module was used for recovery
                        if (realChuteInUse)
                            msg.AppendLine("RealChute Module used.");
                        else
                            msg.AppendLine("Stock Module used.");
                        //Display the terminal velocity (Vt) and what is needed to have any recovery
                        if (Settings.instance.FlatRateModel)
                            msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.CutoffVelocity + " needed)");
                        else
                            msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.HighCut + " needed)");

                        //Setup and then post the message
                        MessageSystem.Message m = new MessageSystem.Message("Stage Recovered", msg.ToString(), MessageSystemButton.MessageButtonColor.BLUE, MessageSystemButton.ButtonIcons.MESSAGE);
                        MessageSystem.Instance.AddMessage(m);
                    }

                    //Stuff for the API and firing the RecoverySuccessEvent

                    //Holder for the recovery value. Actually just the speed portion, not the distance portion too
                    //It's 1 (100%) if we're using the Flat Rate Model
                    float recoveryValue = 1;
                    //Variable Recovery requires that we calculate it based on Vt
                    if (!Settings.instance.FlatRateModel)
                        recoveryValue = GetVariableRecoveryValue((float)Vt);
                        
                    //We also make an array containing the recovery value, total funds recovered, and the total science recovered
                    float[] infoArray = new float[] { recoveryValue, fundsRecovered, scienceRecovered };
                    //Fire success event, passing the vessel, infoArray, and the "SUCCESS" failure reason (aka, non-failure)
                    APIManager.instance.RecoverySuccessEvent.Fire(v, infoArray, "SUCCESS");
                }
                //What a shame! We didn't recover the vessel!
                else
                {
                    //If we're allowed to show failure messages, then let's go about doing that.
                    if (Settings.instance.ShowFailureMessages)
                    {
                        //Say the name of the Stage and that it was destroyed
                        msg.AppendLine("Stage '" + v.protoVessel.vesselName + "' was destroyed!");
                        //Then list all the parts on the stage (stage==vessel by the way. I use them interchangeably, but vessel is more correct)
                        msg.AppendLine("Stage contained these parts:");
                        for (int i = 0; i < RecoveredPartsForEvent.Count; i++)
                        {
                            msg.AppendLine(RecoveredPartsForEvent.Values.ElementAt(i) + " x " + RecoveredPartsForEvent.Keys.ElementAt(i));
                        }
                        //If we're career mode (MONEY!) then we also let you know the (why do I say 'we'? It's only me working on this) total cost of the parts
                        if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                        {
                            float totalCost = 0;
                            //Cycle through all the parts
                            foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                            {
                                float dry, wet;
                                //Add the max of 0 or the part cost (in case they're negative, looking at you MKS and TweakScale!)
                                totalCost += Math.Max(ShipConstruction.GetPartCosts(pps, pps.partInfo, out dry, out wet), 0);
                            }
                            //Alert the user to what the total value was (without modifiers)
                            msg.AppendLine("It was valued at " + totalCost + " Funds.");
                        }
                        //We'll still tell you what module was used and the Vt, just in case it was by a slim margin that it failed
                        msg.AppendLine("\nAdditional Information:");
                        if (realChuteInUse)
                            msg.AppendLine("RealChute Module used.");
                        else
                            msg.AppendLine("Stock Module used.");
                        if (Settings.instance.FlatRateModel)
                            msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.CutoffVelocity + " needed)");
                        else
                            msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.HighCut + " needed)");

                        //If it failed because of burning up (can be in addition to speed) then we'll let you know
                        if (burnIt)
                            msg.AppendLine("The stage burned up in the atmosphere! It was travelling at " + v.obt_speed + " m/s.");

                        //Now we actually create and post the message
                        MessageSystem.Message m = new MessageSystem.Message("Stage Destroyed", msg.ToString(), MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.MESSAGE);
                        MessageSystem.Instance.AddMessage(m);
                    }
                    
                    //API stuff

                    //The reason for failure is either because it burned up (takes precendence) or because of speed (greater than the cutoff)
                    string reasonForFailure = burnIt ? "BURNUP" : "SPEED";
                    //No parts survive, no funds are recovered, and no science is saved
                    float[] infoArray = new float[] { 0, 0, 0 };
                    //Fire failure event, passing the vessel, the infoArray, and the reason for failure ("BURNUP" or "SPEED")
                    APIManager.instance.RecoveryFailureEvent.Fire(v, infoArray, reasonForFailure);
                }
            }
        }

        //These two are needed for the API on successful recovery
        private float fundsRecovered, scienceRecovered;
        //This function calls all the other functions that do the behind the scenes recovery work
        public void DoRecovery(Vessel v, StringBuilder msg, float Vt)
        {
            //If we're career mode then we need to recovery funds
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                //The funds recovered are determined by the GetRecoveryValueForParachutes function. Which is terribly named once I add powered recovery...
                fundsRecovered = GetRecoveryValueForParachutes(v, msg, Vt);
                //Add those funds to the save
                AddFunds(fundsRecovered);
            }
            //If we aren't in career mode (Science or Sandbox) still list the parts, just not the costs
            else
            {
                msg.AppendLine("Stage contains these parts:");
                Dictionary<string, int> PartsRecovered = RecoveredPartsFromVessel(v);
                for (int i = 0; i < PartsRecovered.Count; i++)
                {
                    msg.AppendLine(PartsRecovered.Values.ElementAt(i) + " x " + PartsRecovered.Keys.ElementAt(i));
                }
            }

            //If we can recover Kerbals and there's a reason to
            if (Settings.instance.RecoverKerbals && v.protoVessel.GetVesselCrew().Count > 0)
            {
                msg.AppendLine("\nRecovered Kerbals:");
                //Recover the kerbals and list their names
                foreach (ProtoCrewMember pcm in v.protoVessel.GetVesselCrew())
                {
                    Debug.Log("[SR] Recovering crewmember " + pcm.name);
                    //Yeah, that's all it takes to recover a kerbal. Set them to Available from Assigned
                    pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    msg.AppendLine(pcm.name);
                }
            }
            //If we can recover science and we're in Career mode or Science Mode
            if (Settings.instance.RecoverScience && (HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX))
            {
                //Science recovery is harded, so its got its own function
                float returned = RecoverScience(v);
                //If we actually recovered anything then put a message
                if (returned > 0)
                    msg.AppendLine("\nScience Recovered: "+returned);
                //The API needs to know how much was recovered
                scienceRecovered = returned;
            }
        }

        //Function for determining the parts on a vessel (just the names) and the quantity of each
        public Dictionary<string, int> RecoveredPartsFromVessel(Vessel v)
        {
            //This is what we'll return. Hence being called ret.
            Dictionary<string, int> ret = new Dictionary<string, int>();
            //Loop through the part snapshots
            foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
            {
                //If the return dictionary doesn't have the part name, add it with qty 1
                if (!ret.ContainsKey(pps.partInfo.name))
                {
                    ret.Add(pps.partInfo.name, 1);
                }
                //Otherwise increase the quantity
                else
                {
                    ++ret[pps.partInfo.name];
                }
            }
            //Return dat shiz
            return ret;
        }

        //This function, not surprisingly, recovers the science on the vessel
        public float RecoverScience(Vessel v)
        {
            //We'll return the total at the end
            float totalScience = 0;
            //Go through the parts
            foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
            {
                //Go through the modules on each part
                foreach (ProtoPartModuleSnapshot pm in p.modules)
                {
                    ConfigNode node = pm.moduleValues;
                    //Find the ones with the name "ScienceData
                    if (node.HasNode("ScienceData"))
                    {
                        //And loop through them
                        foreach (ConfigNode subjectNode in node.GetNodes("ScienceData"))
                        {
                            //Get the ScienceSubject from the subjectID
                            ScienceSubject subject = ResearchAndDevelopment.GetSubjectByID(subjectNode.GetValue("subjectID"));
                            //Get the amount of data saved
                            float amt = float.Parse(subjectNode.GetValue("data"));
                            //And submit that data with the subjectID to the R&D center, getting the amount earned back
                            float science = ResearchAndDevelopment.Instance.SubmitScienceData(amt, subject, 1f);
                            //Add the amount earned to the total earned
                            totalScience += science;
                        }
                    }
                }
            }
            //Return the total
            return totalScience;
        }

        //When using the variable recovery rate we determine the rate from a negative curvature quadratic with y=100 at velocity=lowCut and y=0 at vel=highCut.
        //No other zeroes are in that range. Check this github issue for an example and some more details: https://github.com/magico13/StageRecovery/issues/1
        public float GetVariableRecoveryValue(float v)
        {
            //We're following ax^2+bx+c=recovery
            //We know that -b/2a=LowCut since that's the only location where the derivative of the quadratic is 0 (the max)
            //Starting conditions: x=lowCut y=100, x=highCut y=0. Combined with the above info, we can calculate everything
            float x0 = Settings.instance.LowCut;
            float x1 = Settings.instance.HighCut;
            //If we're below the low cut, then return 1 (100%)
            if (v < x0) return 1;
            //If we're above the high cut, return 0
            if (v > x1) return 0;
            //Well, we're inbetween. Calculate the 'a' parameter.
            float a = (float)(-100 / (Math.Pow(x1, 2) - 2 * x0 * x1 + Math.Pow(x0, 2)));
            //From 'a' we can calculate 'b'. 
            float b = -2 * a * x0;
            //And then 'c'
            float c = (float)(a * Math.Pow(x0, 2) + 100);
            //The return value is now a simple matter. The function is setup for percentages but we want to return a float between 0 and 1, so divide by 100
            float ret = (float)(a * Math.Pow(v, 2) + b * v + c)/100f;
            return ret;
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