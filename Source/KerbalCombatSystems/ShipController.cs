﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static KerbalCombatSystems.KCS;
using Random = UnityEngine.Random;

namespace KerbalCombatSystems
{
    public class ModuleShipController : PartModule
    {
        // User parameters changed via UI.

        const string shipControllerGroupName = "Ship AI";
        public bool controllerRunning = false;
        public float updateInterval = 2.5f;

        // Ship AI variables.

        private Coroutine shipControllerCoroutine;
        private Coroutine behaviourCoroutine;
        private KCSFlightController fc;
        private KCSController controller;
        public Vessel target;
        private ModuleShipController targetController;
        public List<ModuleWeaponController> weapons;
        private float weaponsMinRange;
        private float weaponsMaxRange;
        private float lastFired;
        private float fireInterval;
        private float maxDetectionRange;
        public float initialMass;
        private bool hasPropulsion;
        private bool hasWeapons;
        private float lastUpdate;
        private double minSafeAltitude;
        public string state = "Init";
        private ModuleWeaponController currentProjectile;
        private bool roboticsDeployed;
        private List<ModuleWeaponController> incomingWeapons;
        private List<Tuple<ModuleWeaponController, float>> shouldDodgeWeapons;
        private float shipLength;
        private Vector3 maxAngularAcceleration;
        private float maxAcceleration;

        [KSPField(isPersistant = true)]
        public Side side;

        [KSPField(isPersistant = true)]
        public bool alive = true;

