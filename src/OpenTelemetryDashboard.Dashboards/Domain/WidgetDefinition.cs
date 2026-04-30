namespace OpenTelemetryDashboard.Dashboards.Domain;

/// <summary>
/// A user-saved widget definition. The dashboard module stores instances
/// (placement + per-instance config); this entity stores the *recipe* the
/// user can reuse across dashboards. Source attribution lives implicitly in
/// the fully-qualified <c>kind</c> the SPA writes into placements
/// (<c>std:metric-stat</c>, <c>custom:&lt;guid&gt;</c>,
/// <c>library:&lt;libId&gt;/&lt;kindId&gt;</c>) — the entity here only
/// covers the <c>custom:</c> source. Library and builtin definitions are
/// resolved at render time from filesystem and bundle respectively.
/// </summary>
public sealed class WidgetDefinition
{
    public Guid Id { get; init; }

    /// <summary>
    /// Human-readable label shown in the picker. Max 64 chars (mirrors the EF
    /// column constraint).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional short description shown alongside the name in the picker.
    /// Max 280 chars.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Phosphor / Lucide icon class — e.g. <c>i-ph-target</c>. Validated
    /// against a strict regex on save to avoid arbitrary CSS classes.
    /// </summary>
    public string Icon { get; init; } = "i-ph-puzzle-piece";

    public WidgetEngine Engine { get; init; }

    /// <summary>
    /// For <see cref="WidgetEngine.Preset"/>: the builtin kind being wrapped
    /// (e.g. <c>metric-stat</c>). Stored *unprefixed* (no <c>std:</c>) — the
    /// SPA prepends the source prefix when rendering. <c>null</c> for
    /// engines that don't need it (Spec/Composite).
    /// </summary>
    public string? BaseKind { get; init; }

    /// <summary>
    /// Opaque JSON config payload. For <see cref="WidgetEngine.Preset"/>
    /// this is the seed values the user pre-baked; for other engines it can
    /// hold engine-specific knobs. The backend never parses this payload.
    /// </summary>
    public string ConfigJson { get; init; } = "{}";

    /// <summary>
    /// Opaque JSON spec payload. For <see cref="WidgetEngine.Spec"/> this
    /// holds the Vega-Lite spec; for <see cref="WidgetEngine.Composite"/>
    /// the layout DSL. <c>null</c> for <see cref="WidgetEngine.Preset"/>.
    /// </summary>
    public string? SpecJson { get; init; }

    /// <summary>Default grid width (columns). Range 1–12.</summary>
    public int DefaultW { get; init; } = 3;

    /// <summary>Default grid height (rows). Range 1–24.</summary>
    public int DefaultH { get; init; } = 3;

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// App-managed optimistic concurrency token. Same pattern as
    /// <see cref="Dashboard.RowVersion"/>.
    /// </summary>
    public uint RowVersion { get; init; }
}
