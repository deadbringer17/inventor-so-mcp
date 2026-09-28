#if INVENTOR2027 && SO_EXPERIMENTAL
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// Experimental-tier registrar (docs/INVENTOR_SO_MCP_IMPLEMENTATION_PLAN.md §26.3). Compiled only
/// with <c>-p:SoExperimental=true</c>; every handler also refuses to run unless the add-in was
/// started with <c>INVENTOR_SO_EXPERIMENTAL=1</c>. Names here must match the experimental entries of
/// <c>CadBatchCommandCatalog</c> (batch commands) or the experimental tool contracts (queries).
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddExperimental(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        // XR / visualization (queries and view state)
        add(new GetDisplayMeshHandler());
        add(new GetSceneGraphHandler());
        add(new GetVisualRevisionHandler());
        add(new HighlightEntityHandler());
        add(new FocusEntityHandler());
        add(new GetCameraHandler());
        add(new SetCameraHandler());
        add(new RaycastEntityHandler());
        add(new PickEntityHandler());
        add(new InspectXrHandler());
        add(new DesignContextXrHandler());
        add(new AssemblyContextXrHandler());
        add(new AssemblyMoveHandler());
        add(new AssemblyConstraintHandler());
        add(new AssemblyJointHandler());
        add(new XrHistoryHandler());
        add(new ActivateOpenDocumentXrHandler());

        // Inspection and intelligence (queries)
        add(new GetSketchInfoHandler());
        add(new GetDependenciesHandler());
        add(new GetSemanticStateHandler());
        add(new GetRepresentationsHandler());
        add(new GetAssemblyHealthHandler());
        add(new ValidateDrawingHandler());
        add(new SampleParameterMotionHandler());

        // Batch commands: sketch
        add(new DrawEllipseHandler());
        add(new DrawSplineHandler());
        add(new DrawSlotHandler());
        add(new DrawPolygonHandler());
        add(new OffsetSketchEntitiesHandler());
        add(new MirrorSketchEntitiesHandler());
        add(new MoveSketchPointHandler());
        add(new DeleteSketchEntityHandler());
        add(new AddDimensionHandler());
        add(new EditDimensionHandler());
        add(new DeleteDimensionHandler());

        // Batch commands: solid features, work geometry, parameters, document state
        add(new SweepHandler());
        add(new LoftHandler());
        add(new ShellHandler());
        add(new DraftHandler());
        add(new SplitHandler());
        add(new ThickenHandler());
        add(new ThreadHandler());
        add(new MirrorHandler());
        add(new CombineHandler());
        add(new MoveBodyHandler());
        add(new CreateWorkPointHandler());
        add(new CreateUcsHandler());
        add(new RenameWorkGeometryHandler());
        add(new DeleteWorkGeometryHandler());
        add(new RenameParameterHandler());
        add(new DeleteParameterHandler());
        add(new SetIPropertyBatchHandler());
        add(new SetVisibilityHandler());
        add(new ActivateDesignViewHandler());
        add(new ActivateModelStateHandler());
        add(new CreateModelStateHandler());

        // Batch commands: assembly
        add(new SuppressComponentHandler());
        add(new ReplaceComponentHandler());
        add(new PatternComponentHandler());
        add(new ActivatePositionalRepresentationHandler());
        add(new SetBomStructureHandler());

        // Batch commands: drawing
        add(new AddSheetHandler());
        add(new ActivateSheetHandler());
        add(new DeleteSheetHandler());
        add(new AddBaseViewHandler());
        add(new AddProjectedViewHandler());
        add(new MoveDrawingViewHandler());
        add(new SetViewScaleHandler());
        add(new AddNoteHandler());
        add(new AddPartsListHandler());
    }
}
#endif
