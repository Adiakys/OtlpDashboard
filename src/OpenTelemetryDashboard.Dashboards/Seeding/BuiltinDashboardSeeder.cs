using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.Dashboards.Seeding;

/// <summary>
/// Default <see cref="IBuiltinDashboardSeeder"/>. Walks the pack
/// registry, picks every <see cref="PackDashboard"/> marked
/// <c>builtin: true</c>, parses each with <see cref="DashboardSeedParser"/>,
/// and emits an <see cref="IDashboardStore.AddAsync"/> per file whose
/// resolved id is not yet in the store. Special-cases
/// <see cref="Dashboard.DefaultId"/>: when no file claims that id and
/// no row with that id exists, an empty default is added so the SPA
/// always has a starting point.
/// </summary>
public sealed partial class BuiltinDashboardSeeder : IBuiltinDashboardSeeder
{
    private const string DefaultDashboardId = "default";

    private readonly IDashboardStore _store;
    private readonly IPackRegistry _packs;
    private readonly ILogger<BuiltinDashboardSeeder> _logger;

    public BuiltinDashboardSeeder(
        IDashboardStore store,
        IPackRegistry packs,
        ILogger<BuiltinDashboardSeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _packs = packs;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = (await _store.GetAllIdsAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet();

        var resolved = await ResolveFilesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Default gets dedicated handling: when the historic migration
            // has already inserted an empty default row, replace it with
            // the file's content (pristine-upsert). User-modified defaults
            // are left alone. A missing default falls through to AddAsync.
            if (entry.Id == Dashboard.DefaultId)
            {
                if (existing.Contains(entry.Id))
                {
                    await TryUpsertDefaultIfPristineAsync(entry, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await AddAsync(entry, cancellationToken).ConfigureAwait(false);
                    existing.Add(entry.Id);
                    _logger.SeedAdded(entry.Id, entry.SourcePath);
                }
                continue;
            }

            if (existing.Contains(entry.Id))
            {
                _logger.SeedSkippedExisting(entry.Id, entry.SourcePath);
                continue;
            }

            await AddAsync(entry, cancellationToken).ConfigureAwait(false);
            existing.Add(entry.Id);
            _logger.SeedAdded(entry.Id, entry.SourcePath);
        }

        // Guarantee an empty default if neither the seeder nor the
        // historic migration produced one. Covers fresh installs where
        // the migration was skipped (e.g. tests) plus a rolled-back db.
        if (!existing.Contains(Dashboard.DefaultId))
        {
            await _store.AddAsync(EmptyDefault(), cancellationToken).ConfigureAwait(false);
            _logger.SeedDefaultEmpty();
        }
    }

    /// <summary>Walk packs in registry order, expand the
    /// <c>builtin: true</c> dashboards, parse each with strict shape
    /// validation, and dedupe by resolved id. Invalid files are skipped
    /// with a warning — one broken pack-shipped dashboard doesn't
    /// poison the rest.</summary>
    private async Task<List<ResolvedSeed>> ResolveFilesAsync(CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedSeed>();
        var seen = new HashSet<Guid>();

        var packs = await _packs.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pack in packs)
        {
            foreach (var dash in pack.Dashboards)
            {
                if (!dash.Builtin) continue;

                if (!DashboardSeedParser.TryParse(dash.RawJson, out var parsed, out var error))
                {
                    _logger.SeedRejected(dash.SourcePath, error);
                    continue;
                }

                var id = ResolveId(parsed.Id, pack.Id, dash.Id);
                if (!seen.Add(id))
                {
                    _logger.SeedShadowed(id, dash.SourcePath);
                    continue;
                }

                resolved.Add(new ResolvedSeed(id, parsed, dash.SourcePath));
            }
        }

        return resolved;
    }

