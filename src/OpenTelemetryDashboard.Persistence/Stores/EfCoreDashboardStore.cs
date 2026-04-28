using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.Persistence.Stores;

/// <summary>
/// EF Core adapter for <see cref="IDashboardStore"/>. Persists the singleton
/// "default" dashboard inside the shared <see cref="TelemetryDbContext"/> —
/// the Dashboards module owns the entity and the port; this assembly owns
/// the storage shape and migrations.
/// </summary>
public sealed class EfCoreDashboardStore : IDashboardStore
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;

    public EfCoreDashboardStore(IDbContextFactory<TelemetryDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<Dashboard> GetDefaultAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.Dashboards
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == Dashboard.DefaultId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // Lazy-create on first access. A unique-key collision means another
        // request raced us; in that case re-read what they inserted.
        var seed = new Dashboard
        {
            Id = Dashboard.DefaultId,
            Name = "Default",
            LayoutJson = """{"widgets":[]}""",
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = 1
        };

        context.Dashboards.Add(seed);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return seed;
        }
        catch (DbUpdateException)
        {
            return await context.Dashboards
                .AsNoTracking()
                .FirstAsync(d => d.Id == Dashboard.DefaultId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<Dashboard> SaveDefaultAsync(
        string name,
        string layoutJson,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(layoutJson);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.Dashboards
            .FirstOrDefaultAsync(d => d.Id == Dashboard.DefaultId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // No row yet — only an "expected version 0" save is allowed,
            // matching what GetDefaultAsync would have returned to a caller
            // that managed to PUT before any GET. Anything else is stale.
            if (expectedRowVersion != 0)
            {
                throw new DashboardConcurrencyException();
            }

            existing = new Dashboard
            {
                Id = Dashboard.DefaultId,
                Name = name,
                LayoutJson = layoutJson,
                UpdatedAt = DateTimeOffset.UtcNow,
                RowVersion = 1
            };
            context.Dashboards.Add(existing);
        }
        else
        {
            if (existing.RowVersion != expectedRowVersion)
            {
                throw new DashboardConcurrencyException();
            }

            existing.Name = name;
            existing.LayoutJson = layoutJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.RowVersion = checked(existing.RowVersion + 1);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // EF Core didn't find the expected RowVersion at SaveChanges time:
            // a writer slipped in between our read and our save.
            throw new DashboardConcurrencyException(
                "The dashboard has been modified by another writer. Reload and retry.",
                ex);
        }

        return existing;
    }
}
