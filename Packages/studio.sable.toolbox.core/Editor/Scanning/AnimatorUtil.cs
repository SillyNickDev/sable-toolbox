using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace Sable.Toolbox.Scanning
{
    public static class AnimatorUtil
    {
        /// <summary>The editable controller behind a runtime controller (unwraps override controllers).</summary>
        public static AnimatorController EditorController(RuntimeAnimatorController rac)
        {
            while (rac is AnimatorOverrideController aoc) rac = aoc.runtimeAnimatorController;
            return rac as AnimatorController;
        }

        /// <summary>Every state in a state machine, including nested sub-state machines.</summary>
        public static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            if (machine == null) yield break;
            foreach (ChildAnimatorState child in machine.states) yield return child.state;
            foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
                foreach (AnimatorState s in States(sub.stateMachine))
                    yield return s;
        }

        /// <summary>The layer a scan entry points at, or null if the controller is not editable.</summary>
        public static AnimatorControllerLayer Layer(AnimatorLayerRef layer)
        {
            AnimatorController ac = EditorController(layer.Controller);
            if (ac == null) return null;
            AnimatorControllerLayer[] layers = ac.layers;
            return layer.LayerIndex < layers.Length ? layers[layer.LayerIndex] : null;
        }

        /// <summary>Distinct editable controllers on the descriptor's playable layers.</summary>
        public static IEnumerable<AnimatorController> Controllers(AvatarScan scan)
        {
            var seen = new HashSet<AnimatorController>();
            foreach (AnimatorLayerRef l in scan.AnimatorLayers)
            {
                AnimatorController ac = EditorController(l.Controller);
                if (ac != null && seen.Add(ac)) yield return ac;
            }
        }
    }
}
