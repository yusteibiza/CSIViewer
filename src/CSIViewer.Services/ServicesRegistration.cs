using Microsoft.Extensions.DependencyInjection;
using CSIViewer.Infrastructure;

namespace CSIViewer.Services;

public static class ServicesRegistration
{
    public static IServiceCollection AddCSIViewerClient(this IServiceCollection services)
    {
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        });
        services.AddSingleton<AppState>();
        services.AddSingleton<IdentityService>();
        services.AddSingleton<CSIViewer.Networking.ServerApiClient>();
        services.AddSingleton<RemoteClient>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<ClientBootstrap>();
        return services;
    }
}

public sealed class ClientBootstrap
{
    private readonly DatabaseInitializer _database;
    private readonly RemoteClient _client;

    public ClientBootstrap(DatabaseInitializer database, RemoteClient client)
    {
        _database = database;
        _client = client;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }
}
