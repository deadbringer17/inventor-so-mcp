using InventorXrSo.Unity.Pairing;
using NUnit.Framework;
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace InventorXrSo.Tests
{
    public class QrDecoderTests
    {
        [Test]
        public void DecodesAPairingQrFromUnityPixels()
        {
            const string payload = "{\"v\":1,\"host\":\"192.168.1.20\",\"port\":8443,\"ott\":\"abc\",\"cert_sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}";
            var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = 300, Height = 300, Margin = 2 } };
            var image = writer.Write(payload);   // BGRA32, top row first
            var pixels = new Color32[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int src = ((image.Height - 1 - y) * image.Width + x) * 4;   // Unity rows start at the bottom
                pixels[y * image.Width + x] = new Color32(image.Pixels[src + 2], image.Pixels[src + 1], image.Pixels[src], 255);
            }
            Assert.AreEqual(payload, QrDecoder.Decode(pixels, image.Width, image.Height));
        }

        [Test]
        public void ABlankImageHasNoCode() =>
            Assert.IsNull(QrDecoder.Decode(new Color32[64 * 64], 64, 64));
    }
}
