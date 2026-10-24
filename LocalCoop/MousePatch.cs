using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>Test aid (`-lcclicklog`): which object gets the pointer down / click of the harness mouse.</summary>
    internal static class MousePatch
    {
        [HarmonyPatch(typeof(Selectable), "OnPointerDown")]
        internal static class DownPatch
        {
            private static void Prefix(Selectable __instance, PointerEventData eventData) { if (Lc.Has("-lcclicklog")) Lc.Line("mouse: pointer down on '" + __instance.name + "' (interactable " + __instance.IsInteractable() + ")"); }
        }

        [HarmonyPatch(typeof(Button), "OnPointerClick")]
        internal static class ClickPatch
        {
            private static void Prefix(Button __instance) { if (Lc.Has("-lcclicklog")) Lc.Line("mouse: pointer click on '" + __instance.name + "'"); }
        }

        [HarmonyPatch(typeof(Selectable), "OnPointerUp")]
        internal static class UpPatch
        {
            private static void Prefix(Selectable __instance, PointerEventData eventData) { if (Lc.Has("-lcclicklog")) Lc.Line("mouse: pointer up on '" + __instance.name + "' over=" + (eventData != null && eventData.pointerCurrentRaycast.gameObject != null ? eventData.pointerCurrentRaycast.gameObject.name : "-")); }
        }
    }
}
