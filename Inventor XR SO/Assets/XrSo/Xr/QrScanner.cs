using System;
using System.Collections;
using System.Threading.Tasks;
using InventorXrSo.Unity.Pairing;
using InventorXrSo.Unity.Ui;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace InventorXrSo.Xr
{
    /// <summary>Reads the PC's pairing QR through the Quest 3 passthrough camera (Horizon OS camera permission).</summary>
    public sealed class QrScanner : MonoBehaviour
    {
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        private const float TimeoutSeconds = 60f;
        private WebCamTexture _camera;
        private Coroutine _loop;
        private Task<string> _decode;
        private void OnDisable() => Stop();

        public void Scan(Action<string> onResult, Action<string> onError)
        {
            Stop();
            _loop = StartCoroutine(Run(onResult, onError));
        }

        public void Stop()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
            if (_decode != null)
                _ = _decode.ContinueWith(task => { var ignored = task.Exception; }, TaskScheduler.Default);
            if (_camera != null)
            {
                _camera.Stop();
                Destroy(_camera);
                _camera = null;
            }
        }

        private IEnumerator Run(Action<string> onResult, Action<string> onError)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Unity WebCamTexture needs CAMERA in addition to the Horizon camera permission.
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
                float cameraDeadline = Time.unscaledTime + 20f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.unscaledTime < cameraDeadline)
                    yield return null;
                if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    _loop = null;
                    onError(UiText.CameraDenied);
                    yield break;
                }
            }
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(HeadsetCameraPermission);
                float waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) && waited < 20f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
                {
                    _loop = null;
                    onError(UiText.CameraDenied);
                    yield break;
                }
            }
#endif
            if (!TryOpenCamera())
            {
                Stop();
                _loop = null;
                onError(UiText.NoCamera);
                yield break;
            }
            float deadline = Time.unscaledTime + TimeoutSeconds;
            bool receivedFrame = false;
            while (Time.unscaledTime < deadline)
            {
                yield return new WaitForSecondsRealtime(0.3f);
                if (_camera.width < 32 || !_camera.didUpdateThisFrame) continue;
                if (!receivedFrame)
                {
                    receivedFrame = true;
                    Debug.Log("[XR SO QR] Camera frames available: " + _camera.width + "x" + _camera.height);
                }
                // Only the texture read touches Unity. QR search runs off the render thread.
                if (_decode != null && !_decode.IsCompleted) continue;
                Color32[] pixels;
                try { pixels = _camera.GetPixels32(); }
                catch (Exception)
                {
                    Stop();
                    onError(UiText.NoCamera);
                    yield break;
                }
                int width = _camera.width, height = _camera.height;
                _decode = Task.Run(() => QrDecoder.Decode(pixels, width, height));
                while (!_decode.IsCompleted) yield return null;
                if (_decode.IsFaulted)
                {
                    var ignored = _decode.Exception;
                    Stop();
                    onError(UiText.NoCamera);
                    yield break;
                }
                var text = _decode.Result;
                if (text == null) continue;
                Debug.Log("[XR SO QR] QR decoded.");
                Stop();
                onResult(text);
                yield break;
            }
            Stop();
            onError(UiText.QrTimeout);
        }

        private bool TryOpenCamera()
        {
            try
            {
                var devices = WebCamTexture.devices;
                if (devices.Length == 0) return false;
                int index = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var selector = new AndroidJavaClass("com.occhipinti.inventorxrso.PassthroughCameraSelector"))
                    index = selector.CallStatic<int>("findIndex", activity);
#endif
                if (index < 0 || index >= devices.Length) return false;
                Debug.Log("[XR SO QR] Opening passthrough camera: " + devices[index].name);
                _camera = new WebCamTexture(devices[index].name, 1280, 960);
                _camera.Play();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XR SO QR] Camera initialization failed: " + ex.GetType().Name);
                return false;
            }
        }
    }
}
