using System;

namespace InventorXrSo.Core.Capture
{
    public static class CaptureQualityEvaluator
    {
        /// <summary>Image evidence only. Texture/sharpness are heuristics, not a coverage or dimensional estimate.</summary>
        public static CaptureQuality EvaluateRgb(byte[] rgb, int width, int height)
        {
            if (width < 3 || height < 3 || rgb == null || rgb.Length != checked(width * height * 3)) throw new ArgumentException("Invalid RGB8 image.");
            double sum = 0, lapSum = 0, lapSquared = 0;
            int clipped = 0, textured = 0, count = 0;
            // Use adjacent pixels for Laplacian, subsample locations to bound CPU work.
            int step = Math.Max(1, Math.Min(width, height) / 160);
            for (int y = 1; y < height - 1; y += step)
            for (int x = 1; x < width - 1; x += step)
            {
                int i = y * width + x;
                double l = Luma(rgb, i), left = Luma(rgb, i - 1), right = Luma(rgb, i + 1);
                double up = Luma(rgb, i - width), down = Luma(rgb, i + width);
                double lap = left + right + up + down - 4*l;
                sum += l; lapSum += lap; lapSquared += lap*lap; count++;
                if (l < 8 || l > 247) clipped++;
                if (Math.Abs(right-left) + Math.Abs(down-up) >= 20) textured++;
            }
            double meanLap = lapSum / count;
            return new CaptureQuality(sum/count, (double)clipped/count, Math.Max(0, lapSquared/count - meanLap*meanLap), (double)textured/count);
        }
        private static double Luma(byte[] rgb, int i) => 0.2126*rgb[i*3] + 0.7152*rgb[i*3+1] + 0.0722*rgb[i*3+2];
    }

    public sealed class CaptureFrameSelector
    {
        private CaptureFrame _last;
        public string Evaluate(CaptureFrame frame)
        {
            if (!frame.TrackingValid) return "tracking_lost";
            if (_last != null && frame.TimestampUnixMicroseconds <= _last.TimestampUnixMicroseconds) return "timestamp_not_increasing";
            if (frame.Quality.MeanLuminance < 20 || frame.Quality.MeanLuminance > 235 || frame.Quality.ClippedFraction > 0.4) return "exposure";
            if (frame.Quality.LaplacianVariance < 25) return "low_sharpness";
            if (frame.Quality.TextureFraction < 0.02) return "low_texture";
            if (_last != null)
            {
                double seconds = (frame.TimestampUnixMicroseconds - _last.TimestampUnixMicroseconds) / 1000000.0;
                double distance = frame.CameraToCaptureMm.DistanceMm(_last.CameraToCaptureMm);
                double angle = frame.CameraToCaptureMm.AngleDegrees(_last.CameraToCaptureMm);
                if (seconds < 0.4) return "rate_limit";
                if (distance / seconds > 1000 || angle / seconds > 90) return "motion";
                if (distance < 40 && angle < 8) return "redundant";
            }
            return null;
        }
        // A failed disk write must not advance the baseline.
        public void Commit(CaptureFrame frame) => _last = frame;
    }
}
