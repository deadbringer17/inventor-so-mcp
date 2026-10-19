#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>Exact native ownership of image boundary geometry. No reference-key or coordinate inference.</summary>
internal sealed class NativeSketchImageStructure
{
    private readonly Dictionary<IntPtr, SketchImage> _owners = new();
    // Keep each indexed COM identity alive until the capture/guard has finished.
    private readonly List<object> _objects = new();

    internal static NativeSketchImageStructure Capture(PlanarSketch sketch, Action? deadline = null)
    {
        var result = new NativeSketchImageStructure();
        int images = 0;
        foreach (SketchImage image in sketch.SketchImages)
        {
            deadline?.Invoke();
            if (++images > 2000) throw new InvalidOperationException("Image ownership exceeds the snapshot budget.");
            int edges = 0;
            foreach (SketchEntity entity in image.BoundaryGeometry)
            {
                deadline?.Invoke();
                if (++edges > 4 || entity is not SketchLine line)
                    throw new InvalidOperationException("Image boundary ownership is unavailable.");
                result.Add(line, image);
                result.Add(line.StartSketchPoint, image);
                result.Add(line.EndSketchPoint, image);
            }
            if (edges != 4) throw new InvalidOperationException("Image boundary ownership is incomplete.");
        }
        return result;
    }

    internal SketchImage? Owner(SketchEntity entity)
    {
        IntPtr identity = Marshal.GetIUnknownForObject(entity);
        try { return _owners.TryGetValue(identity, out var image) ? image : null; }
        finally { Marshal.Release(identity); }
    }

    private void Add(object entity, SketchImage image)
    {
        IntPtr identity = Marshal.GetIUnknownForObject(entity);
        try
        {
            if (_owners.TryGetValue(identity, out var previous))
            {
                if (!SketchReferences.SameComObject(previous, image))
                    throw new InvalidOperationException("Image boundary ownership is ambiguous.");
                return;
            }
            _owners.Add(identity, image);
            _objects.Add(entity);
            _objects.Add(image);
        }
        finally { Marshal.Release(identity); }
    }

    internal static void RefuseMutation(SketchEntity entity)
    {
        NativeSketchSplineHandleStructure.RefuseMutation(entity);
        if (entity.Parent is not PlanarSketch sketch) return;
        // An incomplete native scan throws before the selected object reaches any mutation.
        if (Capture(sketch).Owner(entity) != null)
            throw new ArgumentException("Image boundary geometry is structural; select the image through its supported image command.");
    }

    internal JArray BoundarySlots(IReadOnlyList<object> entities,Action? deadline=null)
    {
        var maps = new JArray();
        for(int imageSlot=0;imageSlot<entities.Count;imageSlot++)
        {
            deadline?.Invoke();
            if(entities[imageSlot] is not SketchImage image) continue;
            var edges=new JArray();var points=new JArray();
            for(int slot=0;slot<entities.Count;slot++)
                if(entities[slot] is SketchEntity entity && Owner(entity) is SketchImage owner && SketchReferences.SameComObject(owner,image))
                {
                    if(entity is SketchLine) edges.Add(slot);
                    else if(entity is SketchPoint) points.Add(slot);
                }
            if(edges.Count!=4 || points.Count!=4) throw new InvalidOperationException("Image boundary entities are not completely represented.");
            maps.Add(new JObject { ["image_slot"]=imageSlot,["edge_slots"]=edges,["point_slots"]=points });
        }
        return maps;
    }
}
#endif
