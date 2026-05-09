using OpenTelemetryDashboard.Host.Configuration;
using OpenTelemetryDashboard.Persistence.PostgreSql;
using OpenTelemetryDashboard.Persistence.Sqlite;
using OpenTelemetryDashboard.Persistence.SqlServer;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Selects and registers the EF Core persistence provider. The provider is the
/// one value we MUST know at registration time — it dictates which DI
/// extensions are wired. Connection strings stay lazy (resolved through
/// <see cref="IConfiguration"/>) so test fixtures can override them.
/// </summary>
internal static class TelemetryStorageExtensions
{
    public static WebApplicationBuilder AddTelemetryStorage(this WebApplicationBuilder builder)
    {
        builder.Services.AddStorageOptions(builder.Configuration);

        var provider = builder.Configuration
            .GetValue<StorageProvider>($"{StorageOptions.SectionName}:{nameof(StorageOptions.Provider)}");

        switch (provider)
        {
            case StorageProvider.Sqlite:
                builder.Services.AddSqliteTelemetryStore(ResolveConnectionString("Sqlite"));
                break;
            case StorageProvider.SqlServer:
                builder.Services.AddSqlServerTelemetryStore(ResolveConnectionString("SqlServer"));
                break;
            case StorageProvider.PostgreSql:
                builder.Services.AddPostgreSqlTelemetryStore(ResolveConnectionString("PostgreSql"));
                break;
            default:
                throw new InvalidOperationException(
                    $"Storage provider '{provider}' is not supported in this build.");
        }

        return builder;
    }

    private static Func<IServiceProvider, string> ResolveConnectionString(string name) =>
        sp =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{name} is missing or empty in configuration.");
            }
            return cs;
        };
}
