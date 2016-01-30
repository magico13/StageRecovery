using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StageRecovery
{
    public class PhysicsHelpers
    {
        //Attempts to find the altitude on the provided CelestialBody that has the provided atmospheric density
        public static float ComputeCutoffAlt(CelestialBody body, float cutoffDensity, float stepSize = 100)
        {
            //This unfortunately doesn't seem to be coming up with the right altitude for Kerbin (~23km, it finds ~27km)
            double dens = 0;
            float alt = (float)body.atmosphereDepth;
            while (alt > 0)
            {
                dens = body.GetDensity(FlightGlobals.getStaticPressure(alt, body), body.atmosphereTemperatureCurve.Evaluate(alt)); //body.atmospherePressureCurve.Evaluate(alt)
                //Debug.Log("[SR] Alt: " + alt + " Pres: " + dens);
                if (dens < cutoffDensity)
                    alt -= stepSize;
                else
                    break;
            }
            return alt;
        }

        //Function to estimate the final velocity given a stage's mass and parachute info
        public static double VelocityEstimate(double mass, double chuteInfo, bool RealChute = false)
        {
            if (chuteInfo <= 0)
                return 200;
            if (mass <= 0)
                return 0;

            return calcTerminalVelocity(mass, chuteInfo);
           /* if (!RealChute) //This is by trial and error
                return (63 * Math.Pow(mass / chuteInfo, 0.4));
            else //This is according to the formulas used by Stupid_Chris in the Real Chute drag calculator program included with Real Chute. Source: https://github.com/StupidChris/RealChute/blob/master/Drag%20Calculator/RealChute%20drag%20calculator/RCDragCalc.cs
                return Math.Sqrt((8000 * mass * 9.8) / (1.223 * Math.PI * chuteInfo));*/
        }

        public static double calcTerminalVelocity(double tons, double CdASum)
        {
            return Math.Sqrt((2 * (tons * 1000) * 9.81) / (1.22 * CdASum));
        }

        //thank you ShadowMage!
        public static double calcTerminalVelocity(double kilograms, double rho, double cD, double area)
        {
            return Math.Sqrt((2f * kilograms * 9.81f) / (rho * area * cD));
        }

        public static double calcDragKN(double rho, double cD, double velocity, double area)
        {
            return calcDynamicPressure(rho, velocity) * area * cD * 0.001f;
        }

        public static double calcDynamicPressure(double rho, double velocity)
        {
            return 0.5f * rho * velocity * velocity;
        }
    }
}
