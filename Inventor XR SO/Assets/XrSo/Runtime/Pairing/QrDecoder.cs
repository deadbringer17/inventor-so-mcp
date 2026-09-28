using UnityEngine;
using ZXing;
using ZXing.Common;

namespace InventorXrSo.Unity.Pairing
{
    public static class QrDecoder
    {
        private static readonly BarcodeReaderGeneric Reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { PossibleFormats = new[] { BarcodeFormat.QR_CODE }, TryHarder = true },
        };

        /// <summary>Text of the first QR code in a Unity image (rows bottom-up), or null.</summary>
        public static string Decode(Color32[] pixels, int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = (height - 1 - y) * width;   // flip: a mirrored QR would not decode
                for (int x = 0; x < width; x++)
                {
                    var c = pixels[sourceRow + x];
                    int i = (y * width + x) * 4;
                    rgba[i] = c.r; rgba[i + 1] = c.g; rgba[i + 2] = c.b; rgba[i + 3] = 255;
                }
            }
            lock (Reader)
                return Reader.Decode(new RGBLuminanceSource(rgba, width, height, RGBLuminanceSource.BitmapFormat.RGBA32))?.Text;
        }
    }
}
