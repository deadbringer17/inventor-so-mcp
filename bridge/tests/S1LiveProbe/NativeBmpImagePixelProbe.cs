using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Inventor;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Research: copy a native picture's bitmap, without freeing Inventor's handle.
    // The pixel budget is explicit; this fixture supplies six pixels, not a product limit.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap37
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmapInfo37
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    private static extern int BitmapGetObject37(IntPtr bitmap, int size, out NativeBitmap37 data);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint first, uint rows,
        [Out] byte[] pixels, ref NativeBitmapInfo37 info, uint usage);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private static JObject BmpPicturePixels37(object picture, int maxPixels, Inventor.Application app)
    {
        if (maxPixels < 1) throw new ArgumentOutOfRangeException(nameof(maxPixels));
        if (GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)app.MainFrameHWND)), out uint owner) == 0
            || owner != (uint)System.Environment.ProcessId)
            throw new InvalidOperationException("Bitmap handles may only be copied in the owning Inventor process; no external handle will be dereferenced.");
        dynamic native = picture;
        int type = Convert.ToInt32(native.Type);
        var result = new JObject { ["picture_type"] = type };
        if (type != 1) throw new InvalidOperationException("Native image picture is not a bitmap.");
        // OLE_HANDLE is a 32-bit handle value, including on x64. Do not DeleteObject it.
        IntPtr bitmap = new(unchecked((long)(uint)Convert.ToInt32(native.Handle)));
        int structureSize = Marshal.SizeOf<NativeBitmap37>();
        if (bitmap == IntPtr.Zero || BitmapGetObject37(bitmap, structureSize, out var data) != structureSize)
            throw new InvalidOperationException("Cannot inspect the native bitmap handle.");
        if (data.Width <= 0 || data.Height <= 0 || (long)data.Width * data.Height > maxPixels)
            throw new InvalidOperationException("Native bitmap dimensions exceed the explicit probe budget.");
        int length = checked(data.Width * data.Height * 4);
        var pixels = new byte[length];
        var info = new NativeBitmapInfo37 { Size = (uint)Marshal.SizeOf<NativeBitmapInfo37>(),
            Width = data.Width, Height = -data.Height, Planes = 1, BitCount = 32, SizeImage = (uint)length };
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("Cannot create an owned bitmap-copy DC.");
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)data.Height, pixels, ref info, 0) != data.Height)
                throw new InvalidOperationException("Native bitmap copy did not return every row.");
        }
        finally
        {
            if (!DeleteDC(dc)) throw new InvalidOperationException("Owned bitmap-copy DC cleanup failed.");
        }
        // This research reports BGR triples only. X is unused; alpha is a separate API picture.
        var bgr = new JArray();
        for (int i = 0; i < pixels.Length; i += 4) bgr.Add(new JArray(pixels[i], pixels[i + 1], pixels[i + 2]));
        result["width"] = data.Width; result["height"] = data.Height;
        result["native_bits_per_pixel"] = data.BitsPixel;
        result["top_down_bgr"] = bgr;
        result["copied_bgrx_sha256"] = Convert.ToHexString(SHA256.HashData(pixels));
        return result;
    }

    private static JObject BmpImagePixels37(SketchImage image, bool topDown)
    {
        var result = BmpPicturePixels37(image.Image, 6, (Inventor.Application)image.Application);
        result["transparent"] = image.Transparent; result["visible"] = image.Visible;
        if ((int)result["width"]! != 3 || (int)result["height"]! != 2)
            throw new InvalidOperationException("Native image pixels differ from the 3x2 source dimensions.");
        var expected = new JArray();
        for (int y = 0; y < 2; y++)
        {
            int sourceRow = topDown ? y : 1 - y;
            for (int x = 0; x < 3; x++)
                expected.Add(new JArray(sourceRow == 0 ? 255 : 0, x * 100, sourceRow == 1 ? 255 : 0));
        }
        result["expected_top_down_bgr"] = expected;
        result["exact_source_colors"] = JToken.DeepEquals(expected, result["top_down_bgr"]);
        if (!(bool)result["exact_source_colors"]!)
            throw new InvalidOperationException("Native bitmap colors or row orientation differ from the source BMP.");
        return result;
    }

    private static JObject BmpImagePixelResearch37(SketchImage image, bool topDown)
    {
        var result = new JObject { ["evidence"] = "OUT-OF-PROCESS COM pixel getter research; not in-process add-in proof." };
        void Observe(string name, Func<JObject> read)
        {
            try { result[name] = new JObject { ["completed"] = true, ["data"] = read() }; }
            catch (Exception ex) { result[name] = new JObject { ["completed"] = false,
                ["hresult"] = "0x" + ex.HResult.ToString("X8"), ["error"] = ex.ToString() }; }
        }
        Observe("typed_image", () => BmpImagePixels37(image, topDown));
        Observe("dispatch_image", () => BmpPicturePixels37(((dynamic)image).Image, 6, (Inventor.Application)image.Application));
        Observe("typed_image_alpha", () =>
        {
            var alpha = image.ImageAlpha;
            return alpha == null ? new JObject { ["state"] = "absent", ["pixel_copy_complete"] = false }
                : BmpPicturePixels37(alpha, 6, (Inventor.Application)image.Application);
        });
        return result;
    }
}
