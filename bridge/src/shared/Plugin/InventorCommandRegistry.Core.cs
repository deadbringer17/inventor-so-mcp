#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.Core;

/// <summary>
/// Phase-2 Core registrar: registers the always-present read-only probes <c>health</c> and
/// <c>get_document_info</c>. Implemented only when an Inventor compile symbol is set (the handlers
/// touch the Inventor API); without a symbol the <c>partial void AddCore</c> stays an unimplemented
/// no-op so the registry still builds.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddCore(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new HealthHandler());
        add(new GetDocumentInfoHandler());
#if INVENTOR2027
        add(new SelectionHandler());
        add(new TopologyHandler());
        add(new ResolveEntityHandler());
        add(new EventsHandler());
        add(new AtomicBatchHandler());
        add(new SaveArtifactHandler());
        add(new NativePackagePlanHandler());
        add(new CreateDrawingHandler());
        add(new CheckpointHandler("create"));
        add(new CheckpointHandler("list"));
        add(new CheckpointHandler("diff"));
        add(new CheckpointHandler("restore"));
        add(new MoveComponentHandler());
        add(new MoveComponentHandler(insert: true));
        add(new EditConstraintHandler());
        add(new CreateConstraintHandler());
        add(new CreateJointHandler());
        add(new GroundComponentHandler());
        add(new WorkspaceDocumentHandler("new"));
        add(new WorkspaceDocumentHandler("open"));
        add(new WorkspaceDocumentHandler("activate"));
        add(new WorkspaceDocumentHandler("save"));
        add(new WorkspaceDocumentHandler("close"));
        add(new WorkspaceDocumentHandler("list"));
#endif
    }
}
#endif
