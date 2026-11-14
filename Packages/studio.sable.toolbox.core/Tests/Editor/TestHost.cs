using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Sable.Toolbox.Tests
{
    public static class TestHost
    {
        public static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>Puts a view into a real editor window so AttachToPanelEvent and layout run.</summary>
        public static TestHostWindow Attach(VisualElement view)
        {
            var window = EditorWindow.GetWindow<TestHostWindow>();
            window.rootVisualElement.Clear();
            window.rootVisualElement.Add(view);
            return window;
        }

        /// <summary>Presses a button the way keyboard submit does (runs its clicked handlers).</summary>
        public static void Click(Button button)
        {
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        public static Button FindButton(VisualElement root, string text) =>
            root.Query<Button>().Where(b => b.text == text).First();

        public static Label FindLabel(VisualElement root, System.Func<string, bool> match) =>
            root.Query<Label>().Where(l => l.text != null && match(l.text)).First();
    }
}
