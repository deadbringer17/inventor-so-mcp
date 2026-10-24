#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

internal static class NativeSketchSplineTensionEdits99
{
    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,
        PlanarSketch sketch,string sketchId,JObject options)
    {
        var request=NativeSplineTensionEditPolicy99.Parse(options);
        if(ctx.ReadOnly)throw new CodedFailureException(InventorErrorCodes.READ_ONLY,"Spline tension editing requires write permission.");
        RequireOwned(ctx,app);
        if(document is not PartDocument part || app.ActiveDocument==null
            || !SketchReferences.SameComObject(app.ActiveDocument,document)
            || !SketchReferences.SameComObject(sketch.Parent,part.ComponentDefinition)
            || !SketchReferences.SameComObject(SketchReferences.Resolve(document,sketchId,"sketch"),sketch))
            throw new ArgumentException("Spline tension requires the active owning part or sheet-metal sketch.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId)??"";
        CheckBinding(request,documentId,revision,sketchId);
        X.Deadline(ctx,"resolving the native spline tension edit");
        var curve=SketchReferences.Resolve(document,request.Curve.EntityId,"sketch_entity") as SketchSpline
            ?? throw new ArgumentException("The selected native fit spline is unavailable.");
        if(curve.Type!=ObjectTypeEnum.kSketchSplineObject || !SketchReferences.SameComObject(curve.Parent,sketch)
            || curve.Reference || curve.HasReferenceComponent || curve.SketchBlockPath.Count!=0)
            throw new ArgumentException("Projected, derived, block or foreign spline tension cannot be edited.");
        NativeSketchImageStructure.RefuseMutation((SketchEntity)curve);
        var expected=PersistentEntityReference.Decode(request.Curve.EntityId).Key;
        var actual=PersistentEntityReference.Decode(SketchReferences.Describe(document,curve,"sketch_entity")).Key;
        if(!actual.SequenceEqual(expected))throw new ArgumentException("Native spline key differs from the selected reference.");
        RequireOwned(ctx,app);
        CheckBinding(request,documentId,ctx.Events?.Revision(documentId)??"",sketchId);
        X.Deadline(ctx,"setting the existing native spline tension");
        // One native setter; preserve the existing fit method and global options.
        // Solve/Update and rollback remain owned by the enclosing atomic batch.
        curve.Tension=request.Tension;
    }

    private static void CheckBinding(NativeSplineTensionEditPolicy99.Request request,string document,string revision,string sketch)
    {
        if(request.Curve.Revision!=revision)throw ConcurrencyFailure.StaleRevision(request.Curve.Revision,revision);
        NativeSplineTensionEditPolicy99.ValidateBinding(request,document,revision,sketch);
    }

    private static void RequireOwned(InventorCommandContext ctx,Application app)
    {
        var current=app.TransactionManager.CurrentTransaction;
        if(ctx.OwnedBatchTransaction==null || current==null || !SketchReferences.SameComObject(ctx.OwnedBatchTransaction,current))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED,"Spline tension editing requires the owned atomic batch transaction.");
    }
}
#endif
