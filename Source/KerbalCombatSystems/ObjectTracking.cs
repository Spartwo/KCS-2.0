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
    class ModuleObjectTracking : PartModule
    {
        List<ModuleAnimationGroup> SpinAnims;
        //all ore scanning parts plus the sentinal at superlong range

        const string objectTrackingGroupName = "Situational Awareness";
        private int ScalingFactor;

        [KSPField(
              guiActive = true,
              guiActiveEditor = true, 
              guiName = "Detection Range",
              guiUnits = "m",
              groupName = objectTrackingGroupName,
              groupDisplayName = objectTrackingGroupName),
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
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorVariantApplied.Add(OnVariantApplied);

            SpinAnims = part.FindModulesImplementing<ModuleAnimationGroup>();

            ScalingFactor = HighLogic.CurrentGame.Parameters.CustomParams<KCSCombat>().ScalingFactor;
            DetectionRange = BaseDetectionRange * ScalingFactor;
        }


        private void OnVariantApplied(Part appliedPart, PartVariant variant)
        {
            if (appliedPart != part) return;

            string SensorSize = variant.Name;
            ModuleResourceScanner OreScanner = part.FindModuleImplementing<ModuleResourceScanner>();

            Debug.Log(SensorSize);

            switch (SensorSize)
            {
                case "Medium":
                    BaseDetectionRange = 1000f;
                    DetectionRange = BaseDetectionRange * ScalingFactor;
                    OreScanner.MaxAbundanceAltitude = 500000;//500km
                    UpdateButtons(SpinAnims[0], SpinAnims[1]);
                    break;
                case "Short":
                    BaseDetectionRange = 500f;
                    DetectionRange = BaseDetectionRange * ScalingFactor;
                    OreScanner.MaxAbundanceAltitude = 100000;//100km
                    UpdateButtons(SpinAnims[1], SpinAnims[0]);
                    break;
                default:
                    Debug.Log("variant not found");
                    //it's a non-variant scanner, no need to modify ranges
                    break;
            }
        }

        private void UpdateButtons(ModuleAnimationGroup EnabledAnim, ModuleAnimationGroup DisabledAnim)
        {
            //todo: hide overlapping animations using code
        }
    }
}
