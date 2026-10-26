using System;
using System.Collections.Generic;

namespace InventorXrSo.Core.Experience
{
    /// <summary>Explicit client allowlists. Unknown actions/tools fail closed outside authoring.</summary>
    public static class ActivityPolicy
    {
        private static readonly HashSet<string> ViewActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "view.fit", "view.legend", "inspect.scale", "inspect.environment", "inspect.scale.one",
            "inspect.scale.fit", "inspect.scale.table", "inspect.scale.room", "inspect.scale.recenter",
            "inspect.measure", "inspect.measure.pin", "inspect.measure.cancel", "inspect.measure.clear",
            "doc.exit", "doc.connection", "doc.documents", "doc.recenter", "doc.list.close.0"
        };
        private static readonly HashSet<string> SessionTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "inventor_get_capabilities", "inventor_get_visual_revision", "inventor_get_scene_graph",
            "inventor_get_display_mesh", "inventor_pick_entity", "inventor_highlight_entity",
            "inventor_get_sheet_metal_info", "inventor_get_flat_pattern_mesh", "inventor_list_open_documents"
        };
        private static readonly HashSet<string> VerificationTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "inventor_inspect_xr", "inventor_check_interference_xr", "inventor_measure_min_distance_xr",
            "inventor_assembly_health_xr"
        };

        public static bool AllowsAction(Activity activity, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (activity == Activity.Design) return !id.StartsWith("inspect.verify.", StringComparison.Ordinal)
                && !id.StartsWith("inspect.visibility.", StringComparison.Ordinal);
            if (activity != Activity.Presentation && activity != Activity.Verification) return false;
            if (ViewActions.Contains(id) || id.StartsWith("doc.open.", StringComparison.Ordinal)
                || id.StartsWith("doc.list.close.", StringComparison.Ordinal)) return true;
            if (activity == Activity.Verification && (id.StartsWith("inspect.section", StringComparison.Ordinal)
                || id.StartsWith("inspect.visibility.", StringComparison.Ordinal)
                || id.StartsWith("inspect.verify.", StringComparison.Ordinal)
                || id.StartsWith("inspect.pick.", StringComparison.Ordinal)
                || id == "inspect.browse" || id == "inspect.documents" || id == "inspect.properties" || id == "inspect.context.enter"
                || id == "inspect.context.back")) return true;
            return false;
        }

        public static bool AllowsInput(Activity activity, InventorXrSo.Core.Input.InputAction action)
        {
            if (activity == Activity.Design) return true;
            if (activity != Activity.Presentation && activity != Activity.Verification) return false;
            switch (action)
            {
                case InventorXrSo.Core.Input.InputAction.Select:
                case InventorXrSo.Core.Input.InputAction.MoveView:
                case InventorXrSo.Core.Input.InputAction.RotateView:
                case InventorXrSo.Core.Input.InputAction.PressKeys:
                case InventorXrSo.Core.Input.InputAction.KeypadOk:
                case InventorXrSo.Core.Input.InputAction.KeypadCancel:
                case InventorXrSo.Core.Input.InputAction.Speak:
                case InventorXrSo.Core.Input.InputAction.Tabs:
                case InventorXrSo.Core.Input.InputAction.Zoom:
                case InventorXrSo.Core.Input.InputAction.Suggest:
                case InventorXrSo.Core.Input.InputAction.Back:
                case InventorXrSo.Core.Input.InputAction.Fit:
                case InventorXrSo.Core.Input.InputAction.TwoHands: return true;
                default: return false;
            }
        }

        public static bool AllowsTool(Activity activity, string tool)
        {
            if (string.IsNullOrEmpty(tool)) return false;
            if (activity == Activity.Design) return true;
            if (SessionTools.Contains(tool)) return true;
            // Document activation is an explicit list choice. Double-trigger and voice-open have no route here.
            if (tool == "inventor_activate_open_document_xr")
                return activity == Activity.Presentation || activity == Activity.Verification;
            return activity == Activity.Verification && VerificationTools.Contains(tool);
        }

        public static string[] Tabs(Activity activity) => activity == Activity.Presentation
            ? new[] { "misura", "vista", "documento" }
            : new[] { "esplora", "misura", "sezione", "visibilita", "verifica", "vista", "documento" };
    }
}
