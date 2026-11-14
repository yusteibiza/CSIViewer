using CSIViewer.Core;

namespace CSIViewer.Application;

public enum HostStartupState { Unknown, NotInstalled, Disabled, Ready, Running }

public sealed record HostStartupStatus(HostStartupState State, bool LastConfirmed = false)
{
    public bool CanActivate => State is HostStartupState.NotInstalled or HostStartupState.Disabled;
    public bool CanDeactivate => IsInstalled;
    public bool IsInstalled => State is HostStartupState.Disabled or HostStartupState.Ready or HostStartupState.Running;
    public string Text => State switch
    {
        HostStartupState.NotInstalled => Localization.Text("No instalado"),
        HostStartupState.Disabled => Localization.Text("Desactivado"),
        HostStartupState.Ready => Localization.Text("Activado"),
        HostStartupState.Running => Localization.Text("Activado (en ejecución)"),
        _ => Localization.Text("Estado no accesible. Actualiza con permisos de administrador.")
    } + (LastConfirmed ? Localization.Text(" (último estado confirmado)") : "");
}
