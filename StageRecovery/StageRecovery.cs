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

        public void Awake()
        {

        }

        public void Start()
        {
            if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                return;

            if (!eventAdded)
            {
                Debug.Log("[SR] Adding event!");
                GameEvents.onVesselDestroy.Add(VesselDestroyEvent);
                Settings.instance = new Settings();
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

        public float GetRecoveryValueForParachutes(ProtoVessel pv, StringBuilder msg)
        {
            //StringBuilder msg = new StringBuilder();
            Dictionary<string, int> PartsRecovered = new Dictionary<string, int>();
            Dictionary<string, float> Costs = new Dictionary<string, float>();
            float FuelReturns = 0, DryReturns = 0;
            bool probeCoreAttached = false;
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                if (pps.modules.Find(module => (module.moduleName == "ModuleCommand" && ((ModuleCommand)module.moduleRef).minimumCrew == 0)) != null)
                {
                    Debug.Log("[SR] Probe Core found!");
                    probeCoreAttached = true;
                    break;
                }
            }
            float RecoveryMod = probeCoreAttached ? 1.0f : Settings.instance.RecoveryModifier;
            double distanceFromKSC = SpaceCenter.Instance.GreatCircleDistance(SpaceCenter.Instance.cb.GetRelSurfaceNVector(pv.latitude, pv.longitude));
            double maxDist = SpaceCenter.Instance.cb.Radius * Math.PI;
            float recoveryPercent = RecoveryMod * Mathf.Lerp(0.98f, 0.1f, (float)(distanceFromKSC / maxDist));
            float totalReturn = 0;
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                float dryCost, fuelCost;
                totalReturn += ShipConstruction.GetPartCosts(pps, pps.partInfo, out dryCost, out fuelCost);
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
            msg.AppendLine("Recovery percentage: " + Math.Round(100 * recoveryPercent, 1) + "%");
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

                float totalDrag = 0;

                if (!v.packed)
                    foreach (Part p in v.Parts)
                        p.Pack();

                if (v.protoVessel == null)
                    return;
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
                                float area = float.Parse(chute.GetValue("deployedDiameter"));
                                area = (float)(Math.Pow(area / 2, 2) * Math.PI);
                                string mat = chute.GetValue("material");
                                System.Reflection.MethodInfo matMethod = matLibraryType.GetMethod("GetMaterial", new Type[] { mat.GetType() });
                                object MatLibraryInstance = matLibraryType.GetProperty("instance").GetValue(null, null);
                                object materialObject = matMethod.Invoke(MatLibraryInstance, new object[] { mat });
                                float dragC = (float)GetMemberInfoValue(materialObject.GetType().GetMember("dragCoefficient")[0], materialObject);
                                totalDrag += (1 * 100 * dragC * area / 2000f);

                            }
                            isParachute = true;
                            realChuteInUse = true;
                        }
                    }
                    if (!isParachute)
                    {
                        dragCoeff += p.mass * 0.2;
                    }
                }
                double Vt = 9999;
                if (!realChuteInUse)
                {
                    dragCoeff = dragCoeff / (totalMass);
                    Vt = Math.Sqrt(250 * 6.674e-11 * 5.2915793e22 / (((600000) ^ 2) * 1.22309485 * dragCoeff)) / 1000;
                    Debug.Log("[SR] Using Stock Module! Drag: " + dragCoeff + " Vt: " + Vt);
                }
                else
                {
                    Debug.Log("[SR] Using RealChute Module! Drag/Mass ratio: " + (totalDrag / totalMass));
                    if ((totalDrag / totalMass) >= 8)
                    {
                        Vt = 0;
                    }
                }
                if (Vt < 10.0)
                {
                    StringBuilder msg = new StringBuilder();
                    DoRecovery(v, msg);
                    msg.AppendLine("\nAdditional Information:");
                    if (realChuteInUse)
                        msg.AppendLine("RealChute Module used. Drag:Mass ratio of " + Math.Round(totalDrag / totalMass, 2) + " (>8 needed)");
                    else
                        msg.AppendLine("Stock Module used. Terminal velocity of " + Math.Round(Vt, 2) + " ( <10 needed)");
                    MessageSystem.Message m = new MessageSystem.Message("Stage Recovered", msg.ToString(), MessageSystemButton.MessageButtonColor.BLUE, MessageSystemButton.ButtonIcons.MESSAGE);
                    MessageSystem.Instance.AddMessage(m);
                    //Fire success event
                }
                else
                {
                    if (Settings.instance.ShowFailureMessages)
                    {
                        StringBuilder msg = new StringBuilder();
                        msg.AppendLine("Stage '" + v.protoVessel.vesselName + "' was destroyed!");
                        if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                        {
                            float totalCost = 0;
                            foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                            {
                                float dry, wet;
                                totalCost += ShipConstruction.GetPartCosts(pps, pps.partInfo, out dry, out wet);
                            }
                            msg.AppendLine("It was valued at " + totalCost + " Funds.");
                        }
                        msg.AppendLine("\nAdditional Information:");
                        if (realChuteInUse)
                            msg.AppendLine("RealChute Module used. Drag:Mass ratio of " + Math.Round(totalDrag / totalMass, 2) + " (>8 needed)");
                        else
                            msg.AppendLine("Stock Module used. Terminal velocity of " + Math.Round(Vt, 2) + " ( <10 needed)");

                        MessageSystem.Message m = new MessageSystem.Message("Stage Destroyed", msg.ToString(), MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.MESSAGE);
                        MessageSystem.Instance.AddMessage(m);
                    }
                    //Fire failure event
                }
            }
        }

        public void DoRecovery(Vessel v, StringBuilder msg)
        {
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                AddFunds(GetRecoveryValueForParachutes(v.protoVessel, msg));
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
            }
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


    }

    public class Settings
    {
        public static Settings instance;
        protected String filePath = KSPUtil.ApplicationRootPath + "GameData/StageRecovery/Config.txt";
        [Persistent] public float RecoveryModifier;
        [Persistent] public bool RecoverScience, RecoverKerbals, ShowFailureMessages;

        public Settings()
        {
            RecoveryModifier = 0.75f;
            RecoverKerbals = false;
            RecoverScience = false;
            ShowFailureMessages = true;
            instance = this;
        }

        public void Load()
        {
            if (System.IO.File.Exists(filePath))
            {
                ConfigNode cnToLoad = ConfigNode.Load(filePath);
                ConfigNode.LoadObjectFromConfig(this, cnToLoad);
            }
        }

        public void Save()
        {
            ConfigNode cnTemp = ConfigNode.CreateConfigFromObject(this, new ConfigNode());
            cnTemp.Save(filePath);
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