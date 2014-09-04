using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StageRecovery
{
    public class FlightGUI
    {
        public bool showFlightGUI = false;

        public Rect flightWindowRect = new Rect((Screen.width-768)/2, (Screen.height-540)/2, 768, 540);

        private int firstToolbarIndex = 0, infoBarIndex = 0;
        private Vector2 stagesScroll, infoScroll;
        private RecoveryItem selectedStage;
        public void DrawFlightGUI(int windowID)
        {
            GUILayout.BeginVertical();
            int temp = firstToolbarIndex;
            firstToolbarIndex = GUILayout.Toolbar(firstToolbarIndex, new string[] { "Recovered", "Destroyed" });
            if (temp != firstToolbarIndex) NullifySelected();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(192));
            GUILayout.Label((firstToolbarIndex == 0 ? "Recovered" : "Destroyed") + " Stages:");
            stagesScroll = GUILayout.BeginScrollView(stagesScroll, HighLogic.Skin.textArea);

            //List all recovered stages
            if (firstToolbarIndex == 0)
            {
                foreach (RecoveryItem stage in Settings.instance.RecoveredStages)
                {
                    if (GUILayout.Button(stage.StageName))
                        selectedStage = stage;
                }
            }
            //List all destroyed stages
            else if (firstToolbarIndex == 1)
            {
                foreach (RecoveryItem stage in Settings.instance.DestroyedStages)
                {
                    if (GUILayout.Button(stage.StageName))
                        selectedStage = stage;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(HighLogic.Skin.textArea);
            infoBarIndex = GUILayout.Toolbar(infoBarIndex, new string[] { "Parts", "Crew", "Science", "Info" });
            if (selectedStage != null)
            {
                GUILayout.Label("Stage name: " + selectedStage.StageName);
                GUILayout.Label("Status: " + (selectedStage.recovered ? "RECOVERED" : "DESTROYED"));
                infoScroll = GUILayout.BeginScrollView(infoScroll);
                switch (infoBarIndex)
                {
                    case 0: DrawPartsInfo(); break;
                    case 1: DrawCrewInfo(); break;
                    case 2: DrawScienceInfo(); break;
                    case 3: DrawAdvancedInfo(); break;
                }
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("Select a Stage from the list on the left.");
                GUILayout.Label("", GUILayout.ExpandHeight(true));
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        public void NullifySelected()
        {
            selectedStage = null;
            infoBarIndex = 0;
        }

        private void DrawPartsInfo()
        {
            GUILayout.Label("Parts on Stage:");
            for (int i=0; i<selectedStage.PartsRecovered.Count; i++)
            {
                string name = selectedStage.PartsRecovered.Keys.ElementAt(i);
                int amt = selectedStage.PartsRecovered.Values.ElementAt(i);
                float cost = selectedStage.Costs.Values.ElementAt(i);
                float percent = selectedStage.recovered ? selectedStage.RecoveryPercent :  1;
                GUILayout.Label(amt + "x " + name + " @ " + Math.Round(cost * percent, 2) + ": " + Math.Round(cost * amt * percent, 2));
            }

            if (selectedStage.recovered)
            {
                GUILayout.Label("\nTotal refunded for parts: " + Math.Round(selectedStage.DryReturns, 2));
                GUILayout.Label("Total refunded for fuel: " + Math.Round(selectedStage.FuelReturns, 2));
                GUILayout.Label("Total refunds: " + Math.Round(selectedStage.FundsReturned, 2));
                GUILayout.Label("Percent refunded: " + Math.Round(100 * selectedStage.RecoveryPercent, 2) + "%");
            }
            else
            {
                GUILayout.Label("\nTotal Part Value: " + Math.Round(selectedStage.FundsOriginal, 2));
            }
        }

        private void DrawCrewInfo()
        {
            GUILayout.Label("Crew Onboard:");
            if (selectedStage.KerbalsOnboard.Count == 0)
                GUILayout.Label("None");
            else
            {
                foreach (string kerbal in selectedStage.KerbalsOnboard)
                {
                    GUILayout.Label(kerbal);
                }
            }
        }

        private void DrawScienceInfo()
        {
            GUILayout.Label("Total Science Recovered: " + (selectedStage.ScienceExperiments.Count == 0 ? "None" : selectedStage.ScienceRecovered.ToString()));
            if (selectedStage.ScienceExperiments.Count != 0)
            {
                GUILayout.Label("\nExperiments:");
                foreach (string experiment in selectedStage.ScienceExperiments)
                {
                    GUILayout.Label(experiment);
                }
            }
        }

        private void DrawAdvancedInfo()
        {
            GUILayout.Label("Distance from KSC: " + Math.Round(selectedStage.KSCDistance/1000, 2) + "km");
            GUILayout.Label("Parachute Module used: " + selectedStage.ParachuteModule);
            GUILayout.Label("Terminal velocity: "+selectedStage.Vt + " m/s");
            if (Settings.instance.FlatRateModel)
            {
                GUILayout.Label("Maximum velocity for recovery: " + Settings.instance.CutoffVelocity + " m/s");
            }
            else
            {
                GUILayout.Label("Maximum velocity for recovery: " + Settings.instance.HighCut + " m/s");
                GUILayout.Label("Maximum velocity for total recovery: " + Settings.instance.LowCut + " m/s");
            }

            GUILayout.Label("\nPercent Recovered: "+ Math.Round(100*selectedStage.RecoveryPercent, 2) + "%");
            GUILayout.Label("    --Distance: " + Math.Round(100 * selectedStage.DistancePercent, 2) + "%");
            GUILayout.Label("    --Speed: " + Math.Round(100 * selectedStage.SpeedPercent, 2) + "%");

            if (selectedStage.burnedUp)
            {
                GUILayout.Label("\nStage burned up on reentry!");
                GUILayout.Label("Orbital Velocity: " + selectedStage.vessel.obt_speed);
            }

            if (selectedStage.poweredRecovery)
            {
                GUILayout.Label("\nPowered recovery was attempted.");
                GUILayout.Label("Fuel consumed:");
                foreach (KeyValuePair<string, float> fuel in selectedStage.fuelUsed)
                {
                    GUILayout.Label(fuel.Key + " : " + fuel.Value + " units");
                }
            }
        }
    }
}
