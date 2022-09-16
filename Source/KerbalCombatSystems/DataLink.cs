using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleDataLinkRelay : PartModule
    {
        //all ore scanning parts plus the sentinal at superlong range

        const string DataLinkGroupName = "Situational Awareness";
        private int ScalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = DataLinkGroupName,
              groupDisplayName = DataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float DetectionRange = 0f;

        [KSPField(isPersistant = true)]
        public float BaseDetectionRange = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", DetectionRange));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {

            ScalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().ScalingFactor;
            DetectionRange = BaseDetectionRange * ScalingFactor;
        }
    }


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ModuleDataLinkReciever : PartModule
    {
        //all ore scanning parts plus the sentinal at superlong range

        const string DataLinkGroupName = "Situational Awareness";
        private int ScalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true,
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = DataLinkGroupName,
              groupDisplayName = DataLinkGroupName),
              UI_Label(scene = UI_Scene.All)]
        public float DetectionRange = 0f;

        [KSPField(isPersistant = true)]
        public float BaseDetectionRange = 0f;

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.Append(String.Format("Detection Range: {0} m", DetectionRange));

            return output.ToString();
        }

        public override void OnStart(StartState state)
        {

            ScalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().ScalingFactor;
            DetectionRange = BaseDetectionRange * ScalingFactor;
        }
    }
}
