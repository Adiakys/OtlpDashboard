import { AllCommunityModule, ModuleRegistry, provideGlobalGridOptions } from 'ag-grid-community'

/**
 * AG Grid v33+ requires modules to be registered before any grid is mounted.
 * We register the entire community bundle once on the client; the wrapper
 * <AppDataGrid /> assumes this plugin has run.
 *
 * v33 also defaults to the new JS-based Theming API which ignores the CSS
 * theme classes (`ag-theme-quartz`, `ag-theme-quartz-dark`). Switching to
 * `theme: 'legacy'` re-enables the CSS-driven Quartz theme so light/dark
 * follow our `useColorMode` toggle in <AppDataGrid />.
 */
export default defineNuxtPlugin(() => {
  ModuleRegistry.registerModules([AllCommunityModule])
  provideGlobalGridOptions({ theme: 'legacy' })
})
