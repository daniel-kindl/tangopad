using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Editor.Desktop.SmokeTests;

[SupportedOSPlatform("windows")]
internal static partial class Win32WindowProbe
{
    private const uint CloseMessage = 0x0010;
    private const DllImportSearchPath WindowLibrarySearch = DllImportSearchPath.System32;

    internal static unsafe bool TrySendClose(int processId)
    {
        WindowList windows = new();
        GCHandle handle = GCHandle.Alloc(windows);
        try
        {
            _ = EnumWindows(&EnumerateVisibleWindows, GCHandle.ToIntPtr(handle));
            int target = processId;
            List<IntPtr> matches = [];
            foreach (WindowRecord record in windows.Records)
            {
                if (record.ProcessId == (uint)target && IsWindowVisible(record.Handle))
                {
                    matches.Add(record.Handle);
                }
            }

            if (matches.Count == 0)
            {
                return false;
            }

            foreach (IntPtr window in matches)
            {
                _ = PostMessageW(window, CloseMessage, IntPtr.Zero, IntPtr.Zero);
            }

            return true;
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumerateVisibleWindows(IntPtr hwnd, IntPtr state)
    {
        object? target = GCHandle.FromIntPtr(state).Target;
        if (target is not WindowList windows)
        {
            return 1;
        }

        _ = GetWindowThreadProcessId(hwnd, out uint processId);
        windows.Records.Add(new WindowRecord(hwnd, processId));
        return 1;
    }

    private sealed class WindowList
    {
        internal List<WindowRecord> Records { get; } = [];
    }

    private readonly record struct WindowRecord(IntPtr Handle, uint ProcessId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(WindowLibrarySearch)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> callback, IntPtr state);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(WindowLibrarySearch)]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(WindowLibrarySearch)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    // user32.dll exports PostMessageW. PostMessage is a macro, not an entry point.
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(WindowLibrarySearch)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
