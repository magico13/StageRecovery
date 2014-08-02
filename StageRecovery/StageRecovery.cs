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
        private static bool eventAdded = false;

        internal StageRecovery()
        {
            if (ToolbarManager.ToolbarAvailable)
                Settings.instance.gui.AddToolbarButton();
        }

        public void Awake()
        {
            if (Settings.instance == null)
                Settings.instance = new Settings();
            RenderingManager.AddToPostDrawQueue(0, OnDraw);
        }

        private void OnDraw()
        {
            Settings.instance.gui.SetGUIPositions(OnWindow);
        }

        private void OnWindow(int windowID)
        {
            Settings.instance.gui.DrawGUIs(windowID);
        }

        public void OnDestroy()
        {
            if (Settings.instance.gui.SRButtonStock != null)
                ApplicationLauncher.Instance.RemoveModApplication(Settings.instance.gui.SRButtonStock);
            if (Settings.instance.gui.SRToolbarButton != null)
                Settings.instance.gui.SRToolbarButton.Destroy();
        }

        public void Start()
        {
            if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                return;

            if (!eventAdded)
            {
                Debug.Log("[SR] Adding event!");
                GameEvents.onVesselDestroy.Add(VesselDestroyEvent);
                if (!ToolbarManager.ToolbarAvailable)
                    GameEvents.onGUIApplicationLauncherReady.Add(Settings.instance.gui.OnGUIAppLauncherReady);
                //Settings.instance = new Settings();
                eventAdded = true;
            }
            Settings.instance.Load();
            if (Settings.instance.RecoveryModifier > 1) Settings.instance.RecoveryModifier = 1;
            if (Settings.instance.RecoveryModifier < 0) Settings.instance.RecoveryModifier = 0;
            Settings.instance.Save();
        }

        public double AddFunds(double toAdd)
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                return 0;
            Debug.Log("[SR] Adding funds: " + toAdd + ", New total: " + (Funding.Instance.Funds + toAdd));
            return (Funding.Instance.Funds += toAdd);
        }

        public float GetRecoveryValueForParachutes(Vessel v, StringBuilder msg, float Vt)
        {
            ProtoVessel pv = v.protoVessel;
            //StringBuilder msg = new StringBuilder();
            Dictionary<string, int> PartsRecovered = new Dictionary<string, int>();
            Dictionary<string, float> Costs = new Dictionary<string, float>();
            float FuelReturns = 0, DryReturns = 0;
            float RecoveryMod = 0;
            if (Settings.instance.FlatRateModel)
            {
                bool stageControllable = false;
                foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    if (pps.modules.Find(module => (module.moduleName == "ModuleCommand" && ((ModuleCommand)module.moduleRef).minimumCrew <= pps.protoModuleCrew.Count)) != null)
                    {
                        Debug.Log("[SR] Stage is controlled!");
                        stageControllable = true;
                        break;
                    }
                }

                RecoveryMod = stageControllable ? 1.0f : Settings.instance.RecoveryModifier;
            }
            else
                RecoveryMod = GetVariableRecoveryValue(Vt);
            double distanceFromKSC = SpaceCenter.Instance.GreatCircleDistance(SpaceCenter.Instance.cb.GetRelSurfaceNVector(pv.latitude, pv.longitude));
            double maxDist = SpaceCenter.Instance.cb.Radius * Math.PI;
            float distancePercent = Mathf.Lerp(0.98f, 0.1f, (float)(distanceFromKSC / maxDist));
            float recoveryPercent = RecoveryMod * distancePercent;
            float totalReturn = 0;
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                float dryCost, fuelCost;
                //totalReturn += 
                ShipConstruction.GetPartCosts(pps, pps.partInfo, out dryCost, out fuelCost);
                dryCost = dryCost < 0 ? 0 : dryCost;
                fuelCost = fuelCost < 0 ? 0 : fuelCost;
                totalReturn = dryCost + fuelCost;

                FuelReturns += fuelCost*recoveryPercent;
                DryReturns += dryCost*recoveryPercent;
                if (!PartsRecovered.ContainsKey(pps.partInfo.title))
                {
                    PartsRecovered.Add(pps.partInfo.title, 1);
                    Costs.Add(pps.partInfo.title, dryCost*recoveryPercent);
                }
                else
                {
                    ++PartsRecovered[pps.partInfo.title];
                }

            }
            float totalBeforeReturn = (float)Math.Round(totalReturn, 2);
            totalReturn *= recoveryPercent;
            totalReturn = (float)Math.Round(totalReturn, 2);
            Debug.Log("[SR] '"+pv.vesselName+"' being recovered by SR. Percent returned: " + 100 * recoveryPercent + "%. Distance from KSC: " + Math.Round(distanceFromKSC / 1000, 2) + " km");
            Debug.Log("[SR] Funds being returned: " + totalReturn + "/" + totalBeforeReturn);


            msg.AppendLine("Stage '" + pv.vesselName + "' recovered " + Math.Round(distanceFromKSC / 1000, 2) + " km from KSC");
            msg.AppendLine("Parts recovered:");
            for (int i = 0; i < PartsRecovered.Count; i++ )
            {
                msg.AppendLine(PartsRecovered.Values.ElementAt(i) + " x " + PartsRecovered.Keys.ElementAt(i)+": "+(PartsRecovered.Values.ElementAt(i) * Costs.Values.ElementAt(i)));
            }
            msg.AppendLine("Recovery percentage: " + Math.Round(100 * recoveryPercent, 1) + "% (" + Math.Round(100 * distancePercent, 1) + "% distance, " + Math.Round(100 * RecoveryMod, 1)+"% damage)");
            msg.AppendLine("Total refunded for parts: " + DryReturns);
            msg.AppendLine("Total refunded for fuel: " + FuelReturns);
            msg.AppendLine("Total refunds: " + totalReturn);
            
            return totalReturn;
        }

        public object GetMemberInfoValue(System.Reflection.MemberInfo member, object sourceObject)
        {
            object newVal;
            if (member is System.Reflection.FieldInfo)
                newVal = ((System.Reflection.FieldInfo)member).GetValue(sourceObject);
            else
                newVal = ((System.Reflection.PropertyInfo)member).GetValue(sourceObject, null);
            return newVal;
        }

        private void VesselDestroyEvent(Vessel v)
        {
            if (FlightGlobals.fetch == null)
                return;

            if (v != null && !(HighLogic.LoadedSceneIsFlight && v.isActiveVessel) && v.mainBody.bodyName == "Kerbin" && (!v.loaded || v.packed) && v.altitude < 35000 &&
               (v.situation == Vessel.Situations.FLYING || v.situation == Vessel.Situations.SUB_ORBITAL) && !v.isEVA)
            {
                double totalMass = 0;
                double dragCoeff = 0;
                bool realChuteInUse = false;
                float RCParameter = 0f;
                //float totalDrag = 0;

                if (!v.packed)
                    foreach (Part p in v.Parts)
                        p.Pack();

                if (v.protoVessel == null)
                    return;

                bool DeadlyReentryInstalled = AssemblyLoader.loadedAssemblies
                        .Select(a => a.assembly.GetExportedTypes())
                        .SelectMany(t => t)
                        .FirstOrDefault(t => t.FullName == "DeadlyReentry.ReentryPhysics") != null;

                float burnChance = 0f;
                if (DeadlyReentryInstalled && Settings.instance.DeadlyReentryMaxVelocity > 0 && v.obt_speed > Settings.instance.DeadlyReentryMaxVelocity)
                {
                    burnChance = (float)(2 * ((v.obt_speed / Settings.instance.DeadlyReentryMaxVelocity) - 1));
                    Debug.Log("[SR] DR velocity exceeded (" + v.obt_speed + "/" + Settings.instance.DeadlyReentryMaxVelocity + ") Chance of burning up: " + burnChance);
                }

                float totalHeatShield = 0f, maxHeatShield = 0f;
                
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                {
                    List<string> ModuleNames = new List<string>();
                    foreach (ProtoPartModuleSnapshot ppms in p.modules)
                    {
                        ModuleNames.Add(ppms.moduleName);
                    }
                    totalMass += p.mass;
                    bool isParachute = false;
                    if (ModuleNames.Contains("ModuleParachute"))
                    {
                        ModuleParachute mp = (ModuleParachute)p.modules.First(mod => mod.moduleName == "ModuleParachute").moduleRef;
                        dragCoeff += p.mass * mp.fullyDeployedDrag;
                        isParachute = true;
                    }
                    if (ModuleNames.Contains("RealChuteModule"))
                    {
                        ProtoPartModuleSnapshot realChute = p.modules.First(mod => mod.moduleName == "RealChuteModule");
                        if ((object)realChute != null) //Some of this was adopted from DebRefund, as Vendan's method of handling multiple parachutes is better than what I had.
                        {
                            Type matLibraryType = AssemblyLoader.loadedAssemblies
                                .SelectMany(a => a.assembly.GetExportedTypes())
                                .SingleOrDefault(t => t.FullName == "RealChute.Libraries.MaterialsLibrary");

                            ConfigNode[] parchutes = realChute.moduleValues.GetNodes("PARACHUTE");
                            foreach (ConfigNode chute in parchutes)
                            {
                                float diameter = float.Parse(chute.GetValue("deployedDiameter"));
                                //area = (float)(Math.Pow(area / 2, 2) * Math.PI);
                                string mat = chute.GetValue("material");
                                System.Reflection.MethodInfo matMethod = matLibraryType.GetMethod("GetMaterial", new Type[] { mat.GetType() });
                                object MatLibraryInstance = matLibraryType.GetProperty("instance").GetValue(null, null);
                                object materialObject = matMethod.Invoke(MatLibraryInstance, new object[] { mat });
                                float dragC = (float)GetMemberInfoValue(materialObject.GetType().GetMember("dragCoefficient")[0], materialObject);
                                RCParameter += dragC * (float)Math.Pow(diameter, 2);
                                //totalDrag += (1 * 100 * dragC * area / 2000f);

                            }
                            isParachute = true;
                            realChuteInUse = true;
                        }
                    }
                    if (burnChance > 0 && ModuleNames.Contains("ModuleHeatShield"))
                    {
                        ProtoPartModuleSnapshot heatShield = p.modules.First(mod => mod.moduleName == "ModuleHeatShield");
                        String ablativeType = heatShield.moduleValues.GetValue("ablative");
                        if (ablativeType == "AblativeShielding")
                        {
                            float shieldRemaining = float.Parse(p.resources.Find(r => r.resourceName == ablativeType).resourceValues.GetValue("amount"));
                            float maxShield = float.Parse(p.resources.Find(r => r.resourceName == ablativeType).resourceValues.GetValue("maxAmount"));
                            totalHeatShield += shieldRemaining;
                            maxHeatShield += maxShield;
                        }
                        else //Non-ablative shielding. Add a semi-random amount of shielding.
                        {
                            totalHeatShield += 400;
                            maxHeatShield += 400;
                        }
                        Debug.Log("[SR] Heat Shield found");

                    }
                    if (!isParachute)
                    {
                        if (p.partRef != null)
                            dragCoeff += p.mass * p.partRef.maximum_drag;
                        else
                            dragCoeff += p.mass * 0.2;
                    }
                }

                bool burnIt = false;
                if (burnChance > 0)
                {
                    if (maxHeatShield > 0)
                        burnChance -= (totalHeatShield / maxHeatShield);
                    System.Random rand = new System.Random();
                    double choice = rand.NextDouble();
                    burnIt = (choice <= burnChance);
                    Debug.Log("[SR] Burn chance: " + burnChance + " rand: " + choice + " burning? " + burnIt);
                }

                double Vt = double.MaxValue;
                if (!realChuteInUse)
                {
                    dragCoeff = dragCoeff / (totalMass);
                    Vt = Math.Sqrt((250 * 6.674E-11 * 5.2915793E22) / (3.6E11 * 1.22309485 * dragCoeff));
                    Debug.Log("[SR] Using Stock Module! Drag: " + dragCoeff + " Vt: " + Vt);
                }
                else
                {
                    Vt = (800 * totalMass * 9.8) / (1.223 * Math.PI) * Math.Pow(RCParameter, -1);
                    Debug.Log("[SR] Using RealChute Module! Vt: " + Vt);
                }
                Dictionary<string, int> RecoveredPartsForEvent = RecoveredPartsFromVessel(v);
                StringBuilder msg = new StringBuilder();
                if (((Settings.instance.FlatRateModel && Vt < Settings.instance.CutoffVelocity) || 
                    (!Settings.instance.FlatRateModel && Vt < Settings.instance.HighCut)) && !burnIt)
                {
                    DoRecovery(v, msg, (float)Vt);
                    msg.AppendLine("\nAdditional Information:");
                    if (realChuteInUse)
                        msg.AppendLine("RealChute Module used.");
                    else
                        msg.AppendLine("Stock Module used.");
                    if (Settings.instance.FlatRateModel)
                        msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.CutoffVelocity + " needed)");
                    else
                        msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.HighCut + " needed)");
                    if (Settings.instance.ShowSuccessMessages)
                    {
                        MessageSystem.Message m = new MessageSystem.Message("Stage Recovered", msg.ToString(), MessageSystemButton.MessageButtonColor.BLUE, MessageSystemButton.ButtonIcons.MESSAGE);
                        MessageSystem.Instance.AddMessage(m);
                    }

                    float recoveryValue = 0;
                    if (Settings.instance.FlatRateModel && Vt < Settings.instance.CutoffVelocity)
                        recoveryValue = 1;
                    else
                        recoveryValue = GetVariableRecoveryValue((float)Vt);
                    float[] infoArray = new float[] { recoveryValue, fundsRecovered, scienceRecovered };
                    //Fire success event
                    APIManager.instance.RecoverySuccessEvent.Fire(v, infoArray, "SUCCESS");
                }
                else
                {
                    if (Settings.instance.ShowFailureMessages)
                    {
                        msg.AppendLine("Stage '" + v.protoVessel.vesselName + "' was destroyed!");
                        {
                            msg.AppendLine("Stage contained these parts:");
                            for (int i = 0; i < RecoveredPartsForEvent.Count; i++)
                            {
                                msg.AppendLine(RecoveredPartsForEvent.Values.ElementAt(i) + " x " + RecoveredPartsForEvent.Keys.ElementAt(i));
                            }
                        }
                        if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                        {
                            float totalCost = 0;
                            foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                            {
                                float dry, wet;
                                totalCost += Math.Max(ShipConstruction.GetPartCosts(pps, pps.partInfo, out dry, out wet), 0);
                            }
                            msg.AppendLine("It was valued at " + totalCost + " Funds.");
                        }
                        msg.AppendLine("\nAdditional Information:");
                        if (realChuteInUse)
                            msg.AppendLine("RealChute Module used.");
                        else
                            msg.AppendLine("Stock Module used.");
                        if (Settings.instance.FlatRateModel)
                            msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.CutoffVelocity + " needed)");
                        else
                            msg.AppendLine("Terminal velocity of " + Math.Round(Vt, 2) + " (less than " + Settings.instance.HighCut + " needed)");

                        if (burnIt)
                            msg.AppendLine("The stage burned up in the atmosphere! It was travelling at " + v.obt_speed + " m/s.");

                        MessageSystem.Message m = new MessageSystem.Message("Stage Destroyed", msg.ToString(), MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.MESSAGE);
                        MessageSystem.Instance.AddMessage(m);
                    }

                    string reasonForFailure = burnIt ? "BURNUP" : "SPEED";
                    float[] infoArray = new float[] { 0, 0, 0 };
                    //Fire failure event
                    APIManager.instance.RecoveryFailureEvent.Fire(v, infoArray, reasonForFailure);
                }
            }
        }

        private float fundsRecovered, scienceRecovered;
        public void DoRecovery(Vessel v, StringBuilder msg, float Vt)
        {
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                fundsRecovered = GetRecoveryValueForParachutes(v, msg, Vt);
                AddFunds(fundsRecovered);
            }
            else
            {
                msg.AppendLine("Stage contains these parts:");
                Dictionary<string, int> PartsRecovered = RecoveredPartsFromVessel(v);
                for (int i = 0; i < PartsRecovered.Count; i++)
                {
                    msg.AppendLine(PartsRecovered.Values.ElementAt(i) + " x " + PartsRecovered.Keys.ElementAt(i));
                }
            }

            if (Settings.instance.RecoverKerbals && v.protoVessel.GetVesselCrew().Count > 0)
            {
                msg.AppendLine("\nRecovered Kerbals:");
                foreach (ProtoCrewMember pcm in v.protoVessel.GetVesselCrew())
                {
                    Debug.Log("[SR] Recovering crewmember " + pcm.name);
                    pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    msg.AppendLine(pcm.name);
                }
            }
            if (Settings.instance.RecoverScience && (HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX))
            {
                float returned = RecoverScience(v);
                if (returned > 0)
                    msg.AppendLine("\nScience Recovered: "+returned);
                scienceRecovered = returned;
            }
        }

        public Dictionary<string, int> RecoveredPartsFromVessel(Vessel v)
        {
            Dictionary<string, int> ret = new Dictionary<string, int>();
            foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
            {
                if (!ret.ContainsKey(pps.partInfo.name))
                {
                    ret.Add(pps.partInfo.name, 1);
                }
                else
                {
                    ++ret[pps.partInfo.name];
                }
            }

            return ret;
        }

        public float RecoverScience(Vessel v)
        {
            float totalScience = 0;
            foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot pm in p.modules)
                {
                    ConfigNode node = pm.moduleValues;
                    if (node.HasNode("ScienceData"))
                    {
                        foreach (ConfigNode subjectNode in node.GetNodes("ScienceData"))
                        {
                            ScienceSubject subject = ResearchAndDevelopment.GetSubjectByID(subjectNode.GetValue("subjectID"));
                            float amt = float.Parse(subjectNode.GetValue("data"));
                            float science = ResearchAndDevelopment.Instance.SubmitScienceData(amt, subject, 1f);
                            totalScience += science;
                        }
                    }
                }
            }
            return totalScience;
        }

        public float GetVariableRecoveryValue(float v)
        {
            float x0 = Settings.instance.LowCut;
            float x1 = Settings.instance.HighCut;
            if (v < x0) return 100;
            if (v > x1) return 0;
            float a = (float)(-100 / (Math.Pow(x1, 2) - 2 * x0 * x1 + Math.Pow(x0, 2)));
            float b = -2 * a * x0;
            float c = (float)(a * Math.Pow(x0, 2) + 100);
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