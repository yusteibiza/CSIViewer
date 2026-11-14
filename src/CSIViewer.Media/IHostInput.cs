using CSIViewer.Protocol;

namespace CSIViewer.Media;

public interface IHostInput
{
    bool Send(InputEventPayload input, MonitorBounds? monitor = null);
    bool Send(InputEventPayload input, MonitorBounds? monitor, out InputInjectionDiagnostics? diagnostics);
    int ReleaseAll();
    void ClearPressedState();
}
