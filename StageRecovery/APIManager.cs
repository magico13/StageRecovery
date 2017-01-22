using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;

namespace StageRecovery
{
    //This class is the internal side of the API. You could use it to have a hard dependency if you wanted, but the Wrapper allows for soft dependencies.
    public sealed class APIManager
    {
        //This is the actual instance. It gets instantiated when someone calls for it, below.
        private static readonly APIManager instance_ = new APIManager();

        //This is the public reference to the instance. Nobody else can change the instance, it's read only.
        public static APIManager instance
        {
            //get and set let you get the value or set the value. Providing only one (here: get) makes it read only or write only.
            get
            {
                return instance_;
            }
        }

        //These are the two events. They are fired whenever appropriate, which means they activate all the listening methods.
        public RecoveryEvent RecoverySuccessEvent = new RecoveryEvent();
        public RecoveryEvent RecoveryFailureEvent = new RecoveryEvent();
        public RecoveryProcessingEvent OnRecoveryProcessingStart = new RecoveryProcessingEvent();
        public RecoveryProcessingEvent OnRecoveryProcessingFinish = new RecoveryProcessingEvent();


        public bool SREnabled
        {
            get
            {
                return Settings.Instance.SREnabled;
            }
        }

        public double ComputeTerminalVelocity_ProtoParts(List<ProtoPartSnapshot> partList)
        {
            return StageRecovery.ProcessPartList(partList);
        }

        public double ComputeTerminalVelocity_Parts(List<Part> partList)
        {
            return StageRecovery.ProcessPartList(partList);
        }
    }
    
   //The RecoveryEvent class is used by both events. It basically just lets you add a listening method to the event, remove one, or fire all the events.
    public class RecoveryEvent
    {
        //This is the list of methods that should be activated when the event fires
        private List<Action<Vessel, float[], string>> listeningMethods = new List<Action<Vessel, float[], string>>();

        //This adds an event to the List of listening methods
        public void Add(Action<Vessel, float[], string> method)
        {
            //We only add it if it isn't already added. Just in case.
            if (!listeningMethods.Contains(method))
                listeningMethods.Add(method);
        }

        //This removes and event from the List
        public void Remove(Action<Vessel, float[], string> method)
        {
            //We also only remove it if it's actually in the list.
            if (listeningMethods.Contains(method))
                listeningMethods.Remove(method);
        }

        //This fires the event off, activating all the listening methods.
        public void Fire(Vessel vessel, float[] infoArray, string reason)
        {
            //Loop through the list of listening methods and Invoke them.
            foreach (Action<Vessel, float[], string> method in listeningMethods)
                method.Invoke(vessel, infoArray, reason);
        }
    }

    //This basically just lets you add a listening method to the event, remove one, or fire all the events.
    public class RecoveryProcessingEvent
    {
        //This is the list of methods that should be activated when the event fires
        private List<Action<Vessel>> listeningMethods = new List<Action<Vessel>>();

        //This adds an event to the List of listening methods
        public void Add(Action<Vessel> method)
        {
            //We only add it if it isn't already added. Just in case.
            if (!listeningMethods.Contains(method))
                listeningMethods.Add(method);
        }

        //This removes and event from the List
        public void Remove(Action<Vessel> method)
        {
            //We also only remove it if it's actually in the list.
            if (listeningMethods.Contains(method))
                listeningMethods.Remove(method);
        }

        //This fires the event off, activating all the listening methods.
        public void Fire(Vessel vessel)
        {
            //Loop through the list of listening methods and Invoke them.
            foreach (Action<Vessel> method in listeningMethods)
                method.Invoke(vessel);
        }
    }
}
/*
Copyright (C) 2015  Michael Marvin

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