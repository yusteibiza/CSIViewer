using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Security;

namespace CSIViewer.Services;

public sealed partial class AppState : ObservableObject
{
    public DeviceIdentity? Identity { get; set; }

    [ObservableProperty]
    private string _deviceId = "—";

    [ObservableProperty]
    private string _deviceName = Environment.MachineName;

    [ObservableProperty]
    private string _temporaryPasswordDisplay = "—";

    [ObservableProperty]
    private DateTimeOffset? _temporaryPasswordExpiresUtc;

    [ObservableProperty]
    private bool _usesCustomPassword;

    [ObservableProperty]
    private string _serverStatus = global::CSIViewer.Core.Localization.Text("Sin conexión");

    [ObservableProperty]
    private bool _isServerOnline;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool _isReconnecting;

    [ObservableProperty]
    private int _reconnectAttempt;

    [ObservableProperty]
    private int _reconnectMaxRetries;

    [ObservableProperty]
    private string _remoteDeviceId = "—";

    [ObservableProperty]
    private string _remoteDeviceName = global::CSIViewer.Core.Localization.Text("Ninguno");

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isController;

    [ObservableProperty]
    private int _activeSessionCount;

    [ObservableProperty]
    private string _activeSessionNames = "";

    [ObservableProperty]
    private SessionPermissions _activePermissions;

    [ObservableProperty]
    private TransportKind _transport = TransportKind.Relay;

    [ObservableProperty]
    private string _statusMessage = "Listo";

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private AppSettings _settings = new();

    public string TemporaryPasswordRaw { get; set; } = "";
    public string TemporaryPasswordSalt { get; set; } = "";
    public string TemporaryPasswordHash { get; set; } = "";
    public string PublicKeyBase64 => Identity?.PublicKeyBase64 ?? "";
    public string Fingerprint => Identity?.Fingerprint ?? "";
    public string? ActiveSessionId { get; set; }

    public ObservableCollection<KnownDevice> KnownDevices { get; } = [];
    public ObservableCollection<DeviceList> DeviceLists { get; } = [];
    public ObservableCollection<ConnectionHistoryEntry> History { get; } = [];
    public ObservableCollection<ChatMessage> ChatMessages { get; } = [];
    public ObservableCollection<TransferItem> Transfers { get; } = [];
    public ObservableCollection<Contact> Contacts { get; } = [];

    public Action<Action>? UiInvoker { get; set; }

    public event EventHandler? SessionChanged;

    public void Post(Action action)
    {
        var invoker = UiInvoker;
        if (invoker is null)
        {
            action();
        }
        else
        {
            invoker(action);
        }
    }

    public void NotifySessionChanged()
    {
        Post(() => SessionChanged?.Invoke(this, EventArgs.Empty));
    }

    public void ResetSession()
    {
        Post(() =>
        {
            IsSessionActive = false;
            IsController = false;
            ActiveSessionCount = 0;
            ActiveSessionNames = "";
            ActivePermissions = SessionPermissions.None;
            Transport = TransportKind.Relay;
            RemoteDeviceId = "—";
            RemoteDeviceName = global::CSIViewer.Core.Localization.Text("Ninguno");
            ActiveSessionId = null;
            StatusMessage = "Listo";
        });
    }
}
