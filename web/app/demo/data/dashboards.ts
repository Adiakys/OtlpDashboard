import type { DashboardDto, DashboardWidgetDto } from '~/services/types'
import { DEMO_BUNDLE, type RawBundledDashboard } from './bundle'

/**
 * Convert a raw on-disk dashboard JSON into the `DashboardDto` wire shape
 * the SPA's services consume. The on-disk file omits `updatedAt` /
 * `rowVersion` (a server concern) so the demo synthesises them.
 */
function dashboardFromBundle(raw: RawBundledDashboard): DashboardDto {
  return {
    id: raw.id,
    name: raw.name,
    widgets: raw.widgets.map<DashboardWidgetDto>((w) => ({
      id: w.id,
      kind: w.kind,
      x: w.x,
      y: w.y,
      w: w.w,
      h: w.h,
      config: w.config
    })),
    updatedAt: new Date(0).toISOString(),
    rowVersion: 1
  }
}

/**
 * Snapshot of every dashboard the demo seeds. Mutations live in the
 * `DashboardStore` (which copies on demand into `StorageService`); this
 * array is the immutable source of truth used to seed and to back a
 * "Reset demo" affordance later.
 */
export const SEED_DASHBOARDS: DashboardDto[] =
  DEMO_BUNDLE.dashboards.map(dashboardFromBundle)
