using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StageRecovery
{
    public class Settings
    {
        public static Settings instance;
        public SettingsGUI gui = new SettingsGUI();
        protected String filePath = KSPUtil.ApplicationRootPath + "GameData/StageRecovery/Config.txt";
        [Persistent] public float RecoveryModifier, DeadlyReentryMaxVelocity, CutoffVelocity;
        [Persistent] public bool RecoverScience, RecoverKerbals, ShowFailureMessages, ShowSuccessMessages;

        public Settings()
        {
            RecoveryModifier = 0.75f;
            RecoverKerbals = true;
            RecoverScience = true;
            ShowFailureMessages = true;
            ShowSuccessMessages = true;
            DeadlyReentryMaxVelocity = 2250f;
            CutoffVelocity = 10f;
           // instance = this;
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

    public class SettingsGUI
    {
        private bool showWindow = false;

        private static int windowWidth = 200;
        public Rect mainWindowRect = new Rect(0, 0, windowWidth, 1);

        private string DRMaxVel;
        private float recMod, cutoff;
        private bool recoverSci, recoverKerb, showFail, showSuccess;


        public ApplicationLauncherButton SRButtonStock = null;
        public void OnGUIAppLauncherReady()
        {
            bool vis;
            if (ApplicationLauncher.Ready && (SRButtonStock == null || !ApplicationLauncher.Instance.Contains(SRButtonStock, out vis))) //Add Stock button
            {
                SRButtonStock = ApplicationLauncher.Instance.AddModApplication(
                    ShowSettings,
                    hideAll,
                    DummyVoid,
                    DummyVoid,
                    DummyVoid,
                    DummyVoid,
                    ApplicationLauncher.AppScenes.SPACECENTER,
                    GameDatabase.Instance.GetTexture("StageRecovery/icon", false));
            }
        }
        void DummyVoid() { }



        public void SetGUIPositions(GUI.WindowFunction OnWindow)
        {
            if (showWindow) mainWindowRect = GUILayout.Window(8940, mainWindowRect, DrawMainGUI, "Stage Recovery", HighLogic.Skin.window);
        }

        public void DrawGUIs(int windowID)
        {
            if (showWindow) DrawMainGUI(windowID);
        }

        public void hideAll()
        {
            showWindow = false;
        }

        public void reset()
        {
            hideAll();
            mainWindowRect = new Rect(0, 0, windowWidth, 1);
        }


        public void ShowSettings()
        {
            recMod = Settings.instance.RecoveryModifier;
            cutoff = Settings.instance.CutoffVelocity;
            DRMaxVel = Settings.instance.DeadlyReentryMaxVelocity.ToString();
            recoverSci = Settings.instance.RecoverScience;
            recoverKerb = Settings.instance.RecoverKerbals;
            showFail = Settings.instance.ShowFailureMessages;
            showSuccess = Settings.instance.ShowSuccessMessages;
            showWindow = true;
        }

        private void DrawMainGUI(int windowID)
        {
            GUILayout.BeginVertical();
            
            GUILayout.Label("Recovery Modifier: "+Math.Round(100*recMod)+"%");
            //recMod = GUILayout.TextField(recMod, 4);
            recMod = GUILayout.HorizontalSlider(recMod, 0, 1);
            recMod = (float)Math.Round(recMod, 2);

            GUILayout.Label("Cutoff Velocity: " + cutoff + "m/s");
            cutoff = GUILayout.HorizontalSlider(cutoff, 2, 12);
            cutoff = (float)Math.Round(cutoff, 1);

            GUILayout.BeginHorizontal();
            GUILayout.Label("DR Velocity");
            DRMaxVel = GUILayout.TextField(DRMaxVel, 6);
            GUILayout.EndHorizontal();

            recoverSci = GUILayout.Toggle(recoverSci, "Recover Science");
            recoverKerb = GUILayout.Toggle(recoverKerb, "Recover Kerbals");
            showFail = GUILayout.Toggle(showFail, "Failure Messages");
            showSuccess = GUILayout.Toggle(showSuccess, "Success Messages");

            if (GUILayout.Button("Save"))
            {
               /* if (!float.TryParse(recMod, out Settings.instance.RecoveryModifier))
                    Settings.instance.RecoveryModifier = 0.75f;*/
                Settings.instance.RecoveryModifier = recMod;
                Settings.instance.CutoffVelocity = cutoff;
                if (!float.TryParse(DRMaxVel, out Settings.instance.DeadlyReentryMaxVelocity))
                    Settings.instance.DeadlyReentryMaxVelocity = 2000f;
                Settings.instance.RecoverScience = recoverSci;
                Settings.instance.RecoverKerbals = recoverKerb;
                Settings.instance.ShowFailureMessages = showFail;
                Settings.instance.ShowSuccessMessages = showSuccess;
                Settings.instance.Save();
            }

            GUILayout.EndVertical();

            if (!Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))
                GUI.DragWindow();
        }

        

    }
}
