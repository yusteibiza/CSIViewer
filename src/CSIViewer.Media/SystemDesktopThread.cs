using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CSIViewer.Media;

/// <summary>
/// A SYSTEM-only, windowless MTA thread in the interactive Windows session.
/// Uses the access already granted to SYSTEM. Never changes a desktop ACL,
/// switches the visible desktop, impersonates another process or changes UAC.
/// </summary>
internal sealed class SystemDesktopThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = new(128);
    private readonly Thread _thread;
    private IntPtr _desktop;
    private string? _name;
    private bool _changed;
    internal string DesktopName => _name ?? "";

    public SystemDesktopThread()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem || System.Diagnostics.Process.GetCurrentProcess().SessionId == 0)
            throw new UnauthorizedAccessException("Login desktop access requires SYSTEM in the interactive session.");
        _thread = new Thread(Run) { IsBackground = true, Name = "CSIViewer authenticated desktop worker" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public T Invoke<T>(Func<bool, T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.TryAdd(() =>
            {
                try
                {
                    Attach();
                    completion.TrySetResult(operation(_changed));
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            })) throw new InvalidOperationException("Desktop worker queue unavailable.");
        return completion.Task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        var original = GetThreadDesktop(GetCurrentThreadId());
        try { foreach (var operation in _work.GetConsumingEnumerable()) operation(); }
        finally
        {
            if (SetThreadDesktop(original) && _desktop != IntPtr.Zero) CloseDesktop(_desktop);
            _work.Dispose();
        }
    }

    private void Attach()
    {
        var next = OpenInputDesktop(0, false, 0x0001 | 0x0080); // READOBJECTS | WRITEOBJECTS
        if (next == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var name = new char[256];
        if (!GetUserObjectInformation(next, 2, name, name.Length * 2, out _) || !SetThreadDesktop(next))
        {
            var error = Marshal.GetLastWin32Error();
            CloseDesktop(next);
            throw new Win32Exception(error);
        }
        var previous = _desktop;
        _desktop = next;
        var end = Array.IndexOf(name, '\0');
        var currentName = new string(name, 0, end < 0 ? name.Length : end);
        _changed = !string.Equals(_name, currentName, StringComparison.Ordinal);
        _name = currentName;
        if (previous != IntPtr.Zero) CloseDesktop(previous);
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, [Out] char[] buffer, int length, out int needed);
}
