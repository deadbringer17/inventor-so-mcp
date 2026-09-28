using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public static class MatrixUtil
    {
        /// <summary>Apply a column-major 4x4 (already in Unity handedness) as local TRS.</summary>
        public static void Apply(Transform target, float[] columnMajor)
        {
            var m = new Matrix4x4();
            for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
                m[row, column] = columnMajor[column * 4 + row];
            target.localPosition = m.GetColumn(3);
            target.localRotation = m.rotation;
            target.localScale = m.lossyScale;
        }
    }
}
