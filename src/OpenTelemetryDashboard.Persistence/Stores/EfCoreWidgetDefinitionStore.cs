using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.Persistence.Stores;

/// <summary>
/// EF Core adapter for <see cref="IWidgetDefinitionStore"/>. Mirrors the
/// pattern of <see cref="EfCoreDashboardStore"/>: hexagonal port lives in
/// the Widgets module, this assembly owns the storage shape and migrations.
/// </summary>
public sealed class EfCoreWidgetDefinitionStore : IWidgetDefinitionStore
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;

    public EfCoreWidgetDefinitionStore(IDbContextFactory<TelemetryDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<WidgetDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // SQLite can't ORDER BY DateTimeOffset server-side; fetch and sort
        // client-side. Custom widget catalogs are small (tens of entries at
        // most), so the in-memory sort is invisible.
        var rows = await context.WidgetDefinitions
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        rows.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        return rows;
    }

    public async Task<WidgetDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.WidgetDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(
        WidgetDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.WidgetDefinitions.Add(definition);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the definition's scalar fields and bumps the RowVersion.
    /// Optimistic concurrency: a stale <paramref name="definition"/>'s
    /// <c>RowVersion</c> surfaces as
    /// <see cref="WidgetDefinitionConcurrencyException"/>.
    /// </summary>
    public async Task UpdateAsync(
        WidgetDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await context.WidgetDefinitions
            .FirstOrDefaultAsync(d => d.Id == definition.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            throw new WidgetDefinitionConcurrencyException(
                "The widget definition no longer exists. Reload and retry.");
        }

        if (existing.RowVersion != definition.RowVersion)
        {
            throw new WidgetDefinitionConcurrencyException(
                "The widget definition has been modified by another writer. Reload and retry.");
        }

        var entry = context.Entry(existing);
        entry.CurrentValues.SetValues(definition);
        entry.Property(d => d.RowVersion).CurrentValue = checked(definition.RowVersion + 1);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new WidgetDefinitionConcurrencyException(
                "The widget definition has been modified by another writer. Reload and retry.",
                ex);
        }
    }

    public async Task DeleteAsync(
        WidgetDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.WidgetDefinitions.Remove(definition);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
