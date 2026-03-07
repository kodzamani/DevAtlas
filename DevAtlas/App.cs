using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DevAtlas.Enums;
using DevAtlas.Services;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace DevAtlas
{
    public partial class App : Application
    {
        private IHost? _host;
        private ScalabilityService? _scalabilityService;
        private EventHandler? _languageChangedHandler;
        private SKTypeface? _liveChartsTypeface;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            RegisterGlobalExceptionHandlers();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddScalabilityServicesWithConfiguration(configuration);
                    services.AddSingleton<ProjectIndex>();
                    services.AddSingleton<ProjectScanner>();
                    services.AddSingleton<ProjectRunner>();
                    services.AddSingleton<CodeEditorDetector>();

                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.AddDebug();
                    });
                })
                .Build();

            _scalabilityService = _host.Services.GetRequiredService<ScalabilityService>();

            _ = LanguageManager.Instance;
            ConfigureLiveChartsTypeface(LanguageManager.Instance.SelectedLanguage);
            _languageChangedHandler = (_, _) => ConfigureLiveChartsTypeface(LanguageManager.Instance.SelectedLanguage);
            LanguageManager.Instance.LanguageChanged += _languageChangedHandler;

            try
            {
                await _scalabilityService.InitializeAsync();
            }
            catch (Exception ex)
            {
                await ErrorDialogService.ShowErrorAsync(string.Format(LanguageManager.Instance["MessageFailedToInitializeServices"], ex.Message),
                    LanguageManager.Instance["MessageInitializationError"]);
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                desktop.ShutdownRequested += Desktop_ShutdownRequested;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async void Desktop_ShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            try
            {
                UnregisterGlobalExceptionHandlers();

                if (_languageChangedHandler != null)
                {
                    LanguageManager.Instance.LanguageChanged -= _languageChangedHandler;
                    _languageChangedHandler = null;
                }

                _liveChartsTypeface?.Dispose();
                _liveChartsTypeface = null;

                if (_scalabilityService != null)
                {
                    await _scalabilityService.ShutdownAsync();
                }

                if (_host != null)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                    _host.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex.Message}");
            }
        }

        public static ScalabilityService? GetScalabilityService()
        {
            if (Current is App app && app._scalabilityService != null)
            {
                return app._scalabilityService;
            }
            return null;
        }

        public static IServiceProvider? GetServiceProvider()
        {
            if (Current is App app && app._host != null)
            {
                return app._host.Services;
            }
            return null;
        }

        private void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void UnregisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        }

        private async void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                await ErrorDialogService.ShowExceptionAsync(ex, "Fatal application error.");
            }
        }

        private async void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            await ErrorDialogService.ShowExceptionAsync(e.Exception, "Background task error.");
            e.SetObserved();
        }

        private void ConfigureLiveChartsTypeface(AppLanguage language)
        {
            var nextTypeface = CreateLiveChartsTypeface(language);
            if (nextTypeface == null)
            {
                return;
            }

            var previousTypeface = _liveChartsTypeface;
            _liveChartsTypeface = nextTypeface;
            LiveChartsSkiaSharp.DefaultSKTypeface = nextTypeface;
            previousTypeface?.Dispose();
        }

        private static SKTypeface? CreateLiveChartsTypeface(AppLanguage language)
        {
            foreach (var fontPath in GetPreferredChartFontPaths(language))
            {
                if (!File.Exists(fontPath))
                {
                    continue;
                }

                var typeface = SKTypeface.FromFile(fontPath);
                if (typeface != null)
                {
                    return typeface;
                }
            }

            foreach (var fontFamily in GetPreferredChartFontFamilies(language))
            {
                var typeface = SKTypeface.FromFamilyName(fontFamily);
                if (typeface != null)
                {
                    return typeface;
                }
            }

            return SKTypeface.Default;
        }

        // LiveCharts tooltips are rasterized by Skia, so we pick a language-aware system font.
        private static IEnumerable<string> GetPreferredChartFontPaths(AppLanguage language)
        {
            var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

            return language switch
            {
                AppLanguage.Japanese =>
                [
                    Path.Combine(fontsDirectory, "YuGothM.ttc"),
                    Path.Combine(fontsDirectory, "YuGothR.ttc"),
                    Path.Combine(fontsDirectory, "msyh.ttc")
                ],
                AppLanguage.ChineseSimplified =>
                [
                    Path.Combine(fontsDirectory, "msyh.ttc"),
                    Path.Combine(fontsDirectory, "msyhl.ttc"),
                    Path.Combine(fontsDirectory, "msjh.ttc")
                ],
                AppLanguage.Korean =>
                [
                    Path.Combine(fontsDirectory, "malgun.ttf"),
                    Path.Combine(fontsDirectory, "malgunsl.ttf"),
                    Path.Combine(fontsDirectory, "msyh.ttc")
                ],
                _ =>
                [
                    Path.Combine(fontsDirectory, "SegUIVar.ttf"),
                    Path.Combine(fontsDirectory, "seguisym.ttf")
                ]
            };
        }

        private static IEnumerable<string> GetPreferredChartFontFamilies(AppLanguage language) => language switch
        {
            AppLanguage.Japanese => ["Yu Gothic UI", "Meiryo UI", "Segoe UI"],
            AppLanguage.ChineseSimplified => ["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI"],
            AppLanguage.Korean => ["Malgun Gothic", "Segoe UI"],
            _ => ["Segoe UI Variable", "Segoe UI", "Arial"]
        };
    }
}
