using Microsoft.UI.Xaml;
using CSIViewer.Application;
using Microsoft.Extensions.DependencyInjection;

namespace CSIViewer.Services;

public static class ThemeService
{
    public static ElementTheme Current { get; private set; } = ElementTheme.Default;

    public static void Apply(AppTheme theme, FrameworkElement? root = null)
    {
        Current = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        var target = root ?? MainWindow.Instance?.Content as FrameworkElement;
        if (target is not null)
        {
            target.RequestedTheme = Current;
            CSIViewer.Presentation.Translate.Apply(target, App.Services.GetRequiredService<AppState>().Settings.Language);
            CSIViewer.Presentation.AccentAppearance.Apply(App.Services.GetRequiredService<AppState>().Settings.AccentColor, target);
        }
    }

    public static AppTheme ToSetting(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => AppTheme.Light,
        ElementTheme.Dark => AppTheme.Dark,
        _ => AppTheme.System
    };
}
