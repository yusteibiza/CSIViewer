using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CSIViewer.Application;
using CSIViewer.Core;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace CSIViewer.Services;

public sealed class ConsentService : IConsentService
{
    public Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default)
    {
        var window = MainWindow.Instance;
        if (window is null)
        {
            return Task.FromResult(new ConsentDecision(false, SessionPermissions.None));
        }

        var completion = new TaskCompletionSource<ConsentDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() =>
            completion.TrySetResult(new ConsentDecision(false, SessionPermissions.None)));

        window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var xamlRoot = window.Content?.XamlRoot;
                if (xamlRoot is null)
                {
                    completion.TrySetResult(new ConsentDecision(false, SessionPermissions.None));
                    return;
                }

                var dialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = global::CSIViewer.Core.Localization.Text("Solicitud de conexión remota"),
                    PrimaryButtonText = "Aceptar seleccionados",
                    CloseButtonText = "Rechazar",
                    DefaultButton = ContentDialogButton.Close,
                    RequestedTheme = ThemeService.Current
                };

                var panel = new StackPanel { Spacing = 10 };
                panel.Children.Add(new TextBlock
                {
                    Text = global::CSIViewer.Core.Localization.Format("{0} ({1}) quiere conectarse a este equipo.",request.FromName,request.FromDeviceId),
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = global::CSIViewer.Core.Localization.Text("Selecciona los permisos que concedes:"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                var checks = new List<(SessionPermissions Permission, CheckBox Box)>();
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.ViewScreen, global::CSIViewer.Core.Localization.Text("Ver la pantalla"));
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.ControlInput, global::CSIViewer.Core.Localization.Text("Controlar ratón y teclado"));
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.Chat, "Enviar y recibir chat");
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.SendFiles, global::CSIViewer.Core.Localization.Text("Enviar archivos a este equipo"));
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.ReceiveFiles, global::CSIViewer.Core.Localization.Text("Recibir archivos de este equipo"));
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.Audio, global::CSIViewer.Core.Localization.Text("Escuchar el audio del sistema (sin micrófono)"));
                AddPermission(checks, panel, request.RequestedPermissions, SessionPermissions.Clipboard, global::CSIViewer.Core.Localization.Text("Compartir el portapapeles de texto en ambos sentidos"));

                if (request.ChatOnly)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = global::CSIViewer.Core.Localization.Text("Es una sesión de solo chat: no se compartirá la pantalla ni el control."),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = global::CSIViewer.Core.Localization.Text("Mientras la sesión esté activa verás un aviso permanente y podrás finalizarla en cualquier momento."),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                panel.Children.Add(new TextBlock
                {
                    Text = global::CSIViewer.Core.Localization.Format("Huella del controlador: {0}",request.Fingerprint),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["MutedTextBrush"],
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12
                });

                dialog.Content = panel;
                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    completion.TrySetResult(new ConsentDecision(false, SessionPermissions.None));
                    return;
                }

                var granted = SessionPermissions.None;
                foreach (var (permission, box) in checks)
                {
                    if (box.IsChecked == true)
                    {
                        granted |= permission;
                    }
                }

                completion.TrySetResult(new ConsentDecision(granted != SessionPermissions.None, granted));
            }
            catch
            {
                completion.TrySetResult(new ConsentDecision(false, SessionPermissions.None));
            }
            finally
            {
                registration.Dispose();
            }
        });

        return completion.Task;
    }

    public Task<FileReceiveDecision> RequestFileReceiveAsync(
        string fromName,
        string fileName,
        long size,
        bool destinationExists,
        bool willOverwrite,
        CancellationToken cancellationToken = default)
    {
        var window = MainWindow.Instance;
        if (window is null)
        {
            return Task.FromResult(FileReceiveDecision.Reject);
        }

        var completion = new TaskCompletionSource<FileReceiveDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetResult(FileReceiveDecision.Reject));
        window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var xamlRoot = window.Content?.XamlRoot;
                if (xamlRoot is null)
                {
                    completion.TrySetResult(FileReceiveDecision.Reject);
                    return;
                }

                var dialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = willOverwrite ? "Confirmar sobrescritura" : global::CSIViewer.Core.Localization.Text("Archivo entrante"),
                    PrimaryButtonText = willOverwrite ? global::CSIViewer.Core.Localization.Text("Sobrescribir") : "Aceptar",
                    CloseButtonText = "Rechazar",
                    DefaultButton = ContentDialogButton.Primary,
                    RequestedTheme = ThemeService.Current
                };
                if (willOverwrite)
                {
                    dialog.SecondaryButtonText = global::CSIViewer.Core.Localization.Text("Guardar como copia");
                }

                var detail = willOverwrite
                    ? global::CSIViewer.Core.Localization.Format("{0} quiere enviar \"{1}\" ({2}). Ya existe un archivo con ese nombre en la carpeta de descargas. Si aceptas, se reemplazará por la versión verificada.",fromName,fileName,FormatSize(size))
                    : destinationExists
                        ? global::CSIViewer.Core.Localization.Format("{0} quiere enviar \"{1}\" ({2}). Ya existe un archivo con ese nombre; se guardará como una copia con otro nombre.",fromName,fileName,FormatSize(size))
                        : $"{fromName} quiere enviar \"{fileName}\" ({FormatSize(size)}).";
                dialog.Content = new TextBlock
                {
                    Text = detail + global::CSIViewer.Core.Localization.Text(" El archivo se verifica con SHA-256 y no se ejecuta automáticamente."),
                    TextWrapping = TextWrapping.Wrap
                };
                var result = await dialog.ShowAsync();
                completion.TrySetResult(result switch
                {
                    ContentDialogResult.Primary => FileReceiveDecision.Accept,
                    ContentDialogResult.Secondary => FileReceiveDecision.AcceptRename,
                    _ => FileReceiveDecision.Reject
                });
            }
            catch
            {
                completion.TrySetResult(FileReceiveDecision.Reject);
            }
            finally
            {
                registration.Dispose();
            }
        });

        return completion.Task;
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.0} MiB"
            : $"{bytes / 1024.0:0.0} KiB";

    private static void AddPermission(
        List<(SessionPermissions Permission, CheckBox Box)> checks,
        StackPanel panel,
        SessionPermissions requested,
        SessionPermissions permission,
        string label)
    {
        if (!requested.HasFlag(permission))
        {
            return;
        }

        var box = new CheckBox { Content = label, IsChecked = true, Tag = permission };
        checks.Add((permission, box));
        panel.Children.Add(box);
    }
}
