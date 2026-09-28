using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Glb
{
    /// <summary>One B-rep face's slice of a primitive's index buffer (from <c>primitive.extras.faces</c>).</summary>
    public sealed class FaceRange
    {
        public FaceRange(string faceId, int ordinal, int firstIndex, int indexCount)
        {
            FaceId = faceId;
            Ordinal = ordinal;
            FirstIndex = firstIndex;
            IndexCount = indexCount;
        }

        public string FaceId { get; }
        public int Ordinal { get; }
        public int FirstIndex { get; }
        public int IndexCount { get; }
    }

    /// <summary>Triangle index → face, by binary search over the face ranges. No round trip to the PC.</summary>
    public sealed class FaceMap
    {
        private readonly FaceRange[] _faces;
        private readonly int[] _firstTriangle;
        private readonly int[] _endTriangle;

        public FaceMap(IEnumerable<FaceRange> faces, int indexCount)
        {
            _faces = faces.OrderBy(f => f.FirstIndex).ToArray();
            _firstTriangle = new int[_faces.Length];
            _endTriangle = new int[_faces.Length];
            for (int i = 0; i < _faces.Length; i++)
            {
                var f = _faces[i];
                if (f.FirstIndex < 0 || f.IndexCount <= 0 || f.FirstIndex % 3 != 0 || f.IndexCount % 3 != 0 || f.FirstIndex + f.IndexCount > indexCount)
                    throw new FormatException("Face " + f.FaceId + " is not a whole-triangle range inside the index buffer.");
                if (i > 0 && f.FirstIndex < _faces[i - 1].FirstIndex + _faces[i - 1].IndexCount)
                    throw new FormatException("Faces " + _faces[i - 1].FaceId + " and " + f.FaceId + " overlap.");
                _firstTriangle[i] = f.FirstIndex / 3;
                _endTriangle[i] = (f.FirstIndex + f.IndexCount) / 3;
            }
        }

        public int Count => _faces.Length;

        public FaceRange FaceAtTriangle(int triangle)
        {
            int lo = 0, hi = _faces.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (triangle < _firstTriangle[mid]) hi = mid - 1;
                else if (triangle >= _endTriangle[mid]) lo = mid + 1;
                else return _faces[mid];
            }
            return null;
        }

        public FaceRange Find(string faceId) => _faces.FirstOrDefault(f => f.FaceId == faceId);
    }
}
