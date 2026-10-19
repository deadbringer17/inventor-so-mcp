#if INVENTOR2027 && SO_EXPERIMENTAL
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;
using Inventor;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers.Experimental;

/// <summary>
/// STA/in-process copy of Inventor's current bitmap, including images without managed source
/// attributes. Produces opaque top-down BGRX BMP bytes; X is unused, never alpha evidence.
/// No Inventor-owned bitmap handle is freed or selected into a DC.
/// </summary>
internal static class NativeSketchImagePixels
{
    internal const int MaxBmpBytes = 32 * 1024 * 1024;
    internal const int MaxPixels = (MaxBmpBytes - 54) / 4;

    /// <summary>Takes ownership of bytes; dispose after staging. No alias to Inventor memory.</summary>
    internal sealed class Record : IDisposable
    {
        internal Record(byte[] bytes, string sha256, int width, int height)
        { Bytes = bytes; Sha256 = sha256; PixelWidth = width; PixelHeight = height; }
        internal byte[] Bytes { get; }
        internal string Sha256 { get; }
        internal int ByteLength => Bytes.Length;
        internal int PixelWidth { get; }
        internal int PixelHeight { get; }
        public void Dispose() => Array.Clear(Bytes, 0, Bytes.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    private static extern int GetObject(IntPtr bitmap, int size, out Bitmap data);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint first, uint rows,
        [Out] byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    internal static Record Read(InventorCommandContext ctx, Application app, SketchImage image)
    {
        byte[]? pixels = null, bytes = null;
        object? picture = null;
        string phase = "ownership";
        try
        {
            X.Deadline(ctx, "checking ownership of the native image bitmap");
            // OLE_HANDLE and MainFrameHWND are unsigned 32-bit handle values on x64 too.
            if (GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)app.MainFrameHWND)), out uint owner) == 0
                || owner != (uint)System.Environment.ProcessId)
                throw Invalid("Native image pixels must be read inside their owning Inventor process.");
            phase = "transparency_guard";
            if (image.Transparent) throw Invalid("Transparent native image pixels are not yet supported.");
            X.Deadline(ctx, "reading the native image bitmap");
            phase = "image_getter";
            picture = image.Image;
            if (picture == null) throw Invalid("The native image bitmap is unavailable.");
            var native = (stdole.IPictureDisp)picture;
            phase = "picture_type";
            if (native.Type != 1) throw Invalid("The native image picture is not a bitmap.");
            phase = "bitmap_handle";
            IntPtr bitmap = new(unchecked((long)(uint)native.Handle));
            int size = Marshal.SizeOf<Bitmap>();
            if (bitmap == IntPtr.Zero || GetObject(bitmap, size, out Bitmap data) != size)
                throw Api(phase);
            phase = "bitmap_dimensions";
            if (data.Width <= 0 || data.Height <= 0 || (long)data.Width * data.Height > MaxPixels)
                throw Invalid("Native image dimensions exceed the bitmap memory budget.");
            int length = checked(data.Width * data.Height * 4);
            pixels = new byte[length];
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = data.Width,
                Height = -data.Height, Planes = 1, BitCount = 32, Compression = 0, SizeImage = (uint)length };
            phase = "create_dc";
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw Api(phase);
            bool failed = false;
            try
            {
                X.Deadline(ctx, "copying the native image bitmap rows");
                phase = "copy_rows";
                if (GetDIBits(dc, bitmap, 0, (uint)data.Height, pixels, ref info, 0) != data.Height) throw Api(phase);
                X.Deadline(ctx, "verifying the copied native image bitmap");
                if (info.Width != data.Width || info.Height != -data.Height || info.Planes != 1
                    || info.BitCount != 32 || info.Compression != 0) throw Api(phase);
            }
            catch { failed = true; throw; }
            finally { if (!DeleteDC(dc) && !failed) throw Api("delete_dc"); }
            phase = "encode_bmp";
            bytes = new byte[54 + length];
            var header = bytes.AsSpan(0, 54);
            BinaryPrimitives.WriteUInt16LittleEndian(header, 0x4d42);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(2), (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(10), 54);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(18), data.Width);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(22), -data.Height);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(26), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(28), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(34), (uint)length);
            const int copyChunk = 192 * 1024;
            for (int offset = 0; offset < length;)
            {
                X.Deadline(ctx, "encoding the native image bitmap");
                int count = Math.Min(copyChunk, length - offset);
                Buffer.BlockCopy(pixels, offset, bytes, 54 + offset, count);
                // BI_RGB32 uses BGR plus unused X. Clear X for deterministic opaque BMP output.
                for (int x = offset + 3; x < offset + count; x += 4) bytes[54 + x] = 0;
                offset += count;
            }
            X.Deadline(ctx, "hashing the native image bitmap");
            phase = "hash_bmp";
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            X.Deadline(ctx, "finishing the native image bitmap copy");
            var result = new Record(bytes, sha, data.Width, data.Height);
            bytes = null; // Ownership transfers to the disposable result.
            return result;
        }
        catch (CodedFailureException) { throw; }
        catch (Exception error) { throw new CodedFailureException(InventorErrorCodes.API_ERROR,
            "Native image pixels could not be copied.", new JObject { ["phase"] = phase, ["hresult"] = error.HResult,
                ["exception_type"] = error.GetType().Name }, inner: error); }
        finally
        {
            if (pixels != null) Array.Clear(pixels, 0, pixels.Length);
            if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
            GC.KeepAlive(picture); // Its Inventor-owned HBITMAP must outlive all GDI calls.
        }
    }

    private static CodedFailureException Invalid(string message) => new(InventorErrorCodes.INVALID_ARGUMENT, message);
    private static CodedFailureException Api(string phase) => new(InventorErrorCodes.API_ERROR,
        "The native bitmap copy or its owned graphics context could not be verified.",
        new JObject { ["phase"] = phase, ["hresult"] = unchecked((int)0x80004005) });
}
#endif
