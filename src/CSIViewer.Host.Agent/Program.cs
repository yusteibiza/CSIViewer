using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using CSIViewer.Host.Agent;

var options = HostLauncherOptions.Parse(args);
if (!HostStartup.ShouldRunService(args))
{
    // Double-clicking the host opens the standalone host window. The
    // unattended service path remains explicit (Windows service, --service or
    // --once) and does not create a console window.
    HostStartup.LaunchHostUi(options);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(serviceOptions => serviceOptions.ServiceName = "CSIViewerHost");
}

builder.Services.AddSingleton(options);
builder.Services.AddHostedService<HostLauncherService>();

var host = builder.Build();
await host.RunAsync();
