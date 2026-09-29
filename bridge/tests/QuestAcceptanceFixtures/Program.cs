using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object app);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var mode = args.FirstOrDefault(a => a is "--prepare-quest" or "--inspect-quest" or "--restore-quest");
            var index = Array.IndexOf(args, mode);
            var milestone = mode == null || index + 1 >= args.Length ? null : args[index + 1].ToLowerInvariant();
            if (mode == null || milestone is not ("m1" or "m2" or "m3" or "m5"))
            {
                Console.Error.WriteLine("Usage: QuestAcceptanceFixtures (--prepare-quest|--inspect-quest|--restore-quest) <m1|m2|m3|m5>");
                return 64;
            }
            var clsid = Type.GetTypeFromProgID("Inventor.Application")!.GUID;
            GetActiveObject(ref clsid, IntPtr.Zero, out var active);
            var app = (global::Inventor.Application)active;
            return mode switch
            {
                "--prepare-quest" => Fixtures.Prepare(app, milestone),
                "--inspect-quest" => Fixtures.Inspect(app, milestone),
                _ => Fixtures.Restore(app, milestone),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }
}
