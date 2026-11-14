namespace CSIViewer.Application;

/// <summary>Persisted verifier, never a Windows password or a recoverable remote-access password.</summary>
public sealed record HostPasswordVerifier(string Salt, string Hash);
