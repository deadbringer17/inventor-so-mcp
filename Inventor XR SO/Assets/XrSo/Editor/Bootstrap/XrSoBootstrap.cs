using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace InventorXrSo.Bootstrap
{
    /// <summary>Adds the packages the app needs (latest versions compatible with this editor).</summary>
    public static class XrSoBootstrap
    {
        public static readonly string[] Packages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.xr.management",
            "com.unity.xr.oculus",
            "com.unity.test-framework",
            "com.unity.ugui",
            "com.meta.xr.sdk.core",
        };

        private static AddAndRemoveRequest _request;

        /// <summary>Batch entry: -executeMethod InventorXrSo.Bootstrap.XrSoBootstrap.AddPackages (no -quit).</summary>
        public static void AddPackages()
        {
            _request = Client.AddAndRemove(Packages, null);
            EditorApplication.update += Wait;
        }

        private static void Wait()
        {
            if (!_request.IsCompleted) return;
            EditorApplication.update -= Wait;
            if (_request.Status == StatusCode.Success)
            {
                foreach (var package in _request.Result) Debug.Log("XRSO package " + package.name + " " + package.version);
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("XRSO package install failed: " + _request.Error?.message);
                EditorApplication.Exit(1);
            }
        }
    }
}
