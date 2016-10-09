using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


namespace StageRecovery
{
    public class EditorGUI
    {
        public List<EditorStatItem> stages = new List<EditorStatItem>();
        public bool showEditorGUI = false;
        bool highLight = false, tanksDry = true;
        public Rect EditorGUIRect = new Rect(Screen.width / 3, Screen.height / 3, 250, 1);

        public void DrawEditorGUI(int windowID)
        {
            GUILayout.BeginVertical();
            //provide toggles to turn highlighting on/off
            if (GUILayout.Button("Toggle Vessel Highlighting"))
            {
                highLight = !highLight;
                if (highLight)
                    HighlightAll();
                else
                    UnHighlightAll();
            }

            if (GUILayout.Button("Tanks: "+(tanksDry? "Empty":"Full")))
            {
                tanksDry = !tanksDry;
                if (highLight)
                    HighlightAll();
            }

            //list each stage, with info for each
            foreach (EditorStatItem stage in stages)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Stage " + stage.stageNumber);
                double vel = tanksDry ? stage.EmptyVelocity : stage.FullVelocity;
                GUILayout.Label(vel.ToString("N1")+" m/s");
                GUILayout.Label(stage.GetRecoveryPercent(tanksDry) + "%");
            //    GUILayout.Label("("+stage.FullVelocity.ToString("N1") + ")");
                if (GUILayout.Button("Highlight"))
                {
                    //highlight this stage and unhighlight all others
                    bool status = stage.Highlighted;
                    if (highLight)
                        status = false;
                    UnHighlightAll();
                    stage.SetHighlight(!status, tanksDry);
                }
                GUILayout.EndHorizontal();
            }


            if (GUILayout.Button("Recalculate"))
            {
                BreakShipIntoStages();
                if (highLight)
                    HighlightAll();

                EditorGUIRect.height = 1; //reset the height so it is the smallest size it needs to be 
            }
            GUILayout.EndVertical();

           /* if (GUI.Button(new Rect(EditorGUIRect.xMax-10, EditorGUIRect.yMin, 10, 10), "X"))
            {
                UnHighlightAll();
                showEditorGUI = false;
            }*/

