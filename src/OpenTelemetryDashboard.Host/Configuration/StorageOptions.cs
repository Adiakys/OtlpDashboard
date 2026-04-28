using System.ComponentModel.DataAnnotations;
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
    public const string SectionName = "OpenTelemetryDashboard:Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.Sqlite;
    public SqliteStorageOptions Sqlite { get; set; } = new();
}

public sealed class SqliteStorageOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = "Data Source=./data/telemetry.db";
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
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
