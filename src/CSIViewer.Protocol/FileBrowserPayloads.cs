namespace CSIViewer.Protocol;

public sealed record FileBrowserEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTimeOffset? ModifiedUtc)
{
    public string KindText => IsDirectory ? global::CSIViewer.Core.Localization.Text("Carpeta") : global::CSIViewer.Core.Localization.Text("Archivo");
    public string SizeText => IsDirectory ? "" : Size >= 1048576 ? $"{Size / 1048576.0:0.0} MB" : Size >= 1024 ? $"{Size / 1024.0:0.0} KB" : $"{Size} B";
}

public sealed record FileBrowseRequest(string RequestId, string Path, int Offset = 0);
public sealed record FileBrowseResponse(string RequestId, string Path, string? ParentPath,
    FileBrowserEntry[] Entries, string? DestinationToken, bool HasMore = false, string? Error = null);
public sealed record FilePullRequest(string RequestId, string[] Paths, string DestinationToken);
public sealed record FilePullResponse(string RequestId, string? Error = null);
public sealed record FileDirectoryResponse(string RequestId, string? Error = null);
