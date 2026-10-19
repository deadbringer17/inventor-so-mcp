#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Read-only native ownership of spline handles and their structural endpoints.</summary>
internal sealed class NativeSketchSplineHandleStructure
{
    internal sealed class Ownership
    {
        internal Ownership(SketchSplineHandle handle, SketchSpline spline, SketchPoint fitPoint, string role)
        { Handle = handle; Spline = spline; FitPoint = fitPoint; Role = role; }
        internal SketchSplineHandle Handle { get; }
        internal SketchSpline Spline { get; }
        internal SketchPoint FitPoint { get; }
        internal string Role { get; }
    }

    private readonly Dictionary<IntPtr, SketchSplineHandle> _owners = new();
    private readonly List<SketchSplineHandle> _handles = new();
    // Hold indexed identities and their owners alive throughout a capture/guard.
    private readonly List<object> _objects = new();
    internal IReadOnlyList<SketchSplineHandle> Handles => _handles.AsReadOnly();

    internal static NativeSketchSplineHandleStructure Capture(PlanarSketch sketch, Action? deadline = null)
    {
        if (sketch == null) throw new ArgumentNullException(nameof(sketch));
        var result = new NativeSketchSplineHandleStructure();
        var fitPoints = new List<SketchPoint>(); int entities = 0;
        foreach (SketchEntity entity in sketch.SketchEntities)
        {
            deadline?.Invoke();
            if (++entities > 2000) throw new InvalidOperationException("Spline handle ownership exceeds the sketch budget.");
            if (entity is not SketchSplineHandle handle)
            {
                if (entity.Type == ObjectTypeEnum.kSketchSplineHandleObject)
                    throw new InvalidOperationException("Native spline handle ownership is unavailable.");
                continue;
            }
            var ownership = DescribeOwner(handle, sketch);
            if (result.Owner((SketchEntity)handle) != null) continue;
            result._handles.Add(handle); fitPoints.Add(ownership.FitPoint);
            result.Add(handle, handle); result.Add(handle.StartSketchPoint, handle); result.Add(handle.EndSketchPoint, handle);
            result._objects.Add(ownership.Spline); result._objects.Add(ownership.FitPoint);
        }
        // A fit point is selectable through the spline. It cannot simultaneously be
        // an endpoint of this or another structural handle in a complete representation.
        foreach (var point in fitPoints)
        {
            deadline?.Invoke();
            if (result.Owner((SketchEntity)point) != null)
                throw new InvalidOperationException("Spline fit point and structural handle ownership collide.");
        }
        return result;
    }

    internal static Ownership DescribeOwner(SketchSplineHandle handle, PlanarSketch sketch)
    {
        if (handle == null || sketch == null) throw new ArgumentNullException(handle == null ? nameof(handle) : nameof(sketch));
        var spline = handle.Spline; var fitPoint = handle.FitPoint;
        var start = handle.StartSketchPoint; var end = handle.EndSketchPoint;
        if (spline == null || fitPoint == null || start == null || end == null
            || !SketchReferences.SameComObject(handle.Parent, sketch)
            || !SketchReferences.SameComObject(spline.Parent, sketch)
            || !SketchReferences.SameComObject(fitPoint.Parent, sketch)
            || !SketchReferences.SameComObject(start.Parent, sketch)
            || !SketchReferences.SameComObject(end.Parent, sketch))
            throw new InvalidOperationException("Spline handle, owner and points do not belong to the same native sketch.");
        if (SketchReferences.SameComObject(start, end) || SketchReferences.SameComObject(start, fitPoint)
            || SketchReferences.SameComObject(end, fitPoint))
            throw new InvalidOperationException("Spline handle endpoint ownership is not representable.");
        // Validate actual membership, never nearest coordinates or fit-point collection indices as identity.
        int count = spline.FitPointCount;
        if (count < 1 || count > 2000) throw new InvalidOperationException("Spline fit-point ownership exceeds the native budget.");
        int matches = 0;
        for (int i = 1; i <= count; i++)
            if (SketchReferences.SameComObject(spline.get_FitPoint(i), fitPoint)) matches++;
        if (matches != 1) throw new InvalidOperationException("Spline handle fit-point membership is unavailable or ambiguous.");
        bool first = spline.StartSketchPoint != null && SketchReferences.SameComObject(spline.StartSketchPoint, fitPoint);
        bool last = spline.EndSketchPoint != null && SketchReferences.SameComObject(spline.EndSketchPoint, fitPoint);
        if (first && last) throw new InvalidOperationException("Closed spline endpoint handle role is ambiguous.");
        return new Ownership(handle, spline, fitPoint, first ? "Start" : last ? "End" : "Interior");
    }

    internal SketchSplineHandle? Owner(SketchEntity entity)
    {
        IntPtr identity = Marshal.GetIUnknownForObject(entity);
        try { return _owners.TryGetValue(identity, out var handle) ? handle : null; }
        finally { Marshal.Release(identity); }
    }

    private void Add(object entity, SketchSplineHandle owner)
    {
        IntPtr identity = Marshal.GetIUnknownForObject(entity);
        try
        {
            if (_owners.TryGetValue(identity, out var previous))
            {
                if (!SketchReferences.SameComObject(previous, owner))
                    throw new InvalidOperationException("Spline handle structural ownership is ambiguous.");
                return;
            }
            _owners.Add(identity, owner); _objects.Add(entity); _objects.Add(owner);
        }
        finally { Marshal.Release(identity); }
    }

    internal static void RefuseMutation(SketchEntity entity)
    {
        if (entity is SketchSplineHandle || entity.Type == ObjectTypeEnum.kSketchSplineHandleObject)
            throw new ArgumentException("Spline handles are structural; select the owning spline through its supported command.");
        if (entity is not SketchPoint || entity.Parent is not PlanarSketch sketch) return;
        // Scan only actual existing entities; never GetHandle, SetHandleStatus or a creator in a guard.
        if (Capture(sketch).Owner(entity) != null)
            throw new ArgumentException("Spline handle endpoints are structural; select the owning spline through its supported command.");
    }
}
#endif
