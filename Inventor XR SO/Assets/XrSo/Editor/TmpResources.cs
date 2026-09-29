using UnityEditor;

namespace InventorXrSo.Editor
{
    public static class TmpResources
    {
        /// <summary>
        /// Batch: importa TMP Essential Resources (font SDF di default e impostazioni).
        /// L'import del pacchetto e asincrono: si esce solo a import concluso (niente -quit).
        /// </summary>
        public static void ImportEssentials()
        {
            AssetDatabase.importPackageCompleted += _ => EditorApplication.Exit(0);
            AssetDatabase.importPackageFailed += (_, error) =>
            {
                UnityEngine.Debug.LogError("TMP import failed: " + error);
                EditorApplication.Exit(1);
            };
            AssetDatabase.importPackageCancelled += _ => EditorApplication.Exit(2);
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
        }
    }
}
