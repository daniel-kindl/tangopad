using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Editor.Desktop.SmokeTests;

[SupportedOSPlatform("linux")]
internal static partial class X11WindowProbe
{
    private const int ClientMessage = 33;
    private const int EventBytes = 256;
    private const DllImportSearchPath X11LibrarySearch = DllImportSearchPath.SafeDirectories;

    private static bool _errorHandlerInstalled;

    internal static async Task<(X11DisplaySession? Session, string? Reason)> OpenAsync(CancellationToken cancellationToken)
    {
        string? ambient = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrWhiteSpace(ambient) && TryOpen(ambient, out IntPtr ambientDisplay))
        {
            return (new X11DisplaySession(ambientDisplay, ambient, "X11 " + ambient, null), null);
        }

        string? xvfb = FindOnPath("Xvfb");
        if (xvfb is null)
        {
            return (null, "No X11 display is open, and the Xvfb command is not installed.");
        }

        string lastError = "Xvfb did not open a display.";
        for (int number = 99; number <= 109; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists($"/tmp/.X11-unix/X{number}") || File.Exists($"/tmp/.X{number}-lock"))
            {
                continue;
            }

            string displayName = ":" + number.ToString(CultureInfo.InvariantCulture);
            ProcessStartInfo startInfo = new(xvfb)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(displayName);
            startInfo.ArgumentList.Add("-screen");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("1280x800x24");
            startInfo.ArgumentList.Add("-nolisten");
            startInfo.ArgumentList.Add("tcp");
            startInfo.ArgumentList.Add("-ac");

