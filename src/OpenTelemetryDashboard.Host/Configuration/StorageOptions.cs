using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenTelemetryDashboard.Host.Configuration;

public enum StorageProvider
{
    Sqlite = 0,
    PostgreSql = 1,
    SqlServer = 2,
}

public sealed class StorageOptions
{
    public const string SectionName = "Dashboard:Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.Sqlite;
}

public static class StorageOptionsExtensions
{
    public static IServiceCollection AddStorageOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
