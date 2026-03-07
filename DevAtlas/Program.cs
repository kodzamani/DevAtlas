using Avalonia;
using System.Runtime.InteropServices;

namespace DevAtlas;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