            Process? process = new() { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                {
                    continue;
                }

                Task<string>? standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                Task<string>? standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
                long deadline = Environment.TickCount64 + 5000;
                while (Environment.TickCount64 < deadline && !process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryOpen(displayName, out IntPtr display))
                    {
                        X11DisplaySession session = new(display, displayName, "Xvfb " + displayName, process, standardOutput, standardError);
                        process = null;
                        return (session, null);
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The virtual server exited between the check and Kill.
                    }
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                string error = (await standardError.ConfigureAwait(false)).Trim();
                _ = await standardOutput.ConfigureAwait(false);
                if (error.Length > 0)
                {
                    lastError = error.Length <= 500 ? error : error[^500..];
                }
            }
            finally
            {
                process?.Dispose();
            }
        }

        return (null, "Xvfb did not open a display. " + lastError);
    }

    internal static bool TrySendDelete(IntPtr display, int processId)
    {
        ulong pidAtom = Intern(display, "_NET_WM_PID");
        ulong protocolsAtom = Intern(display, "WM_PROTOCOLS");
        ulong deleteAtom = Intern(display, "WM_DELETE_WINDOW");
        if (pidAtom == 0 || protocolsAtom == 0 || deleteAtom == 0)
        {
            return false;
        }

        if (!TryFindWindow(display, XDefaultRootWindow(display), pidAtom, processId, 0, [], out ulong window))
        {
            return false;
        }

        XClientMessageEvent message = default;
        message.Type = ClientMessage;
        message.Display = display;
        message.Window = window;
        message.MessageType = protocolsAtom;
        message.Format = 32;
        message.Data0 = (nint)deleteAtom;
        int sent = XSendEvent(display, window, 0, 0, ref message);
        _ = XFlush(display);
        return sent != 0;
    }

    private static bool TryOpen(string displayName, out IntPtr display)
    {
        display = XOpenDisplay(displayName);
        if (display == IntPtr.Zero)
        {
            return false;
        }

        EnsureErrorHandler();
        return true;
    }

    private static unsafe void EnsureErrorHandler()
    {
        if (_errorHandlerInstalled)
        {
            return;
        }

        _ = XSetErrorHandler(&HandleXError);
        _errorHandlerInstalled = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HandleXError(IntPtr display, IntPtr errorEvent)
    {
        return 0;
    }

    private static bool TryFindWindow(
        IntPtr display,
        ulong window,
        ulong pidAtom,
        int processId,
        int depth,
        HashSet<ulong> seen,
        out ulong found)
    {
        found = 0;
        if (depth > 12 || !seen.Add(window))
        {
            return false;
        }

        if (TryReadPid(display, window, pidAtom, out long pid) && pid == processId)
        {
            found = window;
            return true;
        }

        int query = XQueryTree(display, window, out _, out _, out IntPtr children, out uint count);
        if (children == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (query == 0)
            {
                return false;
            }

            uint limit = Math.Min(count, 4096);
            for (uint index = 0; index < limit; index++)
            {
                ulong child = (ulong)Marshal.ReadIntPtr(children, (int)index * IntPtr.Size);
                if (TryFindWindow(display, child, pidAtom, processId, depth + 1, seen, out found))
                {
                    return true;
                }
            }
        }
        finally
        {
            _ = XFree(children);
        }

        return false;
    }

    private static bool TryReadPid(IntPtr display, ulong window, ulong pidAtom, out long pid)
    {
        pid = 0;
        int status = XGetWindowProperty(
            display,
            window,
            pidAtom,
            0,
            1,
            0,
            0,
            out _,
            out int format,
            out nuint count,
            out _,
            out IntPtr value);
        if (value == IntPtr.Zero)
        {
            return false;
        }

        if (status != 0)
        {
            _ = XFree(value);
            return false;
        }

        try
        {
            if (format != 32 || count < 1)
            {
                return false;
            }

            pid = Marshal.ReadIntPtr(value).ToInt64();
            return true;
        }
        finally
        {
            _ = XFree(value);
        }
    }

    private static ulong Intern(IntPtr display, string name)
    {
        return XInternAtom(display, name, 0);
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>LP64 layout of <c>XClientMessageEvent</c>. Trailing bytes stand in for the rest of <c>XEvent</c>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = EventBytes)]
    private struct XClientMessageEvent
    {
        [FieldOffset(0)]
        public int Type;

        [FieldOffset(24)]
        public IntPtr Display;

        [FieldOffset(32)]
        public ulong Window;

        [FieldOffset(40)]
        public ulong MessageType;

        [FieldOffset(48)]
        public int Format;

        [FieldOffset(56)]
        public nint Data0;
    }

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial IntPtr XOpenDisplay(string displayName);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static unsafe partial IntPtr XSetErrorHandler(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int> handler);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial ulong XDefaultRootWindow(IntPtr display);

    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial ulong XInternAtom(IntPtr display, string name, int onlyIfExists);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial int XGetWindowProperty(
        IntPtr display,
        ulong window,
        ulong property,
        nint longOffset,
        nint longLength,
        int delete,
        ulong requiredType,
        out ulong actualType,
        out int actualFormat,
        out nuint itemCount,
        out nuint bytesAfter,
        out IntPtr value);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial int XQueryTree(
        IntPtr display,
        ulong window,
        out ulong root,
        out ulong parent,
        out IntPtr children,
        out uint childCount);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial int XSendEvent(IntPtr display, ulong window, int propagate, nint eventMask, ref XClientMessageEvent eventData);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial int XFlush(IntPtr display);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(X11LibrarySearch)]
    private static partial int XFree(IntPtr data);

    internal static void CloseDisplay(IntPtr display)
    {
        if (display != IntPtr.Zero)
        {
            _ = XCloseDisplay(display);
        }
    }
}

[SupportedOSPlatform("linux")]
internal sealed class X11DisplaySession : IAsyncDisposable
{
    private readonly Process? _virtualServer;
    private readonly Task<string>? _virtualOutput;
    private readonly Task<string>? _virtualError;

    internal X11DisplaySession(IntPtr display, string name, string label, Process? virtualServer)
        : this(display, name, label, virtualServer, null, null)
    {
    }

    internal X11DisplaySession(
        IntPtr display,
        string name,
        string label,
        Process? virtualServer,
        Task<string>? virtualOutput,
        Task<string>? virtualError)
    {
        Handle = display;
        Name = name;
        Label = label;
        _virtualServer = virtualServer;
        _virtualOutput = virtualOutput;
        _virtualError = virtualError;
    }

    internal string Name { get; }

    internal string Label { get; }

    internal IntPtr Handle { get; private set; }

    public async ValueTask DisposeAsync()
    {
        IntPtr display = Handle;
        Handle = IntPtr.Zero;
        if (display != IntPtr.Zero)
        {
            X11WindowProbe.CloseDisplay(display);
        }

        if (_virtualServer is null)
        {
            return;
        }

        if (!_virtualServer.HasExited)
        {
            try
            {
                _virtualServer.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The virtual server exited between the check and Kill.
            }
        }

        await _virtualServer.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        if (_virtualOutput is not null && _virtualError is not null)
        {
            _ = await Task.WhenAll(_virtualOutput, _virtualError).ConfigureAwait(false);
        }

        _virtualServer.Dispose();
    }
}
