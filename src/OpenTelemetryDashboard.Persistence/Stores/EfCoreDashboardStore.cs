using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.Persistence.Stores;

/// <summary>
/// EF Core adapter for <see cref="IDashboardStore"/>. The Dashboards module
/// owns the entity and the port; this assembly owns the storage shape and
/// migrations.
/// </summary>
public sealed class EfCoreDashboardStore : IDashboardStore
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;

    public EfCoreDashboardStore(IDbContextFactory<TelemetryDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Dashboard>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Dashboards
            .Include(d => d.Widgets)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Dashboards
            .AsNoTracking()
            .Include(d => d.Widgets)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Dashboards.Add(dashboard);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the dashboard's scalar fields and reconciles its widgets
    /// (add/update/delete) against the desired state.
    /// <para>
    /// Optimistic concurrency: the caller must set <paramref name="dashboard"/>.<c>RowVersion</c>
    /// to the value most recently read from the store. A mismatch — either against the
    /// just-loaded row or against EF's original value at SaveChanges time — surfaces as
    /// <see cref="DashboardConcurrencyException"/>.
    /// </para>
    /// </summary>
    public async Task UpdateAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Load tracked (no AsNoTracking) so SaveChanges sees our diff and the
        // UPDATE's WHERE clause uses the just-loaded RowVersion as original.
        var existing = await context.Dashboards
            .Include(d => d.Widgets)
            .FirstOrDefaultAsync(d => d.Id == dashboard.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Row vanished between the caller's read and our load — same
            // outcome to a stale RowVersion: the client must reload and retry.
            throw new DashboardConcurrencyException(
                "The dashboard no longer exists. Reload and retry.");
        }

        if (existing.RowVersion != dashboard.RowVersion)
        {
            throw new DashboardConcurrencyException(
                "The dashboard has been modified by another writer. Reload and retry.");
        }

        // SetValues bulk-copies scalar properties from the desired snapshot
        // onto the tracked entity (it skips navigation collections, so
        // widgets stay reconciled below). RowVersion needs to be bumped
        // afterwards because dashboard.RowVersion is the expected current
        // value, not the post-save one.
        var entry = context.Entry(existing);
        entry.CurrentValues.SetValues(dashboard);
        entry.Property(d => d.RowVersion).CurrentValue = checked(dashboard.RowVersion + 1);

        ReconcileWidgets(context, existing, dashboard);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another writer slipped in between our load and SaveChanges.
            throw new DashboardConcurrencyException(
                "The dashboard has been modified by another writer. Reload and retry.",
                ex);
        }
    }

    public async Task DeleteAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // The entity is detached; Remove tracks it as Deleted and the FK
        // cascade configured in DashboardConfiguration takes care of widgets.
        context.Dashboards.Remove(dashboard);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Diffs the desired widget set against what's currently tracked and
    /// schedules add/update/delete operations on the context.
    /// </summary>
    private static void ReconcileWidgets(
        TelemetryDbContext context,
        Dashboard existing,
        Dashboard desired)
    {
        var desiredById = desired.Widgets.ToDictionary(w => w.Id);
        var existingById = existing.Widgets.ToDictionary(w => w.Id);

        // Delete: widgets present on the persisted side but not in the
        // desired payload.
        foreach (var stale in existing.Widgets.Where(w => !desiredById.ContainsKey(w.Id)).ToList())
        {
            context.Remove(stale);
        }

        foreach (var (id, want) in desiredById)
        {
            if (existingById.TryGetValue(id, out var current))
            {
                // Bulk-copy scalar properties from the desired widget onto
                // the tracked one. Id/DashboardId match by construction so
                // the assignment is harmless.
                context.Entry(current).CurrentValues.SetValues(want);
            }
            else
            {
                // Add: attach a fresh widget to the parent's nav collection
                // so EF's change tracker sees it as Added on SaveChanges.
                existing.Widgets.Add(want);
            }
        }
    }
}
