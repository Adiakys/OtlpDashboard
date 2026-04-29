/**
 * Centralized `useState` keys used by the dashboard module. Exported as
 * constants so they can't drift across composables and so renaming requires
 * touching exactly one file.
 */
export const STATE_INSTRUMENT_CATALOG = 'dashboard:instrumentCatalog'
export const STATE_SERIES_CACHE = 'dashboard:seriesCache'