        [KSPField(isPersistant = true)]
        private bool DeployedSensors;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Manoeuvring Speed",
            guiUnits = "m/s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_FloatRange(
                minValue = 10f,
                maxValue = 500f,
                stepIncrement = 10f,
                scene = UI_Scene.All
            )]
        public float manoeuvringSpeed = 100f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Firing Speed",
            guiUnits = "m/s",
            groupName = shipControllerGroupName,
            groupDisplayName = shipControllerGroupName),
            UI_FloatRange(
                minValue = 1f,
                maxValue = 100f,
                stepIncrement = 10f,
                scene = UI_Scene.All
            )]
        public float firingSpeed = 20f;

        #region Controller State & Start/Update

        [KSPEvent(guiActive = true,
                  guiActiveEditor = false,
                  guiName = "Toggle AI",
                  groupName = shipControllerGroupName,
                  groupDisplayName = shipControllerGroupName)]
        public void ToggleAI()
        {
            if (!controllerRunning) StartAI();
            else StopAI();
        }

        public void StartAI()
        {
            initialMass = vessel.GetTotalMass();
            CheckWeapons();
            shipControllerCoroutine = StartCoroutine(ShipController());
            controllerRunning = true;

        }

        public void StopAI()
        {
            controllerRunning = false;

            if (shipControllerCoroutine != null)
                StopCoroutine(shipControllerCoroutine);

            if (behaviourCoroutine != null)
                StopCoroutine(behaviourCoroutine);
        }

        private void Start()
        {
            if (HighLogic.LoadedSceneIsFlight)
                StartCoroutine(StatusChecker());
        }

        public override void OnStart(StartState state)
        {
            UpdateAttachment();

            if (HighLogic.LoadedSceneIsFlight)
            {
                fc = part.gameObject.AddComponent<KCSFlightController>();
                fc.alignmentToleranceforBurn = 7.5f;
                fc.throttleLerpRate = 3;
                controller = FindObjectOfType<KCSController>();

                Vector3 size = vessel.vesselSize;
                shipLength = (new[] { size.x, size.y, size.z}).ToList().Max();

                Vector3 availableTorque = Vector3.zero;
                var reactionWheels = vessel.FindPartModulesImplementing<ModuleReactionWheel>();
                foreach(var wheel in reactionWheels)
                {
                    Vector3 pos;
                    wheel.GetPotentialTorque(out pos, out pos);
                    availableTorque += pos;
                }

                maxAngularAcceleration = AngularAcceleration(availableTorque, vessel.MOI);
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartEvent.Add(UpdateAttachment);
            }
        }

        private void FixedUpdate()
        {
            if (controllerRunning) fc.Drive();
        }

        public void OnDestroy()
        {
            GameEvents.onEditorPartEvent.Remove(UpdateAttachment);
        }

        #endregion

        #region Main Functions/Loops

        private IEnumerator StatusChecker()
        {
            while (true)
            {
                CheckStatus();
                if (!alive) {
                    StopAI();
                    FireEscapePods();
                    yield break;
                }
                
                yield return new WaitForSeconds(updateInterval);
            }
        }

        private IEnumerator ShipController()
        {
            CheckStatus();
            if (!alive)
            {
                StopAI();
                yield break;
            } 

            while (true)
            {
                lastUpdate = Time.time;

                // Find target.

                UpdateDetectionRange();
                FindTarget();

                // Missiles.

                CheckWeapons();
                MissileFireControl();

                // Update behaviour tree for movement and projectile weapons.

                behaviourCoroutine = StartCoroutine(UpdateBehaviour());
                yield return behaviourCoroutine;
            } 
        }

        private IEnumerator UpdateBehaviour()
        {
            maxAcceleration = GetMaxAcceleration(vessel);

            // Movement.
            if (hasPropulsion && !hasWeapons && CheckWithdraw())
            {
                state = "Withdrawing";

                // Switch to passive robotics while withdrawing.
                UpdateRobotics(false);

                // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.

                double initDeltaV = vessel.VesselDeltaV.TotalDeltaVActual;
                Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                bool facingNorth = Vector3.Angle(vessel.ReferenceTransform.up, orbitNormal) < 90;

                fc.throttle = 1;
                fc.attitude = orbitNormal * (facingNorth ? 1 : -1);

                while (vessel.VesselDeltaV != null && vessel.VesselDeltaV.TotalDeltaVActual > (initDeltaV - 200))
                {
                    if (vessel.VesselDeltaV.TotalDeltaVActual < 1) break;

                    orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                    fc.attitude = orbitNormal * (facingNorth ? 1 : -1);

                    yield return new WaitForSeconds(1.0f);
                }

                fc.throttle = 0;
            }
            else if (target != null && CanFireProjectile(target))
            {
                // Aim at target using current projectile weapon.
                // The weapon handles firing.

                state = "Firing Projectile";
                fc.throttle = 0;
                currentProjectile.target = target;
                currentProjectile.side = side;

                // Temporarily disabled.
                if (currentProjectile.weaponType == "Rocket")
                {
                    yield return new WaitForSeconds(updateInterval);
                    yield break;
                }

                while (UnderTimeLimit() && target != null && currentProjectile.canFire)
                {
                    fc.attitude = currentProjectile.Aim();

                    yield return new WaitForFixedUpdate();
                }
            }
            else if (CheckIncoming()) // Needs to start evading an incoming missile.
            {
                state = "Dodging";

                float previousTolerance = fc.alignmentToleranceforBurn;
                fc.alignmentToleranceforBurn = 45;
                fc.throttle = 1;

                ModuleWeaponController incoming = shouldDodgeWeapons.First().Item1;
                Vector3 incomingVector;
                bool complete = false;

                while (UnderTimeLimit() && incoming != null && !complete)
                {
                    incomingVector = FromTo(vessel, incoming.vessel);
                    fc.attitude = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, incomingVector.normalized);

                    yield return new WaitForFixedUpdate();
                    complete = Vector3.Dot(RelVel(vessel, incoming.vessel), incomingVector) < 0;
                }

                fc.alignmentToleranceforBurn = previousTolerance;
            }
            else if (CheckOrbitUnsafe())
            {
                Orbit o = vessel.orbit;
                double UT = Planetarium.GetUniversalTime();

                if (o.ApA < minSafeAltitude) 
                {
                    // Entirety of orbit is inside atmosphere, burn up until apoapsis is outside atmosphere by a 10% margin.

                    state = "Correcting Orbit (Apoapsis too low)";
                    fc.throttle = 1;

                    while (o.ApA < minSafeAltitude * 1.1)
                    {
                        UT = Planetarium.GetUniversalTime();
                        fc.attitude = o.Radial(UT);
                        yield return new WaitForFixedUpdate();
                    }
                }
                else if (o.altitude < minSafeAltitude)
                {
                    if (o.timeToPe < o.timeToAp)
                    {
                        // Our apoapsis is outside the atmosphere but we are inside the atmosphere and descending.
                        // Burn up until we are ascending and our apoapsis is outside the atmosphere by a 10% margin.

                        state = "Correcting Orbit (Falling inside atmo)";
                        fc.throttle = 1;

                        while (o.ApA < minSafeAltitude * 1.1 || o.timeToPe < o.timeToAp)
                        {
                            UT = Planetarium.GetUniversalTime();
                            fc.attitude = o.Radial(UT);
                            yield return new WaitForFixedUpdate();
                        }
                    }

                    // We are inside the atmosphere but our apoapsis is outside the atmosphere and we are gaining altitude.
                    // The most efficient thing to do is idle.

                    state = "Correcting Orbit (Wait)";
                }
                else
                {
                    // We are outside the atmosphere but our periapsis is inside the atmosphere.
                    // Execute a burn to circularize our orbit at the current altitude.

                    state = "Correcting Orbit (Circularizing)";

                    Vector3d fvel, deltaV = Vector3d.up * 100;
                    fc.throttle = 1;

                    while (deltaV.magnitude > 2)
                    {
                        yield return new WaitForFixedUpdate();

                        UT = Planetarium.GetUniversalTime();
                        fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                        deltaV = fvel - vessel.GetObtVelocity();

                        fc.attitude = deltaV.normalized;
                        fc.throttle = Mathf.Lerp(0, 1, (float)(deltaV.magnitude / 10));
                    }
                }

                fc.throttle = 0;
            }
            else if (target != null && weapons.Count > 0 && hasWeapons)
            {
                // todo: implement for longer range movement.
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebModuleRendezvousAutopilot.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/OrbitalManeuverCalculator.cs
                // https://github.com/MuMech/MechJeb2/blob/dev/MechJeb2/MechJebLib/Maths/Gooding.cs

                /* 

                known: 

                mass of the target
                mass of each of my weapons
                min and max ranges of each of my weapons
                my position & velocity
                target position & velocity

                
                problem:

                maintain a good range for firing the best weapon for this target

                - best weapon: the weapon who's preferred target mass is closest to the target mass.
                - good range: a range between the min and max range of these weapons, preferrably nearer the max range.

                 */

                // Deploy combat robotics.
                UpdateRobotics(true);

                ModuleWeaponController currentWeapon = GetPreferredWeapon(target, weapons);
                float minRange = currentWeapon.MinMaxRange.x;
                float maxRange = currentWeapon.MinMaxRange.y;
                float currentRange = VesselDistance(vessel, target);
                bool complete = false;
                Vector3 relVel = RelVel(vessel, target);

                if (currentRange < minRange)
                {
                    state = "Manoeuvring (Away)";
                    fc.throttle = 1;
                    float oldAlignment = fc.alignmentToleranceforBurn;
                    fc.alignmentToleranceforBurn = 45;

                    while (UnderTimeLimit() && target != null && !complete)
                    {
                        fc.attitude = FromTo(vessel, target) * -1;
                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;
                        complete = FromTo(vessel, target).magnitude > minRange;

                        yield return new WaitForFixedUpdate();
                    }

                    fc.alignmentToleranceforBurn = oldAlignment;
                }
                else if (currentRange > maxRange && !NearIntercept(relVel, minRange, maxAcceleration))
                {
                    float angle;

                    while (UnderTimeLimit() && target != null && !complete)
                    {
                        relVel = RelVel(vessel, target);
                        Vector3 targetVec = ToClosestApproach(relVel, minRange).normalized;
                        angle = Vector3.Angle(relVel.normalized, targetVec);

                        if (angle > 45 && relVel.magnitude > maxAcceleration * 2)
                        {
                            state = "Manoeuvring (Match Velocity)";
                            fc.attitude = relVel.normalized * -1;
                        }
                        else
                        {
                            state = "Manoeuvring (Prograde to Target)";
                            fc.attitude = Vector3.LerpUnclamped(relVel.normalized, targetVec, 1 + 1 * (relVel.magnitude / maxAcceleration));
                        }

                        fc.throttle = Vector3.Dot(RelVel(vessel, target), fc.attitude) < manoeuvringSpeed ? 1 : 0;
                        complete = FromTo(vessel, target).magnitude < maxRange;

                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    if (relVel.magnitude > firingSpeed)
                    {
                        state = "Manoeuvring (Kill Velocity)";

                        while (UnderTimeLimit() && target != null && !complete)
                        {
                            relVel = RelVel(vessel, target);
                            fc.attitude = relVel.normalized * -1;
                            complete = relVel.magnitude < firingSpeed / 3;
                            fc.throttle = !complete ? 1 : 0;

                            yield return new WaitForFixedUpdate();
                        }
                    }
                    else
                    {
                        state = "Manoeuvring (Drift)";
                        fc.throttle = 0;
                        fc.attitude = Vector3.zero;

                        yield return new WaitForSeconds(updateInterval);

                        /*while (UnderTimeLimit() && target != null)
                        {
                            fc.attitude = Vector3.ProjectOnPlane(FromTo(vessel, target).normalized, RelVel(vessel, target));

                            yield return new WaitForFixedUpdate();
                        }*/
                    }
                }
            }
            else
            {
                // Idle

                state = "Idling";

                fc.throttle = 0;
                fc.attitude = Vector3.zero;

                // Switch to passive robotics.
                UpdateRobotics(false);

                yield return new WaitForSeconds(updateInterval);
            }
        }

        public void MissileFireControl()
        {
            if (target == null) return;

            if (Time.time - lastFired > fireInterval)
            {
                lastFired = Time.time;
                fireInterval = UnityEngine.Random.Range(5, 15);

                if (weapons.Count > 0)
                {
                    var weapon = GetPreferredWeapon(target, GetAvailableMissiles(target));

                    if (weapon != null)
                    {
                        weapon.target = target;
                        weapon.side = side;
                        weapon.Fire();
                        targetController.AddIncoming(weapon);

                        StartCoroutine(CheckWeaponsDelayed());
                    }
                }
            }
        }

        #endregion

        #region Utility Functions

        public void CheckWeapons()
        {
            weapons = vessel.FindPartModulesImplementing<ModuleWeaponController>();

            if (weapons.Count > 0)
            {
                weaponsMinRange = weapons.Min(w => w.MinMaxRange.x);
                weaponsMaxRange = weapons.Max(w => w.MinMaxRange.y);
            }
        }

        public IEnumerator CheckWeaponsDelayed()
        {
            yield return new WaitForFixedUpdate();
            CheckWeapons();
        }

        public ModuleShipController GetNearestEnemy()
        {
            var enemiesByDistance = controller.ships.FindAll(s => s != null && s.alive && s.side != side);
            if (enemiesByDistance.Count < 1) return null;
            return enemiesByDistance.OrderBy(s => KCS.VesselDistance(s.vessel, vessel)).First();
        }

        private ModuleWeaponController GetPreferredWeapon(Vessel target, List<ModuleWeaponController> weapons)
        {
            float targetMass = (float)target.totalMass;
            if (weapons.Count < 1) return null;
            return weapons.OrderBy(w => Mathf.Abs(targetMass - (w.mass * w.targetMassRatio))).First();
        }

        private List<ModuleWeaponController> GetAvailableMissiles(Vessel target)
        {
            float targetRange = FromTo(vessel, target).magnitude;
            return weapons.FindAll(w => w.weaponType == "Missile" && targetRange > w.MinMaxRange.x && targetRange < w.MinMaxRange.y);
        }

        private bool CanFireProjectile(Vessel target)
        {
            if (RelVel(vessel, target).magnitude > firingSpeed) return false;

            float targetRange = FromTo(vessel, target).magnitude;
            List<ModuleWeaponController> available = weapons.FindAll(w => ModuleWeaponController.projectileTypes.Contains(w.weaponType));
            available = available.FindAll(w => targetRange > w.MinMaxRange.x && targetRange < w.MinMaxRange.y);
            available = available.FindAll(w => w.canFire);

            if (available.Count < 1) return false;
            currentProjectile = available.First();

            return true;
        }

        private void FireEscapePods()
        {
            //function to fire escape pods when the ship is dead but still holds AI units
            List<ModuleEscapePodGuidance> PodList = vessel.FindPartModulesImplementing<ModuleEscapePodGuidance>();
            //trigger the escape start method in every found controller
            foreach (ModuleEscapePodGuidance EscapePod in PodList)
            {
                EscapePod.BeginEscape();
            }
        }

        public bool CheckStatus()
        {
            hasPropulsion = vessel.FindPartModulesImplementing<ModuleEngines>().FindAll(e => e.EngineIgnited && e.isOperational).Count > 0;
            hasWeapons = vessel.FindPartModulesImplementing<ModuleWeaponController>().FindAll(w => w.canFire).Count > 0;
            //bool control = vessel.maxControlLevel != Vessel.ControlLevel.NONE && vessel.angularVelocity.magnitude < 20;
            bool control = vessel.isCommandable && vessel.angularVelocity.magnitude < 20;
            bool dead = (!hasPropulsion && !hasWeapons) || !control;

            alive = !dead;
            return alive;
        }

        private bool CheckWithdraw()
        {
            if (vessel.VesselDeltaV.TotalDeltaVActual < 1) return false; 

            var nearest = GetNearestEnemy();
            if (nearest == null) return false;

            return Mathf.Abs((nearest.vessel.GetObtVelocity() - vessel.GetObtVelocity()).magnitude) < 200;
        }

        private void UpdateDetectionRange()
        {
            var sensors = vessel.FindPartModulesImplementing<ModuleObjectTracking>();

            if (sensors.Count < 1)
            {
                maxDetectionRange = 1000;
            }
            else
            {
                maxDetectionRange = sensors.Max(s => s.detectionRange);
            }

            //if the sensors aren't deployed and the AI is running
            if(!DeployedSensors && controllerRunning)
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    var anim = Sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                    if (anim == null) continue;
                    KCS.TryToggle(true, anim);
                }
                DeployedSensors = true;
            }

            //if the sensors are deployed and the AI isn't runnning
            if (DeployedSensors && !controllerRunning)
            {
                foreach (ModuleObjectTracking Sensor in sensors)
                {
                    //try deploy animations, not all scanners will have them 
                    var anim = Sensor.part.FindModuleImplementing<ModuleAnimationGroup>();
                    if (anim == null) continue;
                    TryToggle(true, Sensor.part.FindModuleImplementing<ModuleAnimationGroup>());
                }
                DeployedSensors = true;
            }
        }

        private void FindTarget()
        {
            var ships = controller.ships.FindAll(
                s => 
                s != null
                && s.vessel != null
                && VesselDistance(s.vessel, vessel) < maxDetectionRange
                && s.side != side
                && s.alive);

            if (ships.Count < 1) { 
                target = null;
                return;
            }

            targetController = ships.OrderBy(s => VesselDistance(s.vessel, vessel)).First();
            target = targetController.vessel;

            if (vessel.targetObject == null || vessel.targetObject.GetVessel() != target)
            {
                vessel.targetObject = target;

                if (vessel == FlightGlobals.ActiveVessel)
                    FlightGlobals.fetch.SetVesselTarget(target, true);
            }
        }

        public void ToggleSide()
        {
            if (side == Side.A)
            {
                side = Side.B;
            }
            else
            {
                side = Side.A;
            }
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            CelestialBody body = o.referenceBody;
            PQS pqs = body.pqsController;
            double maxTerrainHeight = pqs.radiusMax - pqs.radius;
            minSafeAltitude = Math.Max(maxTerrainHeight, body.atmosphereDepth);
            return o.PeA < minSafeAltitude;
        }

        private bool UnderTimeLimit()
        {
            return Time.time - lastUpdate < updateInterval;
        }

        private bool NearIntercept(Vector3 relVel, float minRange, float maxAccel)
        {
            float timeToKillVelocity = relVel.magnitude / maxAccel;

            Vector3 toClosestApproach = ToClosestApproach(relVel, minRange);
            float velToClosestApproach = Vector3.Dot(relVel, toClosestApproach.normalized);
            if (velToClosestApproach < 1) return false;
            float timeToClosestApproach = Mathf.Abs(toClosestApproach.magnitude / velToClosestApproach);

            return timeToClosestApproach < timeToKillVelocity;
        }

        private Vector3 ToClosestApproach(Vector3 relVel, float minRange)
        {
            Vector3 rotatedVector = Vector3.ProjectOnPlane(relVel, FromTo(vessel, target).normalized).normalized;
            Vector3 toClosestApproach = (target.transform.position + (rotatedVector * minRange)) - vessel.transform.position;
            return toClosestApproach;
        }

        public void UpdateRobotics(bool deploy)
        {
            if (deploy == roboticsDeployed) return;

            //generate list of KAL500 parts, could change in flight
            List<ModuleCombatRobotics> RoboticControllers = vessel.FindPartModulesImplementing<ModuleCombatRobotics>();
            
            if (deploy)
                RoboticControllers.ForEach(rc => rc.CombatTrigger());
            else
                RoboticControllers.ForEach(rc => rc.PassiveTrigger());

            roboticsDeployed = deploy;
        }

        private bool CheckIncoming()
        {
            if (incomingWeapons == null || incomingWeapons.Count < 1) return false;

            Vector3 attitude = vessel.transform.up;

            float timeToDisplace = shipLength / maxAcceleration;
            if (float.IsInfinity(timeToDisplace)) return false;

            shouldDodgeWeapons = new List<Tuple<ModuleWeaponController, float>>();
            incomingWeapons.RemoveAll(w => w == null);

            foreach(var incoming in incomingWeapons)
            {
                Vessel iv = incoming.vessel;
                Vector3 incomingVector = FromTo(vessel, iv);
                Vector3 relVel = RelVel(vessel, iv);

                bool onCollisionCourse = Vector3.Dot(incomingVector.normalized, relVel.normalized) > 0.95;
                if (!onCollisionCourse) continue;

                Vector3 perpendicular = Vector3.ProjectOnPlane(attitude, incomingVector.normalized);
                float rotDistance = Vector3.Angle(attitude, perpendicular) * Mathf.Deg2Rad;
                float timeToRotate = Integrate(rotDistance / 2, maxAngularAcceleration.magnitude) * 2;
                float timeToDodge = timeToRotate + timeToDisplace;

                float timeToHit = incomingVector.magnitude / Vector3.Dot(relVel, incomingVector.normalized);

                if (timeToHit > Mathf.Max(timeToDodge, updateInterval) * 1.5) continue;
                shouldDodgeWeapons.Add(new Tuple<ModuleWeaponController, float>(incoming, timeToHit));
            }

            shouldDodgeWeapons.OrderBy(i => i.Item2);
            return shouldDodgeWeapons.Count > 0;
        }

        public void AddIncoming(ModuleWeaponController wep)
        {
            if (incomingWeapons == null)
                incomingWeapons = new List<ModuleWeaponController>();

            incomingWeapons.Add(wep);
        }

        #endregion

        #region Part Appearance

        Part editorChild;

        private void UpdateAttachment()
        {
            Transform mediumCapTop = part.FindModelTransform("MediumCapTop");
            Transform mediumBoltsTop = part.FindModelTransform("MediumBoltsTop");
            Transform mediumCapBottom = part.FindModelTransform("MediumCapBottom");
            Transform mediumBoltsBottom = part.FindModelTransform("MediumBoltsBottom");

            if (mediumCapTop == null) return;

            bool topAttached = part.attachNodes[1].attachedPart != null;
            bool bottomAttached = part.attachNodes[0].attachedPart != null || !topAttached;

            mediumCapTop.gameObject.SetActive(!topAttached);
            mediumBoltsTop.gameObject.SetActive(topAttached);

            mediumCapBottom.gameObject.SetActive(!bottomAttached);
            mediumBoltsBottom.gameObject.SetActive(bottomAttached);
        }

        private void UpdateAttachment(ConstructionEventType data0, Part data1)
        {
            if (part != data1 && data1.parent != part && data1 != editorChild) return;
            if (data1.parent == part)
            {
                editorChild = data1;
            }
            UpdateAttachment();
        }

        #endregion
    }
}


