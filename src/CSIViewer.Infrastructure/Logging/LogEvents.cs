using Microsoft.Extensions.Logging;

namespace CSIViewer.Infrastructure.Logging;

/// <summary>
/// Stable event identifiers used by the structured logs. Keeping them stable
/// makes it possible to search historical logs after a build change.
/// </summary>
public static class LogEvents
{
    public static readonly EventId AppStarted = new(1000, "AppStarted");
    public static readonly EventId AppStopping = new(1001, "AppStopping");
    public static readonly EventId DiagnosticsExported = new(1002, "DiagnosticsExported");

    public static readonly EventId ConnectionOpening = new(2000, "ConnectionOpening");
    public static readonly EventId ConnectionOpened = new(2001, "ConnectionOpened");
    public static readonly EventId ConnectionClosed = new(2002, "ConnectionClosed");
    public static readonly EventId ConnectionStateChanged = new(2003, "ConnectionStateChanged");
    public static readonly EventId ReconnectAttempt = new(2010, "ReconnectAttempt");
    public static readonly EventId ReconnectSucceeded = new(2011, "ReconnectSucceeded");
    public static readonly EventId ReconnectFailed = new(2012, "ReconnectFailed");

    public static readonly EventId AuthenticationSucceeded = new(2100, "AuthenticationSucceeded");
    public static readonly EventId AuthenticationFailed = new(2101, "AuthenticationFailed");
    public static readonly EventId AuthenticationRejected = new(2102, "AuthenticationRejected");
    public static readonly EventId AuthenticationRateLimited = new(2103, "AuthenticationRateLimited");

    public static readonly EventId SessionStarted = new(2200, "SessionStarted");
    public static readonly EventId SessionEnded = new(2201, "SessionEnded");
    public static readonly EventId SessionStateChanged = new(2202, "SessionStateChanged");

    public static readonly EventId TransferStarted = new(2300, "TransferStarted");
    public static readonly EventId TransferCompleted = new(2301, "TransferCompleted");
    public static readonly EventId TransferFailed = new(2302, "TransferFailed");
    public static readonly EventId TransferInterrupted = new(2303, "TransferInterrupted");
    public static readonly EventId TransferResumed = new(2304, "TransferResumed");

    public static readonly EventId CaptureStarted = new(2400, "CaptureStarted");
    public static readonly EventId CaptureStopped = new(2401, "CaptureStopped");
    public static readonly EventId CaptureFailed = new(2402, "CaptureFailed");

    public static readonly EventId UnhandledException = new(9000, "UnhandledException");
    public static readonly EventId BackgroundTaskFailed = new(9001, "BackgroundTaskFailed");
}
