using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Rigging
{
    /// <summary>The BlendShape Copier as its own window, as it was before the move into the toolbox.</summary>
    public class BlendShapeCopierWindow : EditorWindow
    {
        [SerializeField] private BlendShapeCopier copier = new BlendShapeCopier();

        [MenuItem("Window/Sable Toolbox/BlendShape Copier")]
        private static void Open() => ShowWindow();

        public static BlendShapeCopierWindow ShowWindow()
        {
            BlendShapeCopierWindow window = GetWindow<BlendShapeCopierWindow>("BlendShape Copier");
            window.minSize = new Vector2(420, 320);
            return window;
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/BlendShape Copier: Set As Source")]
        private static void ContextSetSource(MenuCommand command)
        {
            BlendShapeCopierWindow window = ShowWindow();
            window.copier.SetSource((SkinnedMeshRenderer)command.context);
            window.Repaint();
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/BlendShape Copier: Add As Target")]
        private static void ContextAddTarget(MenuCommand command)
        {
            BlendShapeCopierWindow window = ShowWindow();
            window.copier.AddTargets(new[] { (SkinnedMeshRenderer)command.context });
            window.Repaint();
        }

        private void OnEnable()
        {
            copier ??= new BlendShapeCopier();
            copier.RepaintRequested = Repaint;
            copier.Enable();
        }

        private void OnDisable() => copier.Disable();

        private void OnSelectionChange()
        {
            // The preview reads live weights, so keep it honest while poking around.
            copier.MarkDirty();
            Repaint();
        }

        private void OnGUI() => copier.OnGUI();
    }
}
