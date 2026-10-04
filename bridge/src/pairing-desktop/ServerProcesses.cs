using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Inventor.So.Pairing.Desktop;

internal sealed record ServerProcess(int Pid, long StartedTicks, string Executable, string CommandLine)
{
    public override string ToString() => $"PID {Pid} — {ServerProcesses.DisplayPath(Executable, CommandLine)}";
}

internal static class ServerProcesses
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    internal static string DisplayPath(string executable, string commandLine)
    {
        if (!Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return executable;
        var memory = CommandLineToArgvW(commandLine, out var count);
        if (memory == IntPtr.Zero) return executable;
        try
        {
            if (count < 2) return executable;
            var first = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, IntPtr.Size));
            return first == "exec" && count > 2 ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, 2 * IntPtr.Size))! : first!;
        }
        finally { LocalFree(memory); }
    }

    internal static bool IsHttpHost(string executable, string commandLine)
    {
        var name = Path.GetFileName(executable);
        if (name.Equals("Inventor.So.Mcp.Http.exe", StringComparison.OrdinalIgnoreCase)) return true;
        if (!name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var memory = CommandLineToArgvW(commandLine, out var count);
        if (memory == IntPtr.Zero) return false;
        try
        {
            var args = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))!).ToArray();
            var index = args.Length > 1 && args[1] == "exec" ? 2 : 1;
            return args.Length > index && Path.IsPathFullyQualified(args[index]) &&
                Path.GetFileName(args[index]).Equals("Inventor.So.Mcp.Http.dll", StringComparison.OrdinalIgnoreCase) && File.Exists(args[index]);
        }
        finally { LocalFree(memory); }
    }

    public static ServerProcess[] Discover()
    {
        var found = new List<ServerProcess>();
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        object? locator = null, service = null, rows = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            service = ((dynamic)locator!).ConnectServer(".", "root\\cimv2");
            rows = ((dynamic)service).ExecQuery("SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='dotnet.exe' OR Name='Inventor.So.Mcp.Http.exe'");
            foreach (var row in (System.Collections.IEnumerable)rows)
            {
                object? owner = null;
                try
                {
                    dynamic processRow = row;
                    string? executable = Property(row, "ExecutablePath") as string;
                    string? command = Property(row, "CommandLine") as string;
                    if (executable == null || command == null || !IsHttpHost(executable, command)) continue;
                    owner = processRow.ExecMethod_("GetOwnerSid");
                    if (Convert.ToUInt32(Property(owner, "ReturnValue")) != 0 || Property(owner, "Sid") as string != sid) continue;
                    using var process = Process.GetProcessById(Convert.ToInt32(Property(row, "ProcessId")));
                    found.Add(new ServerProcess(process.Id, process.StartTime.ToUniversalTime().Ticks, executable, command));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or COMException) { }
                finally { Release(owner); Release(row); }
            }
        }
        catch (COMException ex) { throw new IOException("Impossibile leggere i server locali tramite Windows: " + ex.Message, ex); }
        finally { Release(rows); Release(service); Release(locator); }
        return found.ToArray();
    }

    public static async Task DisconnectAsync(ServerProcess selected, CancellationToken ct)
    {
        // Open a handle before rechecking identity so PID reuse cannot target a replacement process.
        using var process = Process.GetProcessById(selected.Pid);
        _ = process.Handle;
        var current = await Task.Run(Discover, ct);
        if (process.HasExited) return;
        if (!current.Contains(selected) || process.StartTime.ToUniversalTime().Ticks != selected.StartedTicks)
            throw new InvalidOperationException("Il server è cambiato. Aggiorna l'elenco prima di scollegarlo.");
        process.Kill(); // Only this HTTP backend. Never kill the process tree or Inventor.
        await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static object? Property(object row, string name)
    {
        object? properties = null, property = null;
        try
        {
            properties = ((dynamic)row).Properties_;
            property = ((dynamic)properties).Item(name);
            return ((dynamic)property).Value;
        }
        finally { Release(property); Release(properties); }
    }
}
