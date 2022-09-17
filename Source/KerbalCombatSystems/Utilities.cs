using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalCombatSystems
{
    public static partial class KCS
    {
        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
        }

        public static float GetMaxAcceleration(Vessel v)
        {
            List<ModuleEngines> Engines = v.FindPartModulesImplementing<ModuleEngines>();
            float Thrust = Engines.Sum(e => e.MaxThrustOutputVac(true));

            List<ModuleRCSFX> RCS = v.FindPartModulesImplementing<ModuleRCSFX>();
            foreach (ModuleRCSFX Thruster in RCS)
            {
                if (Thruster.useThrottle && !Thruster.flameout)
                {
                    Thrust += Thruster.thrusterPower;
                }
            }

            return Thrust / v.GetTotalMass();
        }

        public static bool RayIntersectsVessel(Vessel v, Ray r)
        {
            foreach (Part p in v.parts)
            {
                foreach (Bounds b in p.GetColliderBounds())
                {
                    if (b.IntersectRay(r)) return true;
                }
            }

            return false;
        }

        public static Vector3 TargetLead(Vessel Target, Part Firer, float TravelVelocity)
        {
            Vector3 RelPos = Target.transform.position - Firer.transform.position;
            Vector3 RelVel = Target.GetObtVelocity() - Firer.vessel.GetObtVelocity();

            // Quadratic equation coefficients a*t^2 + b*t + c = 0
            float a = Vector3.Dot(RelVel, RelVel) - TravelVelocity * TravelVelocity;
            float b = 2f * Vector3.Dot(RelVel, RelPos);
            float c = Vector3.Dot(RelPos, RelPos);

            float desc = b * b - 4f * a * c;

            float ForwardDelta = 2f * c / (Mathf.Sqrt(desc) - b);

            Vector3 leadPosition = Target.transform.position + RelVel * ForwardDelta;
            return leadPosition - Firer.transform.position;
        }

        
        public static Vector3 GetAwayVector(Part WeaponPart)
        {
            //function to return a part vector that points away from its parent, used for weapon based aiming on ships
            Vector3 TrueVector = WeaponPart.transform.up.normalized * 15f + WeaponPart.transform.position;
            //TrueVector = TrueVector.normalized * 15f;
            //get vector pointing towards parent from child
            Vector3 targetDir = WeaponPart.parent.transform.position + WeaponPart.transform.position;

            //if the up vector of the part points towards the parent then the part is backwards and we reverse the vector
            if (Vector3.Angle(targetDir, TrueVector) > 90f)
            {
                TrueVector = -TrueVector;
            }

            return TrueVector;
        }

        public static void TryToggle(bool Direction, ModuleAnimationGroup Animation)
        {
            if (Direction && Animation.isDeployed == false)
            {
                //try deploy if not already
                Animation.DeployModule();
            }
            else if(!Direction && Animation.isDeployed == true)
            {
                //try retract if not already
                Animation.RetractModule();
            }

            //do nothing otherwise
        }

        public static List<Seperator> FindDecouplerChildren(Part Root, string type, bool ignoreTypeRequirement)
        {
            //run through all child parts of the controllers parent for decoupler modules
            List<Part> ChildParts = Root.FindChildParts<Part>(true).ToList();
            //check the parent itself
            ChildParts.Add(Root);

            //spawn empty modules list to add to
            List<Seperator> SeperatorList = new List<Seperator>();
            ModuleDecouplerDesignate Module;

            ModuleDecouple Decoupler;
            ModuleDockingNode DockingPort;

            foreach (Part CurrentPart in ChildParts)
            {
                Module = CurrentPart.GetComponent<ModuleDecouplerDesignate>();

                //check current part for either the docking port or the decoupler module
                Decoupler = CurrentPart.GetComponent<ModuleDecouple>();
                DockingPort = CurrentPart.GetComponent<ModuleDockingNode>();

                //move to next part if neither are found
                if (Decoupler == null && DockingPort == null) continue;
                Seperator seperator = new Seperator();

                //get the designator module and move to next part if it is not the correct type
                Module = CurrentPart.GetComponent<ModuleDecouplerDesignate>();
                if (Module == null) continue;
                if (Module.DecouplerType != type && !ignoreTypeRequirement) continue;

                //cases for if it is a decoupler or a docking port
                if (Decoupler != null)
                {
                    //ensure it's not already been fired
                    if (CurrentPart.GetComponent<ModuleDecouple>().isDecoupled == true) continue;

                    seperator.decoupler = Decoupler;
                }
                else
                {
                    //todo: skip if attached to another parent docking port

                    seperator.port = DockingPort;
                    seperator.isDockingPort = true;
                }

                seperator.part = CurrentPart;
                SeperatorList.Add(seperator);
            }

            return SeperatorList;
        }

        public static Seperator FindDecoupler(Part origin, string type, bool ignoreTypeRequirement)
        {
            Part CurrentPart;
            Part NextPart = origin.parent;
            ModuleDecouple Decoupler;
            ModuleDockingNode DockingPort;
            ModuleDecouplerDesignate Module;

            if (NextPart == null) return null;

            for (int i = 0; i < 99; i++)
            {
                CurrentPart = NextPart;
                NextPart = CurrentPart.parent;

                //if there are no more parts to check end the sequence
                if (NextPart == null) break;

                //check current part for either the docking port or the decoupler module
                Decoupler = CurrentPart.GetComponent<ModuleDecouple>();
                DockingPort = CurrentPart.GetComponent<ModuleDockingNode>();

                //move to next part if neither are found
                if (Decoupler == null && DockingPort == null) continue;
                Seperator seperator = new Seperator();

                //get the designator module and move to next part if it is not the correct type
                Module = CurrentPart.GetComponent<ModuleDecouplerDesignate>();
                if (Module == null) continue;
                if (Module.DecouplerType != type && !ignoreTypeRequirement) continue;


                //cases for if it is a decoupler or a docking port
                if (Decoupler != null)
                {
                    //ensure it's not already been fired
                    if (CurrentPart.GetComponent<ModuleDecouple>().isDecoupled == true) continue;

                    seperator.decoupler = Decoupler;
                }
                else
                {
                    seperator.port = DockingPort;
                    seperator.isDockingPort = true;
                }

                seperator.part = CurrentPart;
                return seperator;
            }

            return null;
        }

        public static Vector3 FromTo(Vessel v1, Vessel v2)
        {
             return v2.transform.position - v1.transform.position;
        }

        public static Vector3 RelVel(Vessel v1, Vessel v2)
        {
            return v1.GetObtVelocity() - v2.GetObtVelocity();
        }

        public static Vector3 AngularAcceleration(Vector3 torque, Vector3 MoI)
        {
            return new Vector3(MoI.x.Equals(0) ? float.MaxValue : torque.x / MoI.x,
                MoI.y.Equals(0) ? float.MaxValue : torque.y / MoI.y,
                MoI.z.Equals(0) ? float.MaxValue : torque.z / MoI.z);
        }

        public static float Integrate(float d, float a, float i = 0.1f, float v = 0)
        {
            float t = 0;

            while (d > 0)
            {
                v = v + a * i;
                d = d - v * i;
                t = t + i;
            }

            return t;
        }

        public static float SolveTime(float distance, float acceleration, float vel = 0)
        {
            float a = 0.5f * acceleration;
            float b = vel;
            float c = Mathf.Abs(distance) * -1;

            float x = (-b + Mathf.Sqrt(b * b - 4 * a * c)) / (2 * a);

            return x;
        }

        public static float SolveDistance(float time, float acceleration, float vel = 0)
        {
            return (vel * time) + 0.5f * acceleration * Mathf.Pow(time, 2);
        }

        public static float AngularVelocity(Vessel v, Vessel t)
        {
            Vector3 tv1 = FromTo(v, t);
            Vector3 tv2 = tv1 + RelVel(v, t);
            return Vector3.Angle(tv1.normalized, tv2.normalized);
        }

        public static string ShorternName(string name)
        {
            name = name.Split('(').First();
            name = name.Split('[').First();
            name = name.Replace(" - ", " ");
            name = name.Replace("-class", "");
            name = name.Replace("Heavy ", "");
            name = name.Replace("Light ", "");
            name = name.Replace("Frigate", "");
            name = name.Replace("Destroyer", "");
            name = name.Replace("Cruiser", "");
            name = name.Replace("Dreadnought", "");
            name = name.Replace("Corvette", "");
            name = name.Replace("Carrier", "");
            name = name.Replace("Battleship", "");
            name = name.Replace("Fighter", "");
            name = name.Replace("  ", " ");
            name = name.Trim();

            return name;
        }

        public static ModuleCommand FindCommand(Vessel craft)
        {
            //get a list of onboard control points and return the first found
            List<ModuleCommand> CommandPoints = craft.FindPartModulesImplementing<ModuleCommand>();
            if (CommandPoints.Count != 0)
            {
                return CommandPoints.First();
            }
            //gotta have a command point somewhere so this is just for compiling
            return null;
        }
    }
       
    /*public class KCSShip
    {
        public Vessel v;
        public float initialMass;

        public KCSShip(Vessel ship, float mass)
        {
            v = ship;
            initialMass = mass;
        }
    }*/

    public enum Side
    {
        A,
        B
    }

    public class Seperator
    {
        public bool isDockingPort = false;
        public ModuleDockingNode port;
        public ModuleDecouple decoupler;
        public Part part;
        public Transform transform { get => part.transform; }

        public void Separate()
        {
            // figure out which one to use#

            if (isDockingPort)
            {
                //port.Undock();
                //decoupler.ejectionForcePercent = 100;
                port.Decouple();
            }
            else
            {
                decoupler.Decouple();
            }
        }
    }
}
