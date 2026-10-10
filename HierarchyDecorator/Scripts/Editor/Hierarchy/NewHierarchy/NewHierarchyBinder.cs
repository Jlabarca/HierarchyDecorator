#if UNITY_6000_6_OR_NEWER
using System.Collections.Generic;
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HierarchyDecorator
{
    /// <summary>
    /// Draws the decorations in Unity 6.6+'s UI Toolkit Hierarchy window.
    /// The IMGUI <c>hierarchyWindowItemByEntityIdOnGUI</c> callback no longer fires there,
    /// so styles and component icons are applied to each row as it is bound instead.
    /// </summary>
    internal static class NewHierarchyBinder
    {
        private const string IconContainerName = "hd-component-icons";
        private const float IconSize = 16f;

        private static double s_NextHook;

        public static void Initialize()
        {
            EditorApplication.update -= Rehook;
            EditorApplication.update += Rehook;
            Hook();
        }

        // Static event: covers every Hierarchy window, including ones opened later.
        private static void Hook()
        {
            HierarchyWindow.BindViewItem -= OnBindItem;
            HierarchyWindow.BindViewItem += OnBindItem;
        }

        // 6.6 clears HierarchyWindow's statics when code is unloaded/reloaded (and when the window mode is
        // switched), dropping our subscription - re-assert it. Unsubscribe-then-subscribe is idempotent.
        private static void Rehook()
        {
            if (EditorApplication.timeSinceStartup < s_NextHook)
            {
                return;
            }

            s_NextHook = EditorApplication.timeSinceStartup + 1.0;
            Hook();
        }

        private static void OnBindItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            if (!(item.Handler is HierarchyGameObjectHandler handler))
            {
                return;
            }

            HierarchyNode node = item.Node;
            GameObject go = handler.GetGameObject(in node);
            if (go == null)
            {
                return;
            }

            Settings settings = HierarchyDecorator.Settings;
            if (settings == null)
            {
                return;
            }

            ApplyStyle(item, go, settings);
            ApplyComponentIcons(item, handler.GetEntityId(in node), go, settings);
        }

        // --- Styles

        private static void ApplyStyle(HierarchyViewItem item, GameObject go, Settings settings)
        {
            VisualElement row = item.RowContainer;
            Label label = item.Name;

            // Rows are recycled - always write every property so a restyled row never keeps stale values.
            if (!settings.styleData.TryGetStyleFromPrefix(go.name, out HierarchyStyle style))
            {
                row.style.backgroundColor = StyleKeyword.Null;
                label.style.color = StyleKeyword.Null;
                label.style.unityFontStyleAndWeight = StyleKeyword.Null;
                label.style.unityTextAlign = StyleKeyword.Null;
                label.style.fontSize = StyleKeyword.Null;
                label.text = go.name;
                return;
            }

            string text = go.name;
            if (style.isRegex)
            {
                if (style.capturedGroups != null && style.capturedGroups.Length > 0)
                {
                    text = string.Join("", style.capturedGroups);
                }
            }
            else
            {
                text = text.Substring(style.prefix.Length).Trim();
            }

            ModeOptions mode = style.GetCurrentMode(EditorGUIUtility.isProSkin);
            row.style.backgroundColor = mode.backgroundColour;
            label.text = style.FormatString(text);
            label.style.color = mode.fontColour;
            label.style.fontSize = style.fontSize;
            label.style.unityFontStyleAndWeight = style.fontStyle;
            label.style.unityTextAlign = style.fontAlignment;
        }

        // --- Component icons

        private static void ApplyComponentIcons(HierarchyViewItem item, EntityId id, GameObject go, Settings settings)
        {
            VisualElement host = item.RightCustomContainer;
            VisualElement container = host.Q(IconContainerName);

            bool wanted = settings.Components.Enabled
                && (settings.styleData.displayIcons || !settings.styleData.HasStyle(go.name));

            if (!wanted)
            {
                container?.RemoveFromHierarchy();
                return;
            }

            if (container == null)
            {
                container = new VisualElement { name = IconContainerName };
                container.style.flexDirection = FlexDirection.Row;
                container.style.alignItems = Align.Center;
                container.pickingMode = PickingMode.Ignore;
                host.Add(container);
            }
            container.Clear();

            HierarchyItem data = HierarchyManager.GetItem(id, go);
            data.OnGUIBegin();

            HashSet<Texture> seen = settings.Components.StackScripts ? new HashSet<Texture>() : null;
            foreach (ComponentItem component in data.Components.GetItems())
            {
                if (component.IsNullComponent)
                {
                    if (settings.Components.ShowMissingScriptWarning)
                    {
                        container.Add(MakeIcon(EditorGUIUtility.IconContent("warning").image, "Missing script", null));
                    }
                    continue;
                }

                if (!CanShow(component, settings))
                {
                    continue;
                }

                Texture texture = component.Content.image;
                if (texture == null || (seen != null && !seen.Add(texture)))
                {
                    continue;
                }

                bool toggle = component.HasToggle && settings.Components.ClickToToggleComponent;
                VisualElement icon = MakeIcon(texture, component.DisplayName, toggle ? component : null);
                if (toggle && !component.Active)
                {
                    icon.style.opacity = 0.4f;
                }
                container.Add(icon);
            }
        }

        private static VisualElement MakeIcon(Texture texture, string tooltip, ComponentItem toggle)
        {
            var icon = new Image { image = texture, tooltip = tooltip, scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = IconSize;
            icon.style.height = IconSize;
            icon.style.marginLeft = 1f;

            if (toggle != null)
            {
                icon.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0)
                    {
                        return;
                    }

                    toggle.ToggleActive();
                    icon.style.opacity = toggle.Active ? 1f : 0.4f;
                    e.StopPropagation();
                });
            }

            return icon;
        }

        private static bool CanShow(ComponentItem item, Settings settings)
        {
            if (item.IsNullComponent || item.Type.Excluded)
            {
                return false;
            }

            bool shown = item.Type.Shown;
            return item.IsBuiltIn
                ? settings.Components.DisplayBuiltIn || shown
                : settings.Components.DisplayMonoScripts || shown;
        }
    }
}
#endif
