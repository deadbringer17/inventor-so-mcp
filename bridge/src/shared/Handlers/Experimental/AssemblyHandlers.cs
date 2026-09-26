#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class AssemblyX
{
    public static ComponentOccurrence Occurrence(global::Inventor.Document doc, string? id)
        => X.Resolve(doc, id, out _) as ComponentOccurrence ?? throw new ArgumentException("REFERENCE_TYPE_MISMATCH: " + id + " is not an occurrence.");

    public static WorkAxis OriginAxis(AssemblyComponentDefinition def, string axis) => axis.Trim().ToUpperInvariant() switch
    {
        "X" => def.WorkAxes["X Axis"],
        "Y" => def.WorkAxes["Y Axis"],
        "Z" => def.WorkAxes["Z Axis"],
        _ => throw new ArgumentException("Direction must be X, Y or Z."),
    };
}

// ---------------- batch commands ----------------

public sealed class SuppressComponentHandler : ExperimentalHandler
{
    public override string Name => "suppress_component";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var occurrence = AssemblyX.Occurrence((global::Inventor.Document)assembly, (string?)p["occurrence_id"]);
        if (p["suppressed"]?.Type != JTokenType.Boolean) throw new ArgumentException("suppressed must be true or false.");
        bool suppress = (bool)p["suppressed"]!;
        if (suppress && !occurrence.Suppressed) occurrence.Suppress();
        else if (!suppress && occurrence.Suppressed) occurrence.Unsuppress();
        return new JObject { ["occurrence"] = occurrence.Name, ["suppressed"] = occurrence.Suppressed };
    }
}

/// <summary>replace_component: only a file already inside the host workspace, named, never a path (F16).</summary>
public sealed class ReplaceComponentHandler : ExperimentalHandler
{
    public override string Name => "replace_component";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var occurrence = AssemblyX.Occurrence((global::Inventor.Document)assembly, (string?)p["occurrence_id"]);
        string file = X.Str(p, "workspace_document");
        string path = WorkspaceDocumentPolicy.ExistingPath(WorkspaceDocumentPolicy.Root(), file);
        occurrence.Replace(path, X.Bool(p, "replace_all", false));
        return new JObject { ["occurrence"] = occurrence.Name, ["replaced_with"] = System.IO.Path.GetFileName(path) };
    }
}

public sealed class PatternComponentHandler : ExperimentalHandler
{
    public override string Name => "pattern_component";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var def = assembly.ComponentDefinition;
        var parents = app.TransientObjects.CreateObjectCollection();
        foreach (var id in X.Strings(p, "occurrence_ids", 64)) parents.Add(AssemblyX.Occurrence((global::Inventor.Document)assembly, id));
        int count1 = X.Int(p, "count1", 2, 1000);
        double spacing1 = UnitConvert.MmToCm(X.Positive(p, "spacing_mm1"));
        dynamic patterns = def.OccurrencePatterns;
        object pattern;
        if (p["dir2"] != null)
        {
            int count2 = X.Int(p, "count2", 2, 1000);
            double spacing2 = UnitConvert.MmToCm(X.Positive(p, "spacing_mm2"));
            pattern = patterns.AddRectangularPattern(parents, AssemblyX.OriginAxis(def, X.Str(p, "dir1")), true, spacing1, count1,
                AssemblyX.OriginAxis(def, X.Str(p, "dir2")), true, spacing2, count2);
        }
        else pattern = patterns.AddRectangularPattern(parents, AssemblyX.OriginAxis(def, X.Str(p, "dir1")), true, spacing1, count1);
        return new JObject { ["pattern"] = (string)((dynamic)pattern).Name };
    }
}

public sealed class ActivatePositionalRepresentationHandler : ExperimentalHandler
{
    public override string Name => "activate_positional_representation";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        dynamic manager = assembly.ComponentDefinition.RepresentationsManager;
        RepresentationX.Named(manager.PositionalRepresentations, X.Str(p, "name"), "positional representation").Activate();
        return new JObject { ["active_positional_representation"] = (string)manager.ActivePositionalRepresentation.Name };
    }
}

/// <summary>set_bom_structure: the per-occurrence override stored in the assembly itself (F15).</summary>
public sealed class SetBomStructureHandler : ExperimentalHandler
{
    public override string Name => "set_bom_structure";
    public override bool IsReadOnly => false;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var occurrence = AssemblyX.Occurrence((global::Inventor.Document)assembly, (string?)p["occurrence_id"]);
        // An occurrence accepts only these two overrides (verified live on 2027: normal, phantom,
        // purchased and inseparable answer E_INVALIDARG); the others belong to the component's own
        // document, which an assembly transaction must not rewrite.
        occurrence.BOMStructure = X.Str(p, "structure").ToLowerInvariant() switch
        {
            "default" => BOMStructureEnum.kDefaultBOMStructure,
            "reference" => BOMStructureEnum.kReferenceBOMStructure,
            _ => throw new ArgumentException("structure must be default or reference; normal, phantom, purchased and inseparable are set on the component's own document."),
        };
        return new JObject { ["occurrence"] = occurrence.Name, ["bom_structure"] = occurrence.BOMStructure.ToString() };
    }
}

