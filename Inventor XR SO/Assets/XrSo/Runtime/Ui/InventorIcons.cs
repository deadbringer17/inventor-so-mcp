using System;
using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Only selected local Sprites enter the APK; resources are shared across UI rebuilds.</summary>
    public static class InventorIcons
    {
        private static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static bool TryGet(string key, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(key) || key.IndexOf('/') >= 0 || key.IndexOf('\\') >= 0 || key.IndexOf('.') >= 0) return false;
            if (!Loaded.TryGetValue(key, out sprite))
            {
                sprite = Resources.Load<Sprite>("InventorIcons/" + key);
                Loaded[key] = sprite;
            }
            return sprite != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Loaded.Clear();
    }
}
