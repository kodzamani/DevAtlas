using DevAtlas.Configuration;
using DevAtlas.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevAtlas.Services
{
    // Extension methods for dependency injection
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddScalabilityServices(this IServiceCollection services,
            ScalabilityConfiguration? config = null,
            FeatureFlags? featureFlags = null)
        {
            // Add configuration
            services.AddSingleton(config ?? new ScalabilityConfiguration());
            services.AddSingleton(featureFlags ?? new FeatureFlags());

            // Add database
            services.AddDbContext<DevAtlasDbContext>((provider, options) =>
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolder = Path.Combine(appDataPath, "DevAtlas");

                if (!Directory.Exists(appFolder))
                {
                    Directory.CreateDirectory(appFolder);
                }

                var dbPath = Path.Combine(appFolder, "devatlas.db");
                options.UseSqlite($"Data Source={dbPath}");
            });

            // Add caching
            services.AddMemoryCache(options =>
            {
                options.SizeLimit = 1000; // Limit cache size
            });

            // Add scalability service
            services.AddSingleton<ScalabilityService>();

            return services;
        }

        public static IServiceCollection AddScalabilityServicesWithConfiguration(this IServiceCollection services,
            IConfiguration configuration)
        {
            var config = new ScalabilityConfiguration();
            configuration.GetSection("Scalability").Bind(config);

            var featureFlags = new FeatureFlags();
            configuration.GetSection("FeatureFlags").Bind(featureFlags);

            return services.AddScalabilityServices(config, featureFlags);
        }
    }
}
