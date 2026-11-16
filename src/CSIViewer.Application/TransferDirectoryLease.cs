using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CSIViewer.Application;

/// <summary>
/// Windows handle-relative transfer I/O. Descendants are opened one component
/// at a time without following reparse points. No pathname is reopened for a
/// write, rename or delete after validation. Handles pin directory identities.
/// </summary>
internal sealed class TransferDirectoryLease : IDisposable
{
    private readonly List<SafeFileHandle> _directories = [];
    private readonly List<SafeFileHandle> _destinationHandles = [];
    private (uint Volume, uint High, uint Low)? _expectedDestination;
    private readonly SafeFileHandle _root;
    private readonly SafeFileHandle _temporary;
    private SafeFileHandle? _destination;
    private string _destinationName = "";
    private string _destinationDirectory = "";
    private readonly string _rootPath;
    private bool _disposed;

    public TransferDirectoryLease(string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Secure transfer I/O requires Windows.");
        _rootPath = Path.GetFullPath(root);
        _root = CreateFile(_rootPath, 0x001000A0, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (_root.IsInvalid) throw Error(Marshal.GetLastWin32Error());
        _directories.Add(_root);
        try { _temporary = Directory(_root, ".rdpart"); }
        catch { Dispose(); throw; }
    }

    public void SetDestination(string fullPath)
    {
        ReleaseDestinationHandles();
        var path = PathValidator.EnsureInsideRoot(_rootPath, fullPath);
        var relative = Path.GetRelativePath(_rootPath, path);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length == 0 || parts[0].Equals(".rdpart", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reserved transfer path.");
        var parent = _root;
        foreach (var part in parts[..^1]) parent = Directory(parent, part, destination: true);
        _destination = parent;
        _destinationName = parts[^1];
        _destinationDirectory = Path.GetDirectoryName(path)!;
    }

    public void SealDestination()
    {
        var info = Information(_destination!);
        _expectedDestination = (info.Volume, info.IndexHigh, info.IndexLow);
        ReleaseDestinationHandles();
    }

    public void PrepareFinalization(string path)
    {
        SetDestination(path);
        var info = Information(_destination!);
        if (_expectedDestination != (info.Volume, info.IndexHigh, info.IndexLow))
            throw new IOException("Destination directory identity changed during transfer.");
    }

    private void ReleaseDestinationHandles()
    {
        for (var index = _destinationHandles.Count - 1; index >= 0; index--) _destinationHandles[index].Dispose();
        _destinationHandles.Clear();
        _destination = null;
    }

    public bool DestinationExists => Exists(_destination!, _destinationName);
    public string UniqueDestination()
    {
        if (!DestinationExists) return Path.Combine(_destinationDirectory, _destinationName);
        var stem = Path.GetFileNameWithoutExtension(_destinationName);
        var extension = Path.GetExtension(_destinationName);
        for (var index = 1; index < 10_000; index++)
        {
            var name = $"{stem} ({index}){extension}";
            if (!Exists(_destination!, name)) return Path.Combine(_destinationDirectory, name);
        }
        throw new IOException("No unused transfer filename.");
    }

    public FileStream OpenTemporary(string name, bool resume)
    {
        var handle = Open(_temporary, name, 0xC0110080, 0, resume ? 1u : 2u, 0x00200040);
        try
        {
            var info = Information(handle);
            if ((info.Attributes & 0x400) != 0 || info.Links != 1)
                throw new IOException("A transfer partial must be a single-link regular file.");
            return new FileStream(handle, FileAccess.ReadWrite, 1024 * 1024, isAsync: true);
        }
        catch { handle.Dispose(); throw; }
    }

    public long TemporaryLength(string name)
    {
        using var handle = Open(_temporary, name, 0x80, 7, 1, 0x00200040);
        var info = Information(handle);
        if ((info.Attributes & 0x400) != 0 || info.Links != 1) throw new IOException("Linked partial rejected.");
        return ((long)info.SizeHigh << 32) | info.SizeLow;
    }

    public async Task<string?> ReadMetadataAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var handle = Open(_temporary, name, 0x80100080, 5, 1, 0x00200040);
            await using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: true);
            if ((Information(handle).Attributes & 0x400) != 0 || stream.Length > 64 * 1024) return null;
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (IOException) { return null; }
    }

