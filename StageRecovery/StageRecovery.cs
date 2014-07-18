using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP;
using UnityEngine;

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
            Settings.instance.Save();
        }

        

        public double AddFunds(double toAdd)
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                return 0;
            Debug.Log("[SR] Adding funds: " + toAdd + ", New total: " + (Funding.Instance.Funds + toAdd));
            return (Funding.Instance.Funds += toAdd);
        }

        public float GetRecoveryValueForParachutes(ProtoVessel pv)
        {
            double distanceFromKSC = SpaceCenter.Instance.GreatCircleDistance(SpaceCenter.Instance.cb.GetRelSurfaceNVector(pv.latitude, pv.longitude));
            double maxDist = SpaceCenter.Instance.cb.Radius * Math.PI;
            float recoveryPercent = 0.75f * Mathf.Lerp(0.98f, 0.1f, (float)(distanceFromKSC / maxDist));
            float totalReturn = 0;
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                float dryCost, fuelCost;
                totalReturn += ShipConstruction.GetPartCosts(pps, pps.partInfo, out dryCost, out fuelCost);
            }
            float totalBeforeReturn = (float)Math.Round(totalReturn, 2);
            totalReturn *= recoveryPercent;
            totalReturn = (float)Math.Round(totalReturn, 2);
            Debug.Log("[SR] '"+pv.vesselName+"' being recovered by SR. Percent returned: " + 100 * recoveryPercent + "%. Distance from KSC: " + Math.Round(distanceFromKSC / 1000, 2) + " km");
            Debug.Log("[SR] Funds being returned: " + totalReturn + "/" + totalBeforeReturn);
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
                        //Debug.Log("[SR] Found parachute module on " + p.partInfo.name);
                        ModuleParachute mp = (ModuleParachute)p.modules.First(mod => mod.moduleName == "ModuleParachute").moduleRef;
                        dragCoeff += p.mass * mp.fullyDeployedDrag;
                        isParachute = true;
                    }
                    if (ModuleNames.Contains("RealChuteModule"))
                    {
                        //Debug.Log("[SR] Found realchute module on " + p.partInfo.name);
                        PartModule realChute = p.modules.First(mod => mod.moduleName == "RealChuteModule").moduleRef;
                        Type rCType = realChute.GetType();
                        if ((object)realChute != null)
                        {
                            System.Reflection.MemberInfo member = rCType.GetMember("deployedDiameter")[0];
                            float area = (float)GetMemberInfoValue(member, realChute);
                            area = Mathf.PI * Mathf.Pow(area / 2, 2); //Determine the area manually since the "deployedArea" parameter no longer exists in RC
                            Debug.Log("Chute area: " + area);

                            member = rCType.GetMember("material")[0];
                            string mat = (string)GetMemberInfoValue(member, realChute);
                            Debug.Log("Material is " + mat);

                            Type matLibraryType = AssemblyLoader.loadedAssemblies
                                .SelectMany(a => a.assembly.GetExportedTypes())
                                .SingleOrDefault(t => t.FullName == "RealChute.Libraries.MaterialsLibrary");

                            System.Reflection.MethodInfo matMethod = matLibraryType.GetMethod("GetMaterial", new Type[] { mat.GetType() });
                            object MatLibraryInstance = matLibraryType.GetProperty("instance").GetValue(null, null);
                            object materialObject = matMethod.Invoke(MatLibraryInstance, new object[] { mat });

                            float dragC = (float)GetMemberInfoValue(materialObject.GetType().GetMember("dragCoefficient")[0], materialObject);
                            Debug.Log("dragC: " + dragC);
                            isParachute = true;
                            realChuteInUse = true;
                            totalDrag += (1 * 100 * dragC * area / 2000f);
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
                    //Debug.Log("[SR] Recovered parts from " + v.vesselName);
                    AddFunds(GetRecoveryValueForParachutes(v.protoVessel));
                    //Fire success event
                }
                else
                {
                    //Fire failure event
                }
            }
        }
    }

    public class Settings
    {
        public static Settings instance;
        protected String filePath = KSPUtil.ApplicationRootPath + "GameData/StageRecovery/Config.txt";
        [Persistent] public float RecoveryModifier;

        public Settings()
        {
            RecoveryModifier = 0.75f;
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