// ---------------- queries ----------------

/// <summary>get_assembly_health: DOF per occurrence, grounding, constraint and joint health. Read-only.</summary>
public sealed class GetAssemblyHealthHandler : ExperimentalHandler
{
    public override string Name => "get_assembly_health";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var assembly = X.ActiveAssembly(app, Name);
        var def = assembly.ComponentDefinition;
        int max = p["max_occurrences"]?.Type == JTokenType.Integer ? Math.Max(1, Math.Min(20000, (int)p["max_occurrences"]!)) : 2000;
        var occurrences = new JArray();
        int unconstrained = 0;
        foreach (ComponentOccurrence occurrence in def.Occurrences)
        {
            if (occurrences.Count >= max) break;
            X.Deadline(ctx, "while reading assembly health");
            var item = new JObject { ["name"] = occurrence.Name, ["occurrence_id"] = X.Describe((global::Inventor.Document)assembly, occurrence),
                ["suppressed"] = occurrence.Suppressed, ["grounded"] = occurrence.Grounded };
            if (!occurrence.Suppressed)
            {
                try
                {
                    occurrence.GetDegreesOfFreedom(out int translations, out ObjectsEnumerator _, out int rotations, out ObjectsEnumerator _, out Point _);
                    item["dof_translation"] = translations;
                    item["dof_rotation"] = rotations;
                    if (!occurrence.Grounded && translations + rotations == 6) unconstrained++;
                }
                catch { item["dof_translation"] = null; }
            }
            occurrences.Add(item);
        }
        var failingConstraints = new JArray();
        int constraintCount = 0;
        foreach (AssemblyConstraint constraint in def.Constraints)
        {
            constraintCount++;
            if (!constraint.Suppressed && constraint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                failingConstraints.Add(new JObject { ["name"] = constraint.Name, ["health"] = constraint.HealthStatus.ToString() });
        }
        var failingJoints = new JArray();
        int jointCount = 0;
        foreach (AssemblyJoint joint in def.Joints)
        {
            jointCount++;
            if (!joint.Suppressed && joint.HealthStatus != HealthStatusEnum.kUpToDateHealth)
                failingJoints.Add(new JObject { ["name"] = joint.Name, ["health"] = joint.HealthStatus.ToString() });
        }
        return new JObject
        {
            ["healthy"] = failingConstraints.Count == 0 && failingJoints.Count == 0,
            ["occurrence_count"] = def.Occurrences.Count,
            ["unconstrained_occurrences"] = unconstrained,
            ["constraint_count"] = constraintCount,
            ["joint_count"] = jointCount,
            ["failing_constraints"] = failingConstraints,
            ["failing_joints"] = failingJoints,
            ["occurrences"] = occurrences,
        };
    }
}

/// <summary>get_representations: model states, design views, positional representations. Read-only.</summary>
public sealed class GetRepresentationsHandler : ExperimentalHandler
{
    public override string Name => "get_representations";
    public override bool IsReadOnly => true;
    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        var doc = X.Active(app);
        var result = new JObject { ["document_id"] = EntityReferences.DocumentId(doc), ["kind"] = X.Kind(doc) };
        dynamic manager = RepresentationX.Manager(doc);
        try
        {
            var states = RepresentationX.ModelStates(doc);
            result["model_states"] = RepresentationX.Names(states);
            result["active_model_state"] = (string)states.ActiveModelState.Name;
        }
        catch (Exception ex) when (ex is not CodedFailureException) { result["model_states_error"] = ex.Message; }
        result["design_views"] = RepresentationX.Names(manager.DesignViewRepresentations);
        result["active_design_view"] = (string)manager.ActiveDesignViewRepresentation.Name;
        if (doc is AssemblyDocument)
        {
            result["positional_representations"] = RepresentationX.Names(manager.PositionalRepresentations);
            result["active_positional_representation"] = (string)manager.ActivePositionalRepresentation.Name;
        }
        return result;
    }
}

/// <summary>
/// sample_parameter_motion (plan F13): drive one parameter across a range inside ONE transaction
/// that is always aborted, checking each sample. The revision is restored afterwards, like a batch
/// preview, so the study never invalidates anybody's plan.
/// </summary>
public sealed class SampleParameterMotionHandler : ExperimentalHandler
{
    private const int MaxPairs = 60;
    public override string Name => "sample_parameter_motion";
    public override bool IsReadOnly => false;

