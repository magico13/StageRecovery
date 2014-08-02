using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;

namespace StageRecovery
{
    public class APIManager
    {
        private static APIManager instance_ = null;
        public static APIManager instance
        {
            get
            {
                if (instance_ == null) instance_ = new APIManager();
                return instance_;
            }
        }

        public RecoveryEvent RecoverySuccessEvent = new RecoveryEvent();
        public RecoveryEvent RecoveryFailureEvent = new RecoveryEvent();


    }
    
   
    public class RecoveryEvent
    {
        private List<Action<Vessel, float[], string>> listeningMethods = new List<Action<Vessel, float[], string>>();

        public void Add(Action<Vessel, float[], string> method)
        {
            //Debug.Log("[SR] Adding method");
            if (!listeningMethods.Contains(method))
                listeningMethods.Add(method);
        }

        public void Remove(Action<Vessel, float[], string> method)
        {
            //Debug.Log("[SR] Removing method");
            if (listeningMethods.Contains(method))
                listeningMethods.Remove(method);
        }

        public void Fire(Vessel vessel, float[] infoArray, string reason)
        {
            //Debug.Log("[SR] Firing");
            foreach (Action<Vessel, float[], string> method in listeningMethods)
                method.Invoke(vessel, infoArray, reason);
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