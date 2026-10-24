#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers.Core;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Move one real fit/control point in the existing native spline.
/// No spline replacement, handle creation, constraint deletion, Solve or rebuild.</summary>
internal static class NativeSketchSplinePointEdits98
{
    internal static void Apply(InventorCommandContext ctx,Application app,global::Inventor.Document document,
        PlanarSketch sketch,string sketchId,JObject options)
    {
        // Freeze and validate the entire request before resolving or mutating native entities.
        var request=NativeSplinePointEditPolicy98.Parse(options);
        if(ctx.ReadOnly) throw new CodedFailureException(InventorErrorCodes.READ_ONLY,"Spline point editing requires write permission.");
        RequireOwned(ctx,app);
        if(document is not PartDocument part || app.ActiveDocument==null
            || !SketchReferences.SameComObject(app.ActiveDocument,document)
            || !SketchReferences.SameComObject(sketch.Parent,part.ComponentDefinition)
            || !SketchReferences.SameComObject(SketchReferences.Resolve(document,sketchId,"sketch"),sketch))
            throw new ArgumentException("Spline point editing requires the active owning part or sheet-metal sketch.");
        string documentId=EntityReferences.DocumentId(document),revision=ctx.Events?.Revision(documentId) ?? "";
        CheckBinding(request,documentId,revision,sketchId);
        X.Deadline(ctx,"resolving the native spline point edit");
        var source=SketchReferences.Resolve(document,request.Curve.EntityId,"sketch_entity") as SketchEntity
            ?? throw new ArgumentException("The selected native spline is unavailable.");
        var point=SketchReferences.Resolve(document,request.Point.EntityId,"sketch_entity") as SketchPoint
            ?? throw new ArgumentException("The selected native spline point is unavailable.");
        bool fit=request.Curve.Type=="spline";
        if((fit ? source is not SketchSpline || source.Type!=ObjectTypeEnum.kSketchSplineObject
                : source is not SketchControlPointSpline || source.Type!=ObjectTypeEnum.kSketchControlPointSplineObject)
            || point.Type!=ObjectTypeEnum.kSketchPointObject
            || !SketchReferences.SameComObject(source.Parent,sketch) || !SketchReferences.SameComObject(point.Parent,sketch))
            throw new ArgumentException("Spline point native types or sketch owners differ from the selection.");
        if(source.Reference || source.HasReferenceComponent || source.SketchBlockPath.Count!=0
            || point.Reference || point.HasReferenceComponent || point.SketchBlockPath.Count!=0)
            throw new ArgumentException("Projected, derived or block spline points cannot be edited through this operation.");
        // These are read-only scans of existing native image/handle structure.
        // A legitimate fit point distinct from handle endpoints remains editable.
        NativeSketchImageStructure.RefuseMutation(source);
        NativeSketchImageStructure.RefuseMutation((SketchEntity)point);
        var selectedKey=PersistentEntityReference.Decode(request.Point.EntityId).Key;
        var sourceKey=PersistentEntityReference.Decode(request.Curve.EntityId).Key;
        if(!NativeKey(document,source).SequenceEqual(sourceKey) || !NativeKey(document,point).SequenceEqual(selectedKey))
            throw new ArgumentException("Resolved spline or point native key differs from the selected reference.");
        int count=fit ? ((SketchSpline)source).FitPointCount : ((SketchControlPointSpline)source).ControlPointCount;
        if(count<2 || count>2000) throw new ArgumentException("Spline point membership exceeds the native representation budget.");
        int matches=0;
        for(int i=1;i<=count;i++)
        {
            X.Deadline(ctx,"checking actual native spline point membership");
            SketchPoint member=fit ? ((SketchSpline)source).get_FitPoint(i) : ((SketchControlPointSpline)source).get_ControlPoint(i);
            if(member==null || !SketchReferences.SameComObject(member.Parent,sketch))
                throw new ArgumentException("Native spline point membership is unavailable or has a foreign owner.");
            bool same=SketchReferences.SameComObject(member,point),sameKey=NativeKey(document,member).SequenceEqual(selectedKey);
            if(same!=sameKey) throw new ArgumentException("Native spline point identity and persistent key disagree.");
            if(same) matches++;
        }
        // Closed native splines may enumerate the same endpoint more than once.
        // Membership is the actual COM object and key, never the enumeration index.
        if(matches==0) throw new ArgumentException("The selected native point is not a fit or control point of the selected spline.");
        var destination=app.TransientGeometry.CreatePoint2d(UnitConvert.MmToCm(request.Xmm),UnitConvert.MmToCm(request.Ymm));
        RequireOwned(ctx,app);
        CheckBinding(request,documentId,ctx.Events?.Revision(documentId) ?? "",sketchId);
        X.Deadline(ctx,"moving the selected native spline point");
        // Preserve native identity and all existing constraints. Solver refusal is
        // handled by the caller's atomic transaction; no retry or repair here.
        point.MoveTo(destination);
    }

    private static byte[] NativeKey(global::Inventor.Document document,object value)
        =>PersistentEntityReference.Decode(SketchReferences.Describe(document,value,"sketch_entity")).Key;

    private static void CheckBinding(NativeSplinePointEditPolicy98.Request request,string document,string revision,string sketch)
    {
        foreach(var reference in new[] {request.Curve,request.Point})
            if(reference.Revision!=revision) throw ConcurrencyFailure.StaleRevision(reference.Revision,revision);
        NativeSplinePointEditPolicy98.ValidateBinding(request,document,revision,sketch);
    }

    private static void RequireOwned(InventorCommandContext ctx,Application app)
    {
        var current=app.TransactionManager.CurrentTransaction;
        if(ctx.OwnedBatchTransaction==null || current==null || !SketchReferences.SameComObject(ctx.OwnedBatchTransaction,current))
            throw new CodedFailureException(InventorErrorCodes.ATOMIC_REQUIRED,"Spline point editing requires the owned atomic batch transaction.");
    }
}
#endif
