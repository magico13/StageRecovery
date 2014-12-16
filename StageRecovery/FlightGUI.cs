using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StageRecovery
{
    //This class contains all the stuff for the in-flight GUI which SR is using as it's primary display of info
    public class FlightGUI
    {
        //This variable controls whether we show the GUI
        public bool showFlightGUI = false;

        //This Rect object controls the physical window (size and location)
        public Rect flightWindowRect = new Rect((Screen.width-600)/2, (Screen.height-480)/2, 240, 480);

        //This is all stuff we need to keep constant between draws
        private int firstToolbarIndex = 0, infoBarIndex = 0;
        private Vector2 stagesScroll, infoScroll;
        private RecoveryItem selectedStage;
        //And this does the actual drawing
        public void DrawFlightGUI(int windowID)
        {
            //Start with a vertical, then a horizontal (stage list and stage info), then another vertical (stage list).
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(225));
            //Draw the toolbar that selects between recovered and destroyed stages
            int temp = firstToolbarIndex;
            firstToolbarIndex = GUILayout.Toolbar(firstToolbarIndex, new string[] { "Recovered", "Destroyed" });
            if (temp != firstToolbarIndex) NullifySelected();
            //NullifySelected will set the selectedStage to null and reset the toolbar

            //Begin listing the recovered/destryoed stages in a scroll view (so you can scroll if it's too long)
            GUILayout.Label((firstToolbarIndex == 0 ? "Recovered" : "Destroyed") + " Stages:");
            stagesScroll = GUILayout.BeginScrollView(stagesScroll, HighLogic.Skin.textArea);

            RecoveryItem deleteThis = null;
            //List all recovered stages
            if (firstToolbarIndex == 0)
            {
                foreach (RecoveryItem stage in Settings.instance.RecoveredStages)
                {
                    if (GUILayout.Button(stage.StageName))
                    {
                        if (Input.GetMouseButtonUp(0))
                        {
                            //If you select the same stage again it will minimize the list
                            if (selectedStage == stage)
                                selectedStage = null;
                            else
                                selectedStage = stage;
                        }
                        else if (Input.GetMouseButtonUp(1))
                        {
                            //Right clicking deletes the stage
                            deleteThis = stage;
                        }
                    }
                }
            }
            //List all destroyed stages
            else if (firstToolbarIndex == 1)
            {
                foreach (RecoveryItem stage in Settings.instance.DestroyedStages)
                {
                    if (GUILayout.Button(stage.StageName))
                    {
                        if (Input.GetMouseButtonUp(0))
                        {
                            //If you select the same stage again it will minimize the list
                            if (selectedStage == stage)
                                selectedStage = null;
                            else
                                selectedStage = stage;
                        }
                        else if (Input.GetMouseButtonUp(1))
                        {
                            //Right clicking deletes the stage
                            deleteThis = stage;
                        }
                    }
                }
            }

            if (deleteThis != null)
            {
                if (deleteThis == selectedStage)
                    NullifySelected();
                if (firstToolbarIndex == 0)
                    Settings.instance.RecoveredStages.Remove(deleteThis);
                else
                    Settings.instance.DestroyedStages.Remove(deleteThis);
            }

            //End the list of stages
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            //If a stage is selected we show the info for it
            if (selectedStage != null)
            {
                //Make the window larger to accomodate the info
                if (flightWindowRect.width != 600) flightWindowRect.width = 600;
                GUILayout.BeginVertical(HighLogic.Skin.textArea);
                //Show a toolbar with options for specific data, defaulting to the Parts list
                infoBarIndex = GUILayout.Toolbar(infoBarIndex, new string[] { "Parts", "Crew", "Science", "Info" });
                //List the stage name and whether it was recovered or destroyed
                GUILayout.Label("Stage name: " + selectedStage.StageName);
                GUILayout.Label("Status: " + (selectedStage.recovered ? "RECOVERED" : "DESTROYED"));
                //Put everything in a scroll view in case it is too much data for the window to display
                infoScroll = GUILayout.BeginScrollView(infoScroll);
                //Depending on the selected data view we display different things (split into different functions for ease)
                switch (infoBarIndex)
                {
                    case 0: DrawPartsInfo(); break;
                    case 1: DrawCrewInfo(); break;
                    case 2: DrawScienceInfo(); break;
                    case 3: DrawAdvancedInfo(); break;
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                //End the info side of the window
            }
            //If no stage is selected we reset the window size back to 240
            else
            {
                if (flightWindowRect.width != 240) flightWindowRect.width = 240;
            }
            
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            //End the entire window

            //Make it draggable
            if (!Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))
                GUI.DragWindow();
        }

        //Set the selected stage to null and reset the info toolbar to "Parts"
        public void NullifySelected()
        {
            selectedStage = null;
            infoBarIndex = 0;
        }

        //Draw all the info for recovered/destroyed parts
        private void DrawPartsInfo()
        {
            //List all of the parts and their recovered costs (or value if destroyed)
            GUILayout.Label("Parts on Stage:");
            for (int i=0; i<selectedStage.PartsRecovered.Count; i++)
            {
                string name = selectedStage.PartsRecovered.Keys.ElementAt(i);
                int amt = selectedStage.PartsRecovered.Values.ElementAt(i);
                float cost = selectedStage.Costs.Values.ElementAt(i);
                float percent = selectedStage.recovered ? selectedStage.RecoveryPercent :  1;
                GUILayout.Label(amt + "x " + name + " @ " + Math.Round(cost * percent, 2) + ": " + Math.Round(cost * amt * percent, 2));
            }

            //If the stage was recovered, list the refunds for parts, fuel, and total, along with the overall percentage
            if (selectedStage.recovered)
            {
                GUILayout.Label("\nTotal refunded for parts: " + Math.Round(selectedStage.DryReturns, 2));
                GUILayout.Label("Total refunded for fuel: " + Math.Round(selectedStage.FuelReturns, 2));
                GUILayout.Label("Total refunds: " + Math.Round(selectedStage.FundsReturned, 2));
                GUILayout.Label("Percent refunded: " + Math.Round(100 * selectedStage.RecoveryPercent, 2) + "%");
            }
            else //Otherwise just display the total value of the parts
            {
                GUILayout.Label("\nTotal Part Value: " + Math.Round(selectedStage.FundsOriginal, 2));
            }
        }

        //This displays what crew members were onboard the stage, if any (recovered or not)
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


        //This lists all the science experiments recovered and the total number of points
        private void DrawScienceInfo()
        {
            //List the total number of science points recovered
            GUILayout.Label("Total Science Recovered: " + (selectedStage.ScienceExperiments.Count == 0 ? "None" : selectedStage.ScienceRecovered.ToString()));
            if (selectedStage.ScienceExperiments.Count != 0)
            {
                //List all of the experiments recovered (including data amounts and titles)
                GUILayout.Label("\nExperiments:");
                foreach (string experiment in selectedStage.ScienceExperiments)
                {
                    GUILayout.Label(experiment);
                }
            }
        }

        //This displays info about distance from KSC, terminal velocity, and all that miscellanous info
        private void DrawAdvancedInfo()
        {
            //Display distance, module used, and terminal velocity
            GUILayout.Label("Distance from KSC: " + Math.Round(selectedStage.KSCDistance/1000, 2) + "km");
            GUILayout.Label("Parachute Module used: " + selectedStage.ParachuteModule);
            GUILayout.Label("Terminal velocity: "+selectedStage.Vt + " m/s");
            //List the Vt required for maximal/partial recovery
            if (Settings.instance.FlatRateModel)
            {
                GUILayout.Label("Maximum velocity for recovery: " + Settings.instance.CutoffVelocity + " m/s");
            }
            else
            {
                GUILayout.Label("Maximum velocity for recovery: " + Settings.instance.HighCut + " m/s");
                GUILayout.Label("Maximum velocity for total recovery: " + Settings.instance.LowCut + " m/s");
            }

            //List the percent refunded, broken down into distance and speed amounts
            GUILayout.Label("\nPercent refunded: "+ Math.Round(100*selectedStage.RecoveryPercent, 2) + "%");
            GUILayout.Label("    --Distance: " + Math.Round(100 * selectedStage.DistancePercent, 2) + "%");
            GUILayout.Label("    --Speed: " + Math.Round(100 * selectedStage.SpeedPercent, 2) + "%");
            GUILayout.Label("Total refunds: " + Math.Round(selectedStage.FundsReturned, 2));

            //If the stage was burned up, display this and the velocity it was going
            if (selectedStage.burnedUp)
            {
                GUILayout.Label("\nStage burned up on reentry!");
                GUILayout.Label("Surface Speed: " + selectedStage.vessel.srfSpeed);
            }

            //If powered recovery was attempted (and fuel was used) then display that and the fuel amounts consumed
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
