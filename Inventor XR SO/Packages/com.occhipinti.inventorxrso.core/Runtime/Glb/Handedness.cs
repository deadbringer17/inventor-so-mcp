namespace InventorXrSo.Core.Glb
{
    /// <summary>
    /// glTF (right-handed) to Unity (left-handed): mirror X. Positions/normals negate X, triangles reverse
    /// winding (in place, so triangle order and face ranges are unchanged), matrices become S·M·S.
    /// </summary>
    public static class Handedness
    {
        public static float[] FlipX(float[] xyz)
        {
            var result = (float[])xyz.Clone();
            for (int i = 0; i < result.Length; i += 3) result[i] = -result[i];
            return result;
        }

        public static uint[] ReverseWinding(uint[] indices)
        {
            var result = (uint[])indices.Clone();
            for (int i = 0; i + 2 < result.Length; i += 3)
            {
                var b = result[i + 1];
                result[i + 1] = result[i + 2];
                result[i + 2] = b;
            }
            return result;
        }

        /// <summary>Column-major 4x4: negate every element in row 0 or column 0, but not both.</summary>
        public static float[] ConvertMatrix(float[] m)
        {
            var result = new float[16];
            for (int i = 0; i < 16; i++)
            {
                int row = i % 4, column = i / 4;
                result[i] = (row == 0) ^ (column == 0) ? -m[i] : m[i];
            }
            return result;
        }
    }
}
