using System;
using System.Collections;
using InventorXrSo.Core.Capture;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace InventorXrSo.Xr.Capture
{
    /// <summary>Single RGB camera. Metadata is cached before readback; pixels and metadata never cross an await independently.</summary>
    public sealed class QuestCameraSource : MonoBehaviour
    {
        private PassthroughCameraAccess _camera;
        private Coroutine _startup;
        private Matrix4x4 _trackingToCapture;
        private int _generation, _recenterCount;
        private bool _pending;
        private float _nextReadback, _lastFrameAt;
        private DateTime _lastTimestamp;
        private Action<CaptureFrame, byte[]> _onFrame;
        private Action<string> _onError;
        public bool Running { get; private set; }

        public void Begin(Matrix4x4 trackingToCapture, Action<CaptureFrame, byte[]> onFrame, Action<string> onError)
        {
            Stop();
            _trackingToCapture = trackingToCapture; _onFrame = onFrame; _onError = onError;
            _recenterCount = OVRPlugin.GetLocalTrackingSpaceRecenterCount();
            _startup = StartCoroutine(StartCamera());
        }

        public void Stop()
        {
            _generation++; Running = false; _pending = false;
            if (_startup != null) StopCoroutine(_startup);
            _startup = null;
            if (_camera != null) _camera.enabled = false;
        }

        private IEnumerator StartCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(QrScanner.HeadsetCameraPermission))
            {
                Permission.RequestUserPermission(QrScanner.HeadsetCameraPermission);
                float deadline = Time.realtimeSinceStartup + 20;
                while (!Permission.HasUserAuthorizedPermission(QrScanner.HeadsetCameraPermission) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!Permission.HasUserAuthorizedPermission(QrScanner.HeadsetCameraPermission)) { Fail("camera_permission_denied"); yield break; }
            }
            if (!PassthroughCameraAccess.IsSupported || !SystemInfo.supportsAsyncGPUReadback) { Fail("camera_not_supported"); yield break; }
            if (_camera == null)
            {
                // Configure while inactive: enabling starts the native stream immediately.
                var go = new GameObject("Reality Capture RGB left");
                go.transform.SetParent(transform, false); go.SetActive(false);
                _camera = go.AddComponent<PassthroughCameraAccess>();
                _camera.enabled = false;
                _camera.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
                _camera.RequestedResolution = new Vector2Int(1280, 960);
                _camera.MaxFramerate = 15;
                go.SetActive(true);
            }
            _camera.enabled = true;
            float readyDeadline = Time.realtimeSinceStartup + 15;
            while (_camera.enabled && !_camera.IsPlaying && Time.realtimeSinceStartup < readyDeadline) yield return null;
            if (!_camera.IsPlaying) { Fail("camera_start_failed"); yield break; }
            _lastTimestamp = default; _nextReadback = 0; _lastFrameAt = Time.realtimeSinceStartup;
            Running = true;
#else
            yield return null;
            Fail("camera_requires_quest");
#endif
            _startup = null;
        }

        private void Update()
        {
            if (!Running) return;
            if (!TrackingValid()) { Fail("tracking_lost"); return; }
            if (_recenterCount != OVRPlugin.GetLocalTrackingSpaceRecenterCount()) { Fail("tracking_origin_changed"); return; }
            if (Time.realtimeSinceStartup - _lastFrameAt > 5) { Fail("camera_frame_timeout"); return; }
            if (!_camera.IsPlaying || !_camera.IsUpdatedThisFrame || _camera.Timestamp == _lastTimestamp) return;
            _lastFrameAt = Time.realtimeSinceStartup;
            if (_pending || Time.realtimeSinceStartup < _nextReadback) return;
            _lastTimestamp = _camera.Timestamp; _nextReadback = Time.realtimeSinceStartup + 0.5f;
            try { ReadFrame(); }
            catch (Exception) { Fail("camera_metadata_invalid"); }
        }

        private void ReadFrame()
        {
            var timestamp = _camera.Timestamp;
            var cameraPose = _camera.GetCameraPose(); // MRUK 207 returns a pose in tracking space at the image timestamp.
            var lens = _camera.Intrinsics.LensOffset;
            if (!ValidPose(cameraPose) || !ValidPose(lens)) { Fail("camera_pose_unavailable"); return; }
            var cameraToTracking = Matrix4x4.TRS(cameraPose.position, cameraPose.rotation, Vector3.one);
            var cameraToHead = Matrix4x4.TRS(lens.position, lens.rotation, Vector3.one);
            var cameraToCapture = _trackingToCapture * cameraToTracking;
            var headToCapture = cameraToCapture * cameraToHead.inverse;
            var resolution = _camera.CurrentResolution;
            // Derive image intrinsics from the SDK's own projection so sensor cropping and resolution fallback agree.
            var identity = new Pose(Vector3.zero, Quaternion.identity);
            var centre = _camera.WorldToViewportPoint(Vector3.forward, identity);
            var x = _camera.WorldToViewportPoint(new Vector3(1, 0, 1), identity);
            var y = _camera.WorldToViewportPoint(new Vector3(0, 1, 1), identity);
            var imageIntrinsics = new CaptureIntrinsics(resolution.x, resolution.y,
                (x.x - centre.x) * resolution.x, (y.y - centre.y) * resolution.y,
                centre.x * resolution.x, (1 - centre.y) * resolution.y);
            long microseconds = (timestamp.ToUniversalTime().Ticks - DateTime.UnixEpoch.Ticks) / 10;
            var cameraMatrix = ToContract(cameraToCapture);
            var headMatrix = ToContract(headToCapture);
            var extrinsics = ToContract(cameraToHead);
            int generation = _generation;
            _pending = true;
            // Request is queued after MRUK's IssuePluginEvent for this timestamp, without blocking the rendering thread.
            AsyncGPUReadback.Request(_camera.GetTexture(), 0, TextureFormat.RGBA32, request =>
            {
                if (this == null || generation != _generation || !Running) return;
                if (request.hasError) { Fail("camera_readback_failed"); return; }
                if (!TrackingValid() || _recenterCount != OVRPlugin.GetLocalTrackingSpaceRecenterCount()) { Fail("tracking_lost"); return; }
                var rgba = request.GetData<byte>();
                if (rgba.Length != resolution.x * resolution.y * 4) { Fail("camera_image_size_invalid"); return; }
                var rgb = new byte[resolution.x * resolution.y * 3];
                // GPU rows are bottom-up. Persist top-down RGB without mutating the SDK's native buffer.
                for (int row = 0; row < resolution.y; row++)
                for (int col = 0; col < resolution.x; col++)
                {
                    int source = ((resolution.y - 1 - row) * resolution.x + col) * 4;
                    int target = (row * resolution.x + col) * 3;
                    rgb[target] = rgba[source]; rgb[target + 1] = rgba[source + 1]; rgb[target + 2] = rgba[source + 2];
                }
                _pending = false;
                _onFrame(new CaptureFrame(microseconds, "quest_rgb_left", imageIntrinsics, cameraMatrix, headMatrix, extrinsics,
                    CaptureQualityEvaluator.EvaluateRgb(rgb, resolution.x, resolution.y), true), rgb);
            });
        }

        public static CaptureTransform ToContract(Matrix4x4 matrix)
        {
            var values = new double[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) values[r*4+c] = matrix[r,c] * (c == 3 && r < 3 ? 1000.0 : 1);
            return new CaptureTransform(values);
        }
        public static bool TrackingValid() => OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head) && OVRPlugin.GetNodeOrientationTracked(OVRPlugin.Node.Head);
        private static bool ValidPose(Pose pose)
        {
            float norm = Quaternion.Dot(pose.rotation, pose.rotation);
            return float.IsFinite(norm) && Mathf.Abs(norm - 1) < 0.001f &&
                   float.IsFinite(pose.position.x) && float.IsFinite(pose.position.y) && float.IsFinite(pose.position.z);
        }
        private void Fail(string reason) { var callback = _onError; Stop(); callback?.Invoke(reason); }
        private void OnDisable() => Stop();
        private void OnApplicationPause(bool paused) { if (paused && (Running || _startup != null)) Fail("application_suspended"); }
        private void OnApplicationFocus(bool focused) { if (!focused && Running) Fail("application_focus_lost"); }
    }
}
