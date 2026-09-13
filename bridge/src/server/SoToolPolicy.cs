using System;
using System.Collections.Generic;

namespace Bimwright.Ipt.Server;

/// <summary>Production SO surface. New tools are hidden until explicitly reviewed.</summary>
public static class SoToolPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "inventor_list_available_targets", "inventor_get_current_target", "inventor_switch_target",
        "inventor_health", "inventor_list_open_documents", "inventor_get_document_info",
        "inventor_get_selection", "inventor_resolve_entity", "inventor_plan_native_package",
        "inventor_get_sheet_metal_info", "inventor_list_topology",
        "inventor_list_parameters", "inventor_get_parameter", "inventor_get_iproperty", "inventor_get_mass_properties",
        "inventor_list_interfaces", "inventor_check_interference", "inventor_measure_min_distance",
        "inventor_get_assembly_bom", "inventor_list_constraints", "inventor_atomic_batch", "inventor_save_artifact", "inventor_move_component_safe", "inventor_edit_constraint_safe", "inventor_create_constraint_safe", "inventor_create_joint_safe", "inventor_ground_component_safe", "inventor_insert_component_safe",
        "inventor_new_document_safe", "inventor_open_document_safe", "inventor_activate_document_safe", "inventor_save_document_safe", "inventor_close_document_safe", "inventor_list_workspace_documents",
        "inventor_checkpoint_create", "inventor_checkpoint_list", "inventor_checkpoint_restore", "inventor_diff_checkpoint", "inventor_create_drawing_safe"
    };

    public static bool IsExposed(string? name) => name != null && Allowed.Contains(name);
}