            //Make it draggable
            if (!Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))
                GUI.DragWindow();
        }

        public void UnHighlightAll()
        {
            highLight = false;
            foreach (EditorStatItem stage in stages)
                stage.UnHighlight();
        }

        public void HighlightAll()
        {
            highLight = true;
            foreach (EditorStatItem stage in stages)
                stage.Highlight(tanksDry);
        }

        public void BreakShipIntoStages()
        {
            stages.Clear();
            //loop through the part tree and try to break it into stages
            List<Part> parts = EditorLogic.fetch.ship.parts;
            EditorStatItem current = new EditorStatItem();
            int stageNum = 0;
            bool realChuteInUse = false;

            StageParts stage = new StageParts();
            List<Part> RemainingDecouplers = new List<Part>() { parts[0] };
            while (RemainingDecouplers.Count > 0)
            {
                //determine stages from the decouplers
                Part parent = RemainingDecouplers[0];
                RemainingDecouplers.RemoveAt(0);
                stage = DetermineStage(parent);
                current = new EditorStatItem();
                current.stageNumber = stageNum++;
                current.parts = stage.parts;
                RemainingDecouplers.AddRange(stage.decouplers);

                //compute properties
                foreach (Part part in stage.parts)
                {
                    current.dryMass += part.mass;
                    current.mass += part.mass + part.GetResourceMass();

                    double pChutes = 0;
                    if (part.Modules.Contains("RealChuteModule"))
                    {
                        PartModule realChute = part.Modules["RealChuteModule"];
                        ConfigNode rcNode = new ConfigNode();
                        realChute.Save(rcNode);

                        //This is where the Reflection starts. We need to access the material library that RealChute has, so we first grab it's Type
                        Type matLibraryType = AssemblyLoader.loadedAssemblies
                            .SelectMany(a => a.assembly.GetExportedTypes())
                            .SingleOrDefault(t => t.FullName == "RealChute.Libraries.MaterialsLibrary.MaterialsLibrary");


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
                            object MatLibraryInstance = matLibraryType.GetProperty("Instance").GetValue(null, null);
                            //With the library instance we can invoke the GetMaterial method (passing the name of the material as a parameter) to receive an object that is the material
                            object materialObject = matMethod.Invoke(MatLibraryInstance, new object[] { mat });
                            //With that material object we can extract the dragCoefficient using the helper function above.
                            float dragC = (float)StageRecovery.GetMemberInfoValue(materialObject.GetType().GetMember("DragCoefficient")[0], materialObject);
                            //Now we calculate the RCParameter. Simple addition of this doesn't result in perfect results for Vt with parachutes with different diameter or drag coefficients
                            //But it works perfectly for mutiple identical parachutes (the normal case)
                            pChutes += dragC * (float)Math.Pow(diameter, 2);
                        }
                        realChuteInUse = true;
                    }
                    else if (part.Modules.Contains("RealChuteFAR")) //RealChute Lite for FAR
                    {
                        PartModule realChute = part.Modules["RealChuteFAR"];
                        float diameter = (float)realChute.Fields.GetValue("deployedDiameter");
                        // = float.Parse(realChute.moduleValues.GetValue("deployedDiameter"));
                        float dragC = 1.0f; //float.Parse(realChute.moduleValues.GetValue("staticCd"));
                        pChutes += dragC * (float)Math.Pow(diameter, 2);

                        realChuteInUse = true;
                    }
                    else if (!realChuteInUse && part.Modules.Contains("ModuleParachute"))
                    {
                        double scale = 1.0;
                        //check for Tweakscale and modify the area appropriately
                        if (part.Modules.Contains("TweakScale"))
                        {
                            PartModule tweakScale = part.Modules["TweakScale"];
                            double currentScale = 100, defaultScale = 100;
                            double.TryParse(tweakScale.Fields.GetValue("currentScale").ToString(), out currentScale);
                            double.TryParse(tweakScale.Fields.GetValue("defaultScale").ToString(), out defaultScale);
                            scale = currentScale / defaultScale;
                        }

                        ModuleParachute mp = (ModuleParachute)part.Modules["ModuleParachute"];
                        //dragCoeff += part.mass * mp.fullyDeployedDrag;
                        pChutes += mp.areaDeployed * Math.Pow(scale, 2);
                    }

                    current.chuteArea += pChutes;
                }

                stages.Add(current);
            }

            ConsolidateStages();
            Debug.Log("[SR] Found " + stages.Count + " stages!");
        }

        StageParts DetermineStage(Part parent)
        {
            StageParts stage = new StageParts();
            List<Part> toCheck = new List<Part>() { parent };
            while (toCheck.Count > 0) //should instead search through the children, stopping when finding a decoupler, then switch to it's children
            {
                Part checking = toCheck[0];
                toCheck.RemoveAt(0);
                stage.parts.Add(checking);

                foreach (Part part in checking.children)
                {
                    //search for decouplers
                    //if (part.Modules.Contains("ModuleDecouple") || part.Modules.Contains("ModuleAnchoredDecoupler"))
                    if (part.FindModulesImplementing<IStageSeparator>().Count > 0)
                    {
                        stage.decouplers.Add(part);
                    }
                    else
                    {
                        toCheck.Add(part);
                    }
                }
            }
            return stage;
        }

        public void ConsolidateStages()
        {
            //finds identical (and adjacent) stages in the list and merges them into a single master stage
            //must find all identical stages first, then merge

            EditorStatItem compareItem = null;

            for (int i=0; i<stages.Count; i++)
            {
                EditorStatItem stage = stages[i];
              //  if (compareItem == null)
                compareItem = stage;

                int j = i+1;
                while (j < stages.Count)
                {
                    if (stages[j].parts.Count != compareItem.parts.Count || stages[j].mass != compareItem.mass || stages[j].chuteArea != compareItem.chuteArea)
                    {
                        //probably not the same stage setup
                        break;
                    }
                    j++;
                }

                if (j > i+1)
                {
                    Debug.Log("[SR] Found " + (j - i) + " identical stages");
                    //some stages are the same (up to j)
                    //merge the stages
                    for (int k = j-1; k>i; k--)
                    {
                        //add the parts from k to i
                        stages[i].parts.AddRange(stages[k].parts);
                        stages.RemoveAt(k);
                    }
                    stages[i].ForceRecalculate();
                }
            }
        }
    }

    public class StageParts
    {
        public List<Part> parts = new List<Part>();
        public List<Part> decouplers = new List<Part>();
    }

    public class EditorStatItem
    {
        public int stageNumber=0;
        public double dryMass=0, mass=0, chuteArea=0;
        private double _FullVelocity = -1, _DryVelocity = -1;
        private bool _highlighted = false;

        public bool Highlighted
        {
            get
            {
                return _highlighted;
            }
        }
        public double FullVelocity
        {
            get
            {
                if (_FullVelocity < 0)
                    _FullVelocity = GetVelocity(false);
                return _FullVelocity;
            }
        }

        public double EmptyVelocity
        {
            get
            {
                if (_DryVelocity < 0)
                    _DryVelocity = GetVelocity(true);
                return _DryVelocity;
            }
        }


        public List<Part> parts = new List<Part>();

        public void Set(List<Part> StageParts, int StageNum, double DryMass, double Mass, double ChuteArea)
        {
            parts = StageParts;
            stageNumber = StageNum;
            dryMass = DryMass;
            mass = Mass;
            chuteArea = ChuteArea;
        }

        private double GetVelocity(bool dry=true)
        {
            if (dry)
                return StageRecovery.VelocityEstimate(dryMass, chuteArea);
            else
                return StageRecovery.VelocityEstimate(mass, chuteArea);
        }

        public double GetRecoveryPercent(bool dry=true)
        {
            double Vt = GetVelocity(dry);
            bool recovered = false;
            if (Settings.Instance.FlatRateModel)
                recovered = Vt < Settings.Instance.CutoffVelocity;
            else
                recovered = Vt < Settings.Instance.HighCut;

            if (!recovered)
                return 0;

            double recoveryPercent = 0;
            if (recovered && Settings.Instance.FlatRateModel) recoveryPercent = 1;
            else if (recovered && !Settings.Instance.FlatRateModel) recoveryPercent = RecoveryItem.GetVariableRecoveryValue(Vt);

            return Math.Round(100 * recoveryPercent, 2);
        }

        public void Highlight(bool dry=true)
        {
            double vel = dry ? EmptyVelocity : FullVelocity;
            UnityEngine.Color stageColor = UnityEngine.Color.red;
            if (vel < Settings.Instance.HighCut)
                stageColor = UnityEngine.Color.yellow;
            if (vel < Settings.Instance.LowCut)
                stageColor = UnityEngine.Color.green;
            //Part p = parts[0];
            foreach (Part p in parts)
            {
                p.SetHighlight(true, false);
                p.SetHighlightColor(stageColor);
                p.SetHighlightType(Part.HighlightType.AlwaysOn);
            }
            _highlighted = true;
        }

        public void UnHighlight()
        {
            foreach (Part p in parts)
            {
                //p.SetHighlightColor(UnityEngine.Color.green);
                //p.SetHighlight(false, false);
                //p.SetHighlightType();
                p.SetHighlightDefault();
            }
            _highlighted = false;
        }

        public void SetHighlight(bool status, bool dry = true)
        {
            if (status)
                Highlight(dry);
            else
                UnHighlight();
        }

        public bool ToggleHighlight()
        {
            if (_highlighted)
                UnHighlight();
            else
                Highlight();

            return _highlighted;
        }

        public void ForceRecalculate()
        {
            _FullVelocity = GetVelocity(false);
            _DryVelocity = GetVelocity(true);
        }
    }
}
