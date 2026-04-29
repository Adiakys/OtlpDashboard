import type { MetricsService } from '~/services/MetricsService'
import { DashboardLayoutIO, type ImportOutcome } from '../dashboardLayoutIO'
import type { DashboardLayout, WidgetItem } from '../types'

/**
 * Thin Vue-aware wrapper around `DashboardLayoutIO`. Exposes export/import
 * at the same call-site granularity the page needs, while keeping the heavy
 * lifting (file parsing, validation, binding remap) in the framework-free
 * class — easier to unit test, easier to reason about.
 */
export function useDashboardIO(metrics: MetricsService) {
  const io = new DashboardLayoutIO(metrics)

  function exportLayout(layout: DashboardLayout, name: string): void {
    io.exportToFile(layout, name)
  }

  async function importLayout(file: File): Promise<ImportOutcome> {
    return io.importFromFile(file)
  }

  return { exportLayout, importLayout }
}

export type { ImportOutcome }
export type { WidgetItem }
