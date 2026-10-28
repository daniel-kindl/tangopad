using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Editor.Infrastructure.Updates;

// Startup and apply share this gate. A new editor must not open while the native helper can terminate old processes.
internal sealed class UpdateHandoff : IDisposable
{
    private const int NoSuchUnixProcess = 3;
    private readonly string marker;

    private UpdateHandoff(string marker)
    {
        this.marker = marker;
    }

    internal static UpdateHandoff Begin(string root, string targetVersion)
    {
        using Mutex gate = CreateGate(root);
        Enter(gate);
        try
        {
            using Process current = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(current.ProcessName);
            try
            {
                if (processes.Any(process => process.Id != Environment.ProcessId))
                {
                    throw new IOException("Close other Tangopad windows before applying the update.");
                }
            }
            finally { foreach (Process process in processes) { process.Dispose(); } }
            string marker = Path.Combine(root, ".tangopad-update-pending");
            using (FileStream output = new(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(output))
            {
                writer.WriteLine(current.Id.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(targetVersion);
            }
            return new UpdateHandoff(marker);
        }
        finally { gate.ReleaseMutex(); }
    }

    internal static string? WaitForCompletion(string root, TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            using Mutex gate = CreateGate(root);
            Enter(gate);
            bool pending;
            try
            {
                string marker = Path.Combine(root, ".tangopad-update-pending");
                if (!File.Exists(marker)) { return null; }
                string[] owner = File.ReadAllLines(marker);
                if (owner.Length is not (2 or 3) || !int.TryParse(owner[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
                    || !long.TryParse(owner[1], NumberStyles.None, CultureInfo.InvariantCulture, out long started))
                {
                    throw new IOException("The update recovery marker is invalid. Preserve application data and remove .tangopad-update-pending after all update processes stop.");
                }
                pending = IsOwnerRunning(pid, started) || IsHelperRunning();
                if (!pending) { File.Delete(marker); return owner.Length == 3 ? owner[2] : null; }
            }
            finally { gate.ReleaseMutex(); }
            if (elapsed.Elapsed >= timeout) { throw new IOException("An update is still in progress. Start Tangopad again after the update finishes."); }
            Thread.Sleep(50);
        }
    }

    private static bool IsOwnerRunning(int pid, long started)
    {
        return IsOwnerRunning(() =>
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started;
        });
    }

    internal static bool IsOwnerRunning(Func<bool> readOwnerIdentity)
    {
        try
        {
            return readOwnerIdentity();
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception error) when (!OperatingSystem.IsWindows() && error.NativeErrorCode == NoSuchUnixProcess) { return false; }
    }

    private static bool IsHelperRunning()
    {
        foreach (string name in new[] { "Update", "UpdateNix", "UpdateMac" })
        {
            Process[] processes = Process.GetProcessesByName(name);
            bool found = processes.Length != 0;
            foreach (Process process in processes) { process.Dispose(); }
            if (found) { return true; }
        }
        return false;
    }

    private static Mutex CreateGate(string root)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))));
        return new Mutex(false, "Tangopad.Update." + identity);
    }

    private static void Enter(Mutex gate)
    {
        try
        {
            if (!gate.WaitOne(TimeSpan.FromSeconds(5))) { throw new IOException("Another update operation is active. Retry after it finishes."); }
        }
        catch (AbandonedMutexException) { }
    }

    public void Dispose()
    {
        File.Delete(marker);
    }
}
