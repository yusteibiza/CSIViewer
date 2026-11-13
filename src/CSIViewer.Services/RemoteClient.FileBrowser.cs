using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Protocol;
using Microsoft.Extensions.Logging;

namespace CSIViewer.Services;

public sealed record FileBrowserTransferResult(string SessionId, string? Error);

public sealed partial class RemoteClient
{
    public event EventHandler<FileBrowserTransferResult>? FileBrowserTransferFinished;
    private static void ValidateBrowserWritePath(string root, string destination)
    {
        FileBrowser.ValidatePath(root);
        foreach (var candidate in new[] { destination, Path.Combine(root, ".rdpart") })
        {
            for (var current = candidate; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if (File.Exists(current) || Directory.Exists(current)) FileBrowser.ValidatePath(current);
        }
    }
    private RemoteSession RequireFileSession(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var id) || !_sessions.TryGetValue(id, out var session) ||
            session.State != ConnectionState.Connected || !SessionPermissionPolicy.CanTransferFiles(session.Permissions))
            throw new InvalidOperationException("La sesión ya no está disponible o no tiene permiso de archivos.");
        return session;
    }

    private static string RegisterFileDestination(RemoteSession session, string path)
    {
        var full = FileBrowser.ValidatePath(path);
        if (!Directory.Exists(full)) throw new IOException("La carpeta de destino no existe.");
        foreach (var entry in session.FileDestinations)
            if (string.Equals(entry.Value, full, StringComparison.OrdinalIgnoreCase)) return entry.Key;
        if (session.FileDestinations.Count >= 256)
            throw new IOException("Se alcanzó el límite de carpetas de esta sesión. Vuelve a conectar.");
        var token = Guid.NewGuid().ToString("N");
        session.FileDestinations[token] = full;
        return token;
    }

    private string ResolveFileDestination(RemoteSession? session, string? token)
    {
        if (token is null) return _state.Settings.DownloadFolder; // Compatible con 1.0.0.
        if (session is null || session.State != ConnectionState.Connected ||
            !session.FileDestinations.TryGetValue(token, out var path))
            throw new IOException("El destino no está autorizado en esta sesión.");
        return FileBrowser.ValidatePath(path);
    }

    public async Task<FileBrowseResponse> BrowseRemoteFilesAsync(string sessionId, string path, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var session = RequireFileSession(sessionId);
        if (session.BrowseRequests.Count >= 4) throw new IOException("Hay otra navegación pendiente.");
        var id = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<FileBrowseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.BrowseRequests[id] = pending;
        try
        {
            await SendFileControlAsync(session, "file.browse", new FileBrowseRequest(id, path, offset), cancellationToken).ConfigureAwait(false);
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new IOException("El equipo remoto no respondió. El administrador de archivos requiere CSIViewer 1.0.1 en ambos equipos.");
        }
        finally { session.BrowseRequests.TryRemove(id, out _); }
    }

    public Task<Result<int>> SendFilesToRemoteFolderAsync(string sessionId, IReadOnlyList<string> paths,
        string destinationToken, CancellationToken cancellationToken = default)
    {
        var session = RequireFileSession(sessionId);
        if (!Guid.TryParse(destinationToken, out _))
            return Task.FromResult(Result<int>.Fail("invalid_destination", "Selecciona una carpeta de destino remota válida."));
        var selected = paths.ToArray();
        return Task.Run(() => SendPathsToSessionAsync(selected, session, destinationToken, cancellationToken), cancellationToken);
    }

    public async Task RequestRemoteFilesAsync(string sessionId, string[] paths, string localDirectory,
        CancellationToken cancellationToken = default)
    {
        var session = RequireFileSession(sessionId);
        if (!SessionPermissionPolicy.CanReceiveFiles(session.IsController, session.Permissions))
            throw new InvalidOperationException("No hay permiso para recibir archivos.");
        if (paths.Length is < 1 or > 100) throw new IOException("Selecciona entre 1 y 100 elementos.");
        if (session.RequestedPulls.Count >= 4) throw new IOException("Hay varias recepciones pendientes.");
        var destination = RegisterFileDestination(session, localDirectory);
        var id = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<FilePullResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.PullRequests[id] = pending;
        session.RequestedPulls[id] = 0;
        try
        {
            await SendFileControlAsync(session, "file.pull", new FilePullRequest(id, paths, destination), cancellationToken).ConfigureAwait(false);
            var response = await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            if (response.Error is not null) throw new IOException(response.Error);
        }
        catch { session.RequestedPulls.TryRemove(id, out _); throw; }
        finally { session.PullRequests.TryRemove(id, out _); }
    }

    // Always use the session that decrypted this message, never the primary alias.
    // Disk enumeration and outgoing uploads must not block file.accept/chunks on the receive loop.
    private bool TryHandleFileBrowserControl(ControlEnvelope envelope, RemoteSession? session)
    {
        if (session is null) return false;
        if (envelope.Type == "file.pull.finished")
        {
            var response = ControlJson.Payload<FilePullResponse>(envelope);
            if (response is not null && session.RequestedPulls.TryRemove(response.RequestId, out _))
                FileBrowserTransferFinished?.Invoke(this, new(session.SessionId, response.Error));
            return true;
        }
        if (envelope.Type == "file.directory.result")
        {
            var response = ControlJson.Payload<FileDirectoryResponse>(envelope);
            if (response is not null && session.DirectoryRequests.TryGetValue(response.RequestId, out var pending))
                pending.TrySetResult(response);
            return true;
        }
        if (envelope.Type == "file.browse.result")
        {
            var response = ControlJson.Payload<FileBrowseResponse>(envelope);
            if (response is not null && session.BrowseRequests.TryGetValue(response.RequestId, out var pending))
                pending.TrySetResult(response);
            return true;
        }
        if (envelope.Type == "file.pull.result")
        {
            var response = ControlJson.Payload<FilePullResponse>(envelope);
            if (response is not null && session.PullRequests.TryGetValue(response.RequestId, out var pending))
                pending.TrySetResult(response);
            return true;
        }
        if (envelope.Type is not ("file.browse" or "file.pull")) return false;
        if (!SessionPermissionPolicy.CanTransferFiles(session.Permissions) || session.State != ConnectionState.Connected) return true;
        var pull = envelope.Type == "file.pull";
        if (pull ? Interlocked.CompareExchange(ref session.FilePullBusy, 1, 0) != 0 :
                   Interlocked.CompareExchange(ref session.BrowserBusy, 1, 0) != 0)
        {
            _ = ReplyFileBrowserBusyAsync(envelope, session);
            return true;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (envelope.Type == "file.browse") await AnswerFileBrowseAsync(envelope, session).ConfigureAwait(false);
                else await AnswerFilePullAsync(envelope, session).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "File browser operation failed"); }
            finally
            {
                if (pull) Interlocked.Exchange(ref session.FilePullBusy, 0);
                else Interlocked.Exchange(ref session.BrowserBusy, 0);
            }
        });
        return true;
    }

    private async Task ReplyFileBrowserBusyAsync(ControlEnvelope envelope, RemoteSession session)
    {
        try
        {
            if (envelope.Type == "file.pull")
            {
                var request = ControlJson.Payload<FilePullRequest>(envelope);
                if (request is not null && Guid.TryParse(request.RequestId, out _))
                    await SendFileControlAsync(session, "file.pull.result", new FilePullResponse(request.RequestId, "Hay otra recepción solicitada en curso.")).ConfigureAwait(false);
            }
            else
            {
                var request = ControlJson.Payload<FileBrowseRequest>(envelope);
                if (request is not null && Guid.TryParse(request.RequestId, out _))
                    await SendFileControlAsync(session, "file.browse.result", new FileBrowseResponse(request.RequestId, "", null, [], null, Error: "La navegación está ocupada. Vuelve a intentarlo.")).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not report file browser busy state"); }
    }

    private async Task AnswerFileBrowseAsync(ControlEnvelope envelope, RemoteSession session)
    {
        var request = ControlJson.Payload<FileBrowseRequest>(envelope);
        if (request is null || !Guid.TryParse(request.RequestId, out _) || request.Path is null || request.Path.Length > 4096) return;
        FileBrowseResponse response;
        try
        {
            RequireFileSession(session.SessionId);
            response = FileBrowser.Browse(request.Path, request.Offset) with { RequestId = request.RequestId };
            // Upload-only permission reveals destination folders, not filenames
            // or sizes of files the peer has no permission to receive.
            if (!SessionPermissionPolicy.CanSendFiles(session.IsController, session.Permissions))
                response = response with { Entries = response.Entries.Where(x => x.IsDirectory).ToArray() };
            if (response.Path.Length > 0 && SessionPermissionPolicy.CanReceiveFiles(session.IsController, session.Permissions))
                response = response with { DestinationToken = RegisterFileDestination(session, response.Path) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            response = new(request.RequestId, request.Path, null, [], null, Error: "No se puede abrir la carpeta: " + ex.Message);
        }
        if (session.State == ConnectionState.Connected)
            await SendFileControlAsync(session, "file.browse.result", response).ConfigureAwait(false);
    }

    private async Task AnswerFilePullAsync(ControlEnvelope envelope, RemoteSession session)
    {
        var request = ControlJson.Payload<FilePullRequest>(envelope);
        if (request is null || !Guid.TryParse(request.RequestId, out _) || request.Paths is null ||
            request.Paths.Length is < 1 or > 100 || !Guid.TryParse(request.DestinationToken, out _)) return;
        string[] paths;
        try
        {
            RequireFileSession(session.SessionId);
            if (!SessionPermissionPolicy.CanSendFiles(session.IsController, session.Permissions))
                throw new InvalidOperationException("El anfitrión no ha concedido permiso para recibir sus archivos.");
            paths = request.Paths.Select(FileBrowser.ValidatePath).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            await SendFileControlAsync(session, "file.pull.result", new FilePullResponse(request.RequestId, ex.Message)).ConfigureAwait(false);
            return;
        }
        await SendFileControlAsync(session, "file.pull.result", new FilePullResponse(request.RequestId)).ConfigureAwait(false);
        // Existing transfer pipeline retains permission checks, receiver consent,
        // conflict policy, encryption, SHA-256 verification, pause and cancellation.
        string? error;
        try
        {
            var result = await SendPathsToSessionAsync(paths, session, request.DestinationToken, _lifetimeCts.Token).ConfigureAwait(false);
            error = result.IsSuccess ? null : result.Error?.Message ?? "La recepción no pudo completarse.";
        }
        catch (Exception ex) { error = ex is UnauthorizedAccessException ? "Windows denegó el acceso a los archivos." : "No se pudo completar la recepción."; }
        if (session.State == ConnectionState.Connected)
            await SendFileControlAsync(session, "file.pull.finished", new FilePullResponse(request.RequestId, error)).ConfigureAwait(false);
    }
}