    /// <summary>Resolves the dashboard id with the documented precedence:
    /// explicit <c>id</c> in the dashboard JSON wins; the well-known
    /// "default" id maps to <see cref="Dashboard.DefaultId"/>; otherwise
    /// a deterministic Guid derived from <c>&lt;packId&gt;/&lt;dashId&gt;</c>
    /// keeps the same dashboard stable across reloads.</summary>
    private static Guid ResolveId(Guid? explicitId, string packId, string dashId)
    {
        if (explicitId is { } id) return id;
        if (string.Equals(dashId, DefaultDashboardId, StringComparison.Ordinal))
        {
            return Dashboard.DefaultId;
        }
        return DeterministicGuidFrom($"{packId}/{dashId}");
    }

    /// <summary>Deterministic Guid derivation using SHA-256 truncated
    /// to 16 bytes, with version/variant nibbles set to match the RFC
    /// 4122 v8 (custom) shape. Stable across processes and platforms.</summary>
    private static Guid DeterministicGuidFrom(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private async Task<bool> TryUpsertDefaultIfPristineAsync(ResolvedSeed entry, CancellationToken cancellationToken)
    {
        var existing = await _store.GetByIdAsync(Dashboard.DefaultId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return false;

        // "Pristine" = the migration's empty insert that no human has
        // touched. Any save through the API increments RowVersion or
        // adds widgets, so either signal is enough to back off.
        if (existing.Widgets.Count > 0 || existing.RowVersion > 0)
        {
            _logger.SeedSkippedExisting(entry.Id, entry.SourcePath);
            return true;
        }

        var replacement = ToDomain(entry, existing.RowVersion);
        try
        {
            await _store.UpdateAsync(replacement, cancellationToken).ConfigureAwait(false);
            _logger.SeedDefaultUpserted(entry.SourcePath);
        }
        catch (DashboardConcurrencyException)
        {
            _logger.SeedSkippedExisting(entry.Id, entry.SourcePath);
        }
        return true;
    }

    private async Task AddAsync(ResolvedSeed entry, CancellationToken cancellationToken)
    {
        var dashboard = ToDomain(entry, rowVersion: 0);
        await _store.AddAsync(dashboard, cancellationToken).ConfigureAwait(false);
    }

    private static Dashboard ToDomain(ResolvedSeed entry, uint rowVersion)
    {
        var widgets = new List<DashboardWidget>(entry.File.Widgets.Count);
        foreach (var w in entry.File.Widgets)
        {
            widgets.Add(new DashboardWidget
            {
                Id = w.Id,
                DashboardId = entry.Id,
                Kind = w.Kind,
                X = w.X,
                Y = w.Y,
                W = w.W,
                H = w.H,
                ConfigJson = w.ConfigJson
            });
        }
        return new Dashboard
        {
            Id = entry.Id,
            Name = entry.File.Name,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = rowVersion,
            Widgets = widgets
        };
    }

    private static Dashboard EmptyDefault() => new()
    {
        Id = Dashboard.DefaultId,
        Name = "main",
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = 0,
        Widgets = []
    };

    private sealed record ResolvedSeed(Guid Id, DashboardSeedFile File, string SourcePath);
}

internal static partial class BuiltinDashboardSeederLogs
{
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Skipping {Path}: {Error}")]
    public static partial void SeedRejected(this ILogger logger, string path, string? error);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Built-in dashboard id {Id} from {Path} is shadowed by an earlier file in the scan order; skipping.")]
    public static partial void SeedShadowed(this ILogger logger, Guid id, string path);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information,
        Message = "Skipping built-in dashboard {Id} from {Path}: id already present in the store.")]
    public static partial void SeedSkippedExisting(this ILogger logger, Guid id, string path);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information,
        Message = "Seeded built-in dashboard {Id} from {Path}.")]
    public static partial void SeedAdded(this ILogger logger, Guid id, string path);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information,
        Message = "Replaced pristine default dashboard with file {Path}.")]
    public static partial void SeedDefaultUpserted(this ILogger logger, string path);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information,
        Message = "No built-in default dashboard present; seeded an empty default dashboard.")]
    public static partial void SeedDefaultEmpty(this ILogger logger);
}
