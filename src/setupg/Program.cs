using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Com0com.Setupg;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
}

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        RequestedThemeVariant = ThemeVariant.Light;
        Name = "NewVirtualCOMPair";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The application icon (embedded com0com.ico).</summary>
    public static WindowIcon? Icon
    {
        get
        {
            var s = typeof(App).Assembly.GetManifestResourceStream("com0com.ico");
            return s == null ? null : new WindowIcon(s);
        }
    }
}
