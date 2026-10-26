using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace InventorXrSo.Core.Capture
{
    public enum CaptureMode { Object, Area }
    public enum CaptureState { Ready, Recording, Paused, Completed, Interrupted }

    /// <summary>Row-major rigid transform; translation in millimetres, column vectors, Unity left-handed axes.</summary>
    public sealed class CaptureTransform
    {
        private readonly double[] _values;
        public double[] Values => (double[])_values.Clone();
        [JsonConstructor]
        public CaptureTransform(double[] values)
        {
            if (values == null || values.Length != 16 || values.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new ArgumentException("Expected a finite 4x4 transform.", nameof(values));
            if (Math.Abs(values[12]) + Math.Abs(values[13]) + Math.Abs(values[14]) + Math.Abs(values[15] - 1) > 1e-5)
                throw new ArgumentException("Invalid homogeneous transform.", nameof(values));
            for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
            {
                double dot = 0;
                for (int r = 0; r < 3; r++) dot += values[r * 4 + a] * values[r * 4 + b];
                if (Math.Abs(dot - (a == b ? 1 : 0)) > 1e-4) throw new ArgumentException("Transform must be rigid.");
            }
            double det = values[0] * (values[5] * values[10] - values[6] * values[9])
                       - values[1] * (values[4] * values[10] - values[6] * values[8])
                       + values[2] * (values[4] * values[9] - values[5] * values[8]);
            if (Math.Abs(det - 1) > 1e-4) throw new ArgumentException("Transform must preserve handedness.");
            _values = (double[])values.Clone();
        }

        public static CaptureTransform Identity => new CaptureTransform(new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 });
        public double DistanceMm(CaptureTransform other)
        {
            double x = _values[3] - other._values[3], y = _values[7] - other._values[7], z = _values[11] - other._values[11];
            return Math.Sqrt(x*x + y*y + z*z);
        }
        public double AngleDegrees(CaptureTransform other)
        {
            double trace = 0;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) trace += _values[r*4+c] * other._values[r*4+c];
            return Math.Acos(Math.Max(-1, Math.Min(1, (trace - 1) / 2))) * 180 / Math.PI;
        }
    }

    public sealed class CaptureIntrinsics
    {
        public int Width { get; }
        public int Height { get; }
        public double Fx { get; }
        public double Fy { get; }
        public double Cx { get; }
        public double Cy { get; }
        [JsonConstructor]
        public CaptureIntrinsics(int width, int height, double fx, double fy, double cx, double cy)
        {
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
                new[] { fx, fy, cx, cy }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || fx <= 0 || fy <= 0 ||
                cx < 0 || cy < 0 || cx > width || cy > height) throw new ArgumentException("Invalid camera intrinsics.");
            Width = width; Height = height; Fx = fx; Fy = fy; Cx = cx; Cy = cy;
        }
    }

    public sealed class CaptureQuality
    {
        public double MeanLuminance { get; }
        public double ClippedFraction { get; }
        public double LaplacianVariance { get; }
        public double TextureFraction { get; }
        [JsonConstructor]
        public CaptureQuality(double meanLuminance, double clippedFraction, double laplacianVariance, double textureFraction)
        {
            if (new[] { meanLuminance, clippedFraction, laplacianVariance, textureFraction }.Any(v => double.IsNaN(v) || double.IsInfinity(v)) ||
                meanLuminance < 0 || meanLuminance > 255 || clippedFraction < 0 || clippedFraction > 1 ||
                laplacianVariance < 0 || textureFraction < 0 || textureFraction > 1) throw new ArgumentException("Invalid image quality.");
            MeanLuminance = meanLuminance; ClippedFraction = clippedFraction;
            LaplacianVariance = laplacianVariance; TextureFraction = textureFraction;
        }
    }

    public sealed class CaptureFrame
    {
        public long TimestampUnixMicroseconds { get; }
        public string CameraId { get; }
        public CaptureIntrinsics Intrinsics { get; }
        public CaptureTransform CameraToCaptureMm { get; }
        public CaptureTransform HeadToCaptureMm { get; }
        public CaptureTransform CameraToHeadMm { get; }
        public CaptureQuality Quality { get; }
        public bool TrackingValid { get; }
        public string PixelConvention => "RGB8; top-left origin; x right, y down; camera pose x right, y up, z forward";
        [JsonConstructor]
        public CaptureFrame(long timestampUnixMicroseconds, string cameraId, CaptureIntrinsics intrinsics,
            CaptureTransform cameraToCaptureMm, CaptureTransform headToCaptureMm, CaptureTransform cameraToHeadMm,
            CaptureQuality quality, bool trackingValid)
        {
            if (timestampUnixMicroseconds <= 0 || string.IsNullOrWhiteSpace(cameraId) || cameraId.Length > 64) throw new ArgumentException("Invalid frame identity.");
            TimestampUnixMicroseconds = timestampUnixMicroseconds; CameraId = cameraId;
            Intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
            CameraToCaptureMm = cameraToCaptureMm ?? throw new ArgumentNullException(nameof(cameraToCaptureMm));
            HeadToCaptureMm = headToCaptureMm ?? throw new ArgumentNullException(nameof(headToCaptureMm));
            CameraToHeadMm = cameraToHeadMm ?? throw new ArgumentNullException(nameof(cameraToHeadMm));
            Quality = quality ?? throw new ArgumentNullException(nameof(quality)); TrackingValid = trackingValid;
        }
    }

    /// <summary>Oriented volume selected by the operator; it is a crop hint, never observed geometry.</summary>
    public sealed class CaptureVolume
    {
        public CaptureTransform VolumeToCaptureMm { get; }
        public double WidthMm { get; }
        public double HeightMm { get; }
        public double DepthMm { get; }
        [JsonConstructor]
        public CaptureVolume(CaptureTransform volumeToCaptureMm, double widthMm, double heightMm, double depthMm)
        {
            if (new[] { widthMm, heightMm, depthMm }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 10 || v > 100000))
                throw new ArgumentException("Invalid capture volume.");
            VolumeToCaptureMm = volumeToCaptureMm ?? throw new ArgumentNullException(nameof(volumeToCaptureMm));
            WidthMm = widthMm; HeightMm = heightMm; DepthMm = depthMm;
        }
    }

    public sealed class ScanSession
    {
        public int SchemaVersion => 1;
        public string SessionId { get; }
        public CaptureMode Mode { get; }
        public string Device { get; }
        public string ApplicationVersion { get; }
        public DateTimeOffset CreatedAt { get; }
        public CaptureVolume Volume { get; }
        public CaptureTransform CaptureToTrackingMm { get; }
        public string CoordinateConvention => "millimetres; Unity left-handed x right, y up, z forward; row-major matrices, column vectors";
        [JsonConstructor]
        public ScanSession(string sessionId, CaptureMode mode, string device, string applicationVersion,
            DateTimeOffset createdAt, CaptureVolume volume, CaptureTransform captureToTrackingMm)
        {
            if (!Guid.TryParseExact(sessionId, "N", out _) || !Enum.IsDefined(typeof(CaptureMode), mode) || string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(applicationVersion))
                throw new ArgumentException("Invalid session metadata.");
            SessionId = sessionId; Mode = mode; Device = device; ApplicationVersion = applicationVersion; CreatedAt = createdAt;
            Volume = volume ?? throw new ArgumentNullException(nameof(volume));
            CaptureToTrackingMm = captureToTrackingMm ?? throw new ArgumentNullException(nameof(captureToTrackingMm));
        }
    }
}