    protected override JToken Run(InventorCommandContext ctx, Application app, JObject p)
    {
        if (ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY, "Motion sampling edits then aborts; it needs write permission.");
        var doc = X.Active(app);
        string id = EntityReferences.DocumentId(doc);
        if ((string?)p["document_id"] != id) throw ConcurrencyFailure.DocumentChanged((string?)p["document_id"], id);
        if (ctx.Events == null || (string?)p["expected_revision"] != ctx.Events.Revision(id))
            throw ConcurrencyFailure.StaleRevision((string?)p["expected_revision"], ctx.Events?.Revision(id));
        string revision = ctx.Events.Revision(id);
        var parameter = X.FindParameter(X.ParametersOf(doc), X.Str(p, "parameter")) ?? throw new ArgumentException("No parameter named '" + p["parameter"] + "'.");
        double from = X.Num(p, "from_value"), to = X.Num(p, "to_value");
        int steps = X.Int(p, "steps", 2, 100);
        var checks = ValidationSpec.Parse(p["checks"], X.Kind(doc));
        string unit = parameter.get_Units();

        var samples = new JArray();
        JObject? firstFailure = null;
        bool? priorInteraction = null;
        Transaction? transaction = null;
        try
        {
            priorInteraction = app.UserInterfaceManager.UserInteractionDisabled;
            app.UserInterfaceManager.UserInteractionDisabled = true;
            transaction = app.TransactionManager.StartTransaction((_Document)doc, "Inventor SO motion study");
            if (transaction.HasParentTransaction) throw ConcurrencyFailure.TransactionBusy();
            for (int i = 0; i < steps; i++)
            {
                X.Deadline(ctx, "during the motion study");
                double value = from + (to - from) * i / (steps - 1);
                var sample = new JObject { ["index"] = i, ["value"] = value, ["unit"] = unit };
                string? failure = null;
                try
                {
                    parameter.Expression = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + unit;
                    if (!doc.Update2()) failure = "rebuild";
                    if (failure == null && doc is AssemblyDocument assembly) failure = CheckAssembly(app, assembly, checks, sample);
                }
                catch (Exception ex) { failure = "error: " + ex.Message; }
                sample["ok"] = failure == null;
                if (failure != null) { sample["failure"] = failure; firstFailure ??= (JObject)sample.DeepClone(); }
                samples.Add(sample);
            }
        }
        finally
        {
            try { if (transaction != null && ReferenceEquals(app.TransactionManager.CurrentTransaction, transaction)) transaction.Abort(); }
            finally
            {
                if (priorInteraction.HasValue) app.UserInterfaceManager.UserInteractionDisabled = priorInteraction.Value;
                ctx.Events?.TryRestoreRevision(id, revision);
            }
        }
        return new JObject
        {
            ["document_id"] = id,
            ["revision"] = ctx.Events?.Revision(id),
            ["parameter"] = parameter.Name,
            ["checks"] = checks.ToJson(),
            ["samples"] = samples,
            ["first_failure"] = firstFailure,
            ["valid_ranges"] = Ranges(samples),
            ["rolled_back"] = true,
        };
    }

    private static string? CheckAssembly(Application app, AssemblyDocument assembly, ValidationSpec checks, JObject sample)
    {
        var def = assembly.ComponentDefinition;
        foreach (AssemblyConstraint c in def.Constraints)
            if (!c.Suppressed && c.HealthStatus != HealthStatusEnum.kUpToDateHealth) return "constraint_health: " + c.Name;
        bool interference = checks.Has(ValidationSpec.Interference);
        double? clearance = checks.Find(ValidationSpec.MinClearance)?.ValueMm;
        if (!interference && clearance == null) return null;
        var parts = def.Occurrences.Cast<ComponentOccurrence>().Where(o => !o.Suppressed).ToArray();
        if (parts.Length > MaxPairs) return "too many occurrences for pairwise checks";
        double minimum = double.MaxValue;
        for (int i = 0; i < parts.Length; i++)
            for (int j = i + 1; j < parts.Length; j++)
            {
                var pair = app.TransientObjects.CreateObjectCollection();
                pair.Add(parts[i]);
                pair.Add(parts[j]);
                if (interference && def.AnalyzeInterference(pair).Count > 0) return "interference: " + parts[i].Name + "/" + parts[j].Name;
                if (clearance != null)
                {
                    double distance = UnitConvert.CmToMm(app.MeasureTools.GetMinimumDistance(parts[i], parts[j]));
                    minimum = Math.Min(minimum, distance);
                    if (distance < clearance) { sample["min_clearance_mm"] = distance; return "min_clearance: " + parts[i].Name + "/" + parts[j].Name; }
                }
            }
        if (clearance != null && minimum < double.MaxValue) sample["min_clearance_mm"] = minimum;
        return null;
    }

    private static JArray Ranges(JArray samples)
    {
        var ranges = new JArray();
        double? start = null, last = null;
        foreach (JObject sample in samples)
        {
            bool ok = (bool)sample["ok"]!;
            double value = (double)sample["value"]!;
            if (ok) { start ??= value; last = value; }
            else if (start != null) { ranges.Add(new JArray(start.Value, last!.Value)); start = null; }
        }
        if (start != null) ranges.Add(new JArray(start.Value, last!.Value));
        return ranges;
    }
}
#endif
