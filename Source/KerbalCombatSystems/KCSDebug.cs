using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;
using System.IO;

namespace KerbalCombatSystems
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]

    public class KCSDebug : MonoBehaviour
    {
        public static bool ShowLines;
        public static bool ShowUI;
        //used to track if the debug lines were showing prior
        private bool ShowLinesDebug;

        private void Start()
        {
            ShowLines = false;
            ShowUI = true;
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
        }

        private void OnHideUI()
        {
            ShowLinesDebug = ShowLines;
            ShowLines = false;
            ShowUI = false;
        }

        private void OnShowUI()
        {
            if (ShowLinesDebug) ShowLines = true;
            ShowUI = true;
        }

        private void Update()
        {
            //on press f12 toggle missile lines
            if (Input.GetKeyDown(KeyCode.F12) && !Input.GetKey(KeyCode.LeftAlt))
            {
                //switch bool return
                ShowLines = !ShowLines;
                Debug.Log("[KCS]: Lines " + (ShowLines ? "enabled." : "disabled."));
            }
        }

        public static LineRenderer CreateLine(Color LineColour)
        {
            //spawn new line
            LineRenderer Line = new GameObject().AddComponent<LineRenderer>();
            Line.useWorldSpace = true;
            //create a material for the line with its unique colour
            Material LineMaterial = new Material(Shader.Find("Standard"));
            LineMaterial.color = LineColour;
            LineMaterial.shader = Shader.Find("Unlit/Color");
            //apply the material to the line renderer
            Line.material = LineMaterial;

            //make it a point
            Line.startWidth = 0.5f;
            Line.endWidth = 0f;

            // Don't draw until the line is first plotted.
            Line.positionCount = 0;

            //pass the line back to be associated with a vector
            return Line;
        }

        public static void PlotLine(Vector3[] Positions, LineRenderer Line)
        {
            if (ShowLines)
            {
                Line.positionCount = 2;
                Line.SetPositions(Positions);
            }
            else
            {
                Line.positionCount = 0;
            }
        }

        public static void DestroyLine(LineRenderer line)
        {
            if (line == null) return;
            if (line.gameObject == null) return;
            line.gameObject.DestroyGameObject();
        }
    }
}
