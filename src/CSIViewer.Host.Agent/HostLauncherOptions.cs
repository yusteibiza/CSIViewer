namespace CSIViewer.Host.Agent;

public sealed class HostLauncherOptions
{
    public string UiExecutable { get; init; } = "";
    public string UiArguments { get; init; } = "--unattended";
    public int PollSeconds { get; init; } = 5;
    public bool RunOnce { get; init; }

    public static HostLauncherOptions Parse(string[] args)
    {
        var ui = "";
        var uiArguments = "--unattended";
        var pollSeconds = 5;
        var runOnce = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var separator = argument.IndexOf('=');
            var key = separator >= 0 ? argument[..separator] : argument;
            var value = separator >= 0 ? argument[(separator + 1)..] : null;
            if (value is null && index + 1 < args.Length && !args[index + 1].StartsWith('-'))
            {
                value = args[++index];
            }

            switch (key.ToLowerInvariant())
            {
                case "--ui":
                case "--client":
                    ui = value ?? "";
                    break;
                case "--ui-arguments":
                case "--client-arguments":
                case "--arguments":
                    uiArguments = value ?? "";
                    break;
                case "--poll":
                    if (int.TryParse(value, out var seconds))
                    {
                        pollSeconds = Math.Clamp(seconds, 2, 3600);
                    }

                    break;
                case "--once":
                case "--run-once":
                    runOnce = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(ui))
        {
            ui = FindDefaultUi();
        }

        return new HostLauncherOptions
        {
            UiExecutable = ui,
            UiArguments = uiArguments,
            PollSeconds = pollSeconds,
            RunOnce = runOnce
        };
    }

    private static string FindDefaultUi()
    {
        return HostUiLocator.Find() ??
               Path.Combine(AppContext.BaseDirectory, HostUiLocator.UiExecutableName);
    }
}