    public async Task WriteMetadataAsync(string name, string json, CancellationToken cancellationToken)
    {
        var temporary = name + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using var stream = OpenTemporary(temporary, resume: false);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Rename(stream.SafeFileHandle, _temporary, name, overwrite: true);
        }
        finally { DeleteTemporary(temporary); }
    }

    public string MoveToDestination(SafeFileHandle source, bool overwrite)
    {
        var name = _destinationName;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Rename(source, _destination!, name, overwrite);
                return Path.Combine(_destinationDirectory, name);
            }
            catch (IOException) when (!overwrite && attempt < 9_999 && Exists(_destination!, name))
            {
                name = Path.GetFileName(UniqueDestination());
            }
        }
    }

    public void DeleteTemporary(string name)
    {
        try
        {
            using var file = Open(_temporary, name, 0x00010080, 7, 1, 0x00200040);
            var buffer = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buffer, 1);
                if (!SetInformation(file, 4, buffer, 4)) throw Error(Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (IOException) { /* absent/busy is best-effort cleanup, never follow a substituted name */ }
        catch (UnauthorizedAccessException) { }
    }

    private SafeFileHandle Directory(SafeFileHandle parent, string name, bool destination = false)
    {
        // No share-delete: pin identity during each handle-relative operation.
        // Relative native I/O never reparses a DOS path to find this parent.
        var handle = Open(parent, name, 0x001000A0, 3, 3, 0x00200021);
        if ((Information(handle).Attributes & 0x400) != 0)
        {
            handle.Dispose();
            throw new IOException("Redirected transfer directory rejected.");
        }
        if (destination) _destinationHandles.Add(handle);
        else _directories.Add(handle);
        return handle;
    }

    private static bool Exists(SafeFileHandle directory, string name)
    {
        try { using var handle = Open(directory, name, 0x80, 7, 1, 0x00200000); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static SafeFileHandle Open(SafeFileHandle parent, string name, uint access, uint share, uint disposition, uint options)
    {
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['\\', '/', ':', '\0']) >= 0)
            throw new IOException("Invalid relative transfer component.");
        var text = Marshal.StringToHGlobalUni(name);
        var unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        var added = false;
        try
        {
            parent.DangerousAddRef(ref added);
            var unicode = new UnicodeString { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2 + 2)), Buffer = text };
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), Root = parent.DangerousGetHandle(), Name = unicodePointer, Attributes = 0x1040 };
            var status = NtCreateFile(out var file, access, ref attributes, out _, IntPtr.Zero, 0x80, share,
                disposition, options, IntPtr.Zero, 0);
            if (status < 0)
            {
                file?.Dispose();
                throw Error((int)RtlNtStatusToDosError(status));
            }
            return file;
        }
        finally
        {
            if (added) parent.DangerousRelease();
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(text);
        }
    }

    private static FileInformation Information(SafeFileHandle file)
    {
        if (!GetFileInformation(file, out var information)) throw Error(Marshal.GetLastWin32Error());
        return information;
    }

    private static void Rename(SafeFileHandle file, SafeFileHandle directory, string name, bool overwrite)
    {
        var bytes = Encoding.Unicode.GetBytes(name);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var header = lengthOffset + 4;
        var buffer = Marshal.AllocHGlobal(header + bytes.Length);
        var added = false;
        try
        {
            directory.DangerousAddRef(ref added);
            for (var index = 0; index < header; index++) Marshal.WriteByte(buffer, index, 0);
            Marshal.WriteByte(buffer, overwrite ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(buffer, rootOffset, directory.DangerousGetHandle());
            Marshal.WriteInt32(buffer, lengthOffset, bytes.Length);
            Marshal.Copy(bytes, 0, buffer + header, bytes.Length);
            // The Win32 SetFileInformationByHandle wrapper does not accept a
            // relative RootDirectory for rename. Use the native relative form.
            var status = NtSetInformationFile(file, out _, buffer, (uint)(header + bytes.Length), 10);
            if (status < 0) throw Error((int)RtlNtStatusToDosError(status));
        }
        finally
        {
            if (added) directory.DangerousRelease();
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Exception Error(int code) => code switch
    {
        2 => new FileNotFoundException("Transfer entry not found."),
        3 => new DirectoryNotFoundException("Transfer directory not found."),
        5 => new UnauthorizedAccessException("Access to the transfer entry was denied."),
        _ => new IOException($"Transfer native error {code}: {new Win32Exception(code).Message}", unchecked((int)(0x80070000u | (uint)code)))
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseDestinationHandles();
        for (var index = _directories.Count - 1; index >= 0; index--) _directories[index].Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes { public int Length; public IntPtr Root, Name; public uint Attributes; public IntPtr Security, Quality; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public IntPtr Status, Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle file, uint access, ref ObjectAttributes attributes,
        out IoStatus status, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle file, out IoStatus status, IntPtr buffer, uint length, int informationClass);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformation(SafeFileHandle file, out FileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformation(SafeFileHandle file, int informationClass, IntPtr buffer, uint length);
}
