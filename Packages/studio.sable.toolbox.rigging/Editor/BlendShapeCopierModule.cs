using System.Linq;
using Sable.Toolbox.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sable.Toolbox.Rigging
{
    /// <summary>Hosts the BlendShape Copier inside the Sable Toolbox window.</summary>
    public sealed class BlendShapeCopierModule : IToolboxModule
    {
        public string Id => "studio.sable.toolbox.rigging.blendshape-copier";
        public string DisplayName => "BlendShape Copier";
        public int Order => 100;

        public VisualElement CreateView(ToolboxContext context)
        {
            var copier = new BlendShapeCopier();
            var gui = new IMGUIContainer(copier.OnGUI);
            copier.RepaintRequested = gui.MarkDirtyRepaint;

            // Seed targets from the shared scan instead of walking the avatar again.
            var addFromAvatar = new Button(() =>
            {
                if (context.Scan == null) return;
                copier.AddTargets(context.Scan.Renderers.OfType<SkinnedMeshRenderer>());
                gui.MarkDirtyRepaint();
            }) { text = "Add all skinned meshes from avatar" };
            addFromAvatar.SetEnabled(context.Avatar != null);

            void OnChanged()
            {
                addFromAvatar.SetEnabled(context.Avatar != null);
                copier.MarkDirty();
                gui.MarkDirtyRepaint();
            }

            var root = new VisualElement();
            root.RegisterCallback<AttachToPanelEvent>(_ => { copier.Enable(); context.Changed += OnChanged; });
            root.RegisterCallback<DetachFromPanelEvent>(_ => { copier.Disable(); context.Changed -= OnChanged; });
            root.Add(addFromAvatar);
            root.Add(gui);
            return root;
        }
    }
}
