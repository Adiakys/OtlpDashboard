import type { MetricsService } from '~/services/MetricsService'
import type { InstrumentDto } from '~/services/types'
import type {
  DashboardLayout,
  MetricBinding,
  WidgetConfig,
  WidgetItem
} from './types'

const EXPORT_VERSION = 1 as const

interface ExportEnvelope {
  version: typeof EXPORT_VERSION
  exportedAt: string
  name: string
  widgets: WidgetItem[]
}

/**
 * Outcome of {@link DashboardLayoutIO.importFromFile}. Discriminated by
 * `kind` so the caller can react to each failure mode without exception
 * flow — i18n and UI feedback live outside this class.
 */
export type ImportOutcome =
  | { kind: 'success'; widgets: WidgetItem[]; unresolvedBindings: number }
  | { kind: 'invalid' }
  | { kind: 'parse-error'; cause: Error }

/**
 * Self-contained import/export of dashboard layouts. Exporting writes a
 * versioned JSON envelope; importing parses the envelope, validates its
 * shape, and rebinds every metric reference against the current instance's
 * instrument catalog so widgets keep working when a layout is moved between
 * instances (resource hashes are instance-specific and never round-trip).
 *
 * The class holds no mutable state — multiple imports/exports can run
 * concurrently against the same instance.
 */
export class DashboardLayoutIO {
  constructor(private readonly metrics: MetricsService) {}

  /**
   * Serialize the working layout to a JSON file and trigger a browser
   * download. Server-managed fields (id, rowVersion, updatedAt) are
   * deliberately omitted — the destination instance reassigns them on save.
   */
  exportToFile(layout: DashboardLayout, name: string): void {
    const envelope: ExportEnvelope = {
      version: EXPORT_VERSION,
      exportedAt: new Date().toISOString(),
      name,
      widgets: layout.widgets
    }
    const filename = `dashboard-${name}-${todayStamp()}.json`
    downloadJson(envelope, filename)
  }

  /**
   * Read a JSON file produced by {@link exportToFile} (or any payload that
   * matches the same shape), validate it, and return the rebinding result.
   * The returned widgets are ready to replace the working layout — the
   * caller decides when to apply them and how to surface failures.
   */
  async importFromFile(file: File): Promise<ImportOutcome> {
    const parsed = await readJsonFile(file)
    if (parsed.kind === 'parse-error') return parsed
    if (!isValidEnvelope(parsed.value)) return { kind: 'invalid' }

    const remapped = await this.rebindToCurrentInstance(parsed.value.widgets)
    return { kind: 'success', ...remapped }
  }

  /**
   * Replace the {@link MetricBinding#resourceHash} of every imported widget
   * with the one belonging to a matching instrument on this instance.
   * Match key: serviceName + scopeName + instrumentName + kind.
   *
   * If the instrument list cannot be fetched (network error), bindings are
   * kept verbatim — the layout still loads, just with empty widgets.
   */
  private async rebindToCurrentInstance(widgets: WidgetItem[]): Promise<{ widgets: WidgetItem[]; unresolvedBindings: number }> {
    const instruments = await this.tryListInstruments()
    if (!instruments) return { widgets, unresolvedBindings: 0 }

    let unresolved = 0
    const remap = (binding: MetricBinding): MetricBinding => {
      const match = findInstrument(instruments, binding)
      if (!match) {
        unresolved++
        return binding
      }
      return bindingFromInstrument(match)
    }

    return {
      widgets: widgets.map(w => remapWidgetBindings(w, remap)),
      unresolvedBindings: unresolved
    }
  }

  private async tryListInstruments(): Promise<InstrumentDto[] | null> {
    try {
      return await this.metrics.listInstruments()
    } catch {
      return null
    }
  }
}

// --- file I/O helpers (kept module-private to keep the class focused) ---

async function readJsonFile(file: File): Promise<{ kind: 'parsed'; value: unknown } | { kind: 'parse-error'; cause: Error }> {
  try {
    const text = await file.text()
    return { kind: 'parsed', value: JSON.parse(text) as unknown }
  } catch (e) {
    return { kind: 'parse-error', cause: e instanceof Error ? e : new Error(String(e)) }
  }
}

function downloadJson(payload: unknown, filename: string): void {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function todayStamp(): string {
  return new Date().toISOString().slice(0, 10)
}

// --- validation ---

function isValidEnvelope(data: unknown): data is ExportEnvelope {
  if (!isObject(data)) return false
  const widgets = (data as { widgets?: unknown }).widgets
  return Array.isArray(widgets) && widgets.every(isValidWidget)
}

function isValidWidget(value: unknown): value is WidgetItem {
  if (!isObject(value)) return false
  const w = value as Record<string, unknown>
  return typeof w.id === 'string'
    && typeof w.kind === 'string'
    && typeof w.x === 'number'
    && typeof w.y === 'number'
    && typeof w.w === 'number'
    && typeof w.h === 'number'
    && isObject(w.config)
}

function isObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object'
}

// --- binding remap ---

function findInstrument(instruments: InstrumentDto[], binding: MetricBinding): InstrumentDto | null {
  const expectedService = binding.serviceName ?? null
  const expectedInstance = binding.serviceInstanceId ?? null
  let serviceFallback: InstrumentDto | null = null
  for (const i of instruments) {
    if (i.scopeName !== binding.scopeName) continue
    if (i.name !== binding.instrumentName) continue
    if (i.kind !== binding.kind) continue
    // Older exports (pre-serviceName field) match service-agnostically rather
    // than not at all.
    if (expectedService !== null && i.serviceName !== expectedService) continue
    // When the import pins an instance id, prefer the matching one;
    // remember the first service-only match in case the pinned id isn't
    // present in the live catalog (covers re-deploys that change the
    // instance id but keep the service name stable).
    if (expectedInstance !== null) {
      if (i.serviceInstanceId === expectedInstance) return i
      serviceFallback ??= i
      continue
    }
    return i
  }
  return serviceFallback
}

function bindingFromInstrument(instrument: InstrumentDto): MetricBinding {
  return {
    resourceHash: instrument.resourceHash,
    scopeName: instrument.scopeName,
    instrumentName: instrument.name,
    kind: instrument.kind,
    serviceName: instrument.serviceName,
    serviceInstanceId: instrument.serviceInstanceId,
    unit: instrument.unit,
    description: instrument.description
  }
}

function remapWidgetBindings(
  widget: WidgetItem,
  remap: (binding: MetricBinding) => MetricBinding
): WidgetItem {
  const config = widget.config as unknown as Record<string, unknown>
  const next: Record<string, unknown> = { ...config }
  if (isObject(config.metric)) {
    next.metric = remap(config.metric as unknown as MetricBinding)
  }
  if (Array.isArray(config.metrics)) {
    next.metrics = (config.metrics as unknown as MetricBinding[]).map(remap)
  }
  return { ...widget, config: next as unknown as WidgetConfig }
}
