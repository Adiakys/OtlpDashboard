import { AuthStore } from '~/services/AuthStore'
import { DashboardService } from '~/services/DashboardService'
import { HttpClientService } from '~/services/HttpClientService'
import { InfoService } from '~/services/InfoService'
import { LogsService } from '~/services/LogsService'
import { MetricsService } from '~/services/MetricsService'
import { ServiceMapService } from '~/services/ServiceMapService'
import { TraceService } from '~/services/TraceService'
import { WidgetService } from '~/services/WidgetService'
import type { QueryLimitsDto, TelemetryLimitsDto } from '~/services/types'

/**
 * DI container — runs once per client context. HttpClientService is a
 * singleton shared by all feature services (LogsService / TraceService /
 * InfoService), exactly matching the architectural contract: one HTTP pipe,
 * many callers.
 *
 * Also drains `?token=…` from the landing URL into the AuthStore (30 min
 * default TTL) and strips the parameter from the address bar so it doesn't
 * leak into browser history / logs / shared links. When the login page
 * populates the AuthStore via `setToken(...)`, everything else is unchanged.
 *
 * A single `$fetch` response interceptor turns every 401 into a redirect to
 * `/login?next=…`; the interceptor is wired here (not inside HttpClientService)
 * so the service class stays unaware of vue-router and the feature pages
 * don't need to duplicate any "if 401 then redirect" logic.
 *
 * Bootstraps `$appName` reactively from <c>/api/v1/info</c> so the sidebar
 * and login form reflect the server's configured ApplicationName.
 *
 * Exposed to pages/components via `useNuxtApp()`:
 *   const { $authStore, $logsService, $traceService, $appName } = useNuxtApp()
 */
export default defineNuxtPlugin(async () => {
  const config = useRuntimeConfig()
  const authStore = new AuthStore()
  // Vite replaces this literal at build time. When the env var is unset,
  // the comparison folds to `false` and the entire demo branch (including
  // the dynamic `import('~/demo')`) is dropped from the prod bundle.
  const isDemo = import.meta.env.VITE_DEMO_MODE === 'true'

  if (import.meta.client) {
    const currentUrl = new URL(window.location.href)
    const tokenParam = currentUrl.searchParams.get('token')
    if (tokenParam) {
      authStore.setToken(tokenParam)
      currentUrl.searchParams.delete('token')
      const cleaned = currentUrl.pathname + currentUrl.search + currentUrl.hash
      window.history.replaceState(window.history.state, '', cleaned)
    }
  }

  // Shared 401 handler: clears the auth store and bounces the user to
  // /login. Used by both the real fetcher (via `$fetch.create`) and the
  // demo fetcher (via its `hooks.onResponseError`) so the login UX is
  // identical regardless of build mode. Uses the Vue Router's *logical*
  // path so the loop guard ("don't redirect if already on /login") and
  // the `next=` query param work under any `app.baseURL` (the demo
  // build runs at `/<repo>/`, not `/`).
  const router = useRouter()
  function handleAuthFailure({ response }: { response: { status: number } }): void {
    if (response?.status !== 401) return
    if (!import.meta.client) return
    const current = router.currentRoute.value
    if (current.path === '/login') return
    authStore.clear()
    navigateTo({ path: '/login', query: { next: current.fullPath } }, { replace: true })
  }

  let fetcher: typeof $fetch
  if (isDemo) {
    const { createDemoBootstrap } = await import('~/demo')
    const bootstrap = createDemoBootstrap({
      onResponseError: handleAuthFailure
    })
    fetcher = bootstrap.fetcher
  } else {
    fetcher = $fetch.create({
      onResponseError: handleAuthFailure
    })
  }

  const http = new HttpClientService(config.public.apiBaseUrl, () => authStore.getToken(), fetcher)
  const infoService = new InfoService(http)

  // Defaults keep the UI coherent while the /info call is in flight (or if
  // the endpoint is unreachable). Fire-and-forget: we don't block the plugin
  // on the network.
  const appName = ref('OTel Dashboard')
  const appVersion = ref('')
  const telemetryLimits = ref<TelemetryLimitsDto | null>(null)
  const queryLimits = ref<QueryLimitsDto | null>(null)

  // Per-kind retention projections, exposed as their own refs so pages
  // can pass them straight to the time-range picker without each one
  // re-deriving the same computed locally. Single source of truth here.
  const logRetentionDays = computed(() => telemetryLimits.value?.maxLogDays ?? null)
  const traceRetentionDays = computed(() => telemetryLimits.value?.maxTraceDays ?? null)
  const metricRetentionDays = computed(() => telemetryLimits.value?.maxMetricDays ?? null)
  // Same pattern for the query-API caps: one ref, two projections so
  // pages can inject `$queryMaxWindowHours` / `$queryMaxLimit` directly.
  const queryMaxWindowHours = computed(() => queryLimits.value?.maxWindowHours ?? null)
  const queryMaxLimit = computed(() => queryLimits.value?.maxLimit ?? null)

  async function refreshInfo() {
    try {
      const info = await infoService.getInfo()
      if (info.applicationName) appName.value = info.applicationName
      // Version, telemetry limits, query window cap, and storage provider
      // are all server-gated behind auth — clear them on the
      // unauthenticated leg so the sidebar v-if hides the version label
      // and time-range pickers don't show stale hints after logout.
      appVersion.value = info.version ?? ''
      telemetryLimits.value = info.telemetryLimits
      queryLimits.value = info.queryLimits
    } catch {
      /* keep the defaults */
    }
  }

  if (import.meta.client) {
    // Initial load. The login page will call refreshInfo() again after a
    // successful login so the version (auth-gated server-side) appears.
    refreshInfo()
  }

  return {
    provide: {
      authStore,
      http,
      infoService,
      logsService: new LogsService(http),
      traceService: new TraceService(http),
      metricsService: new MetricsService(http),
      serviceMapService: new ServiceMapService(http),
      dashboardService: new DashboardService(http),
      widgetService: new WidgetService(http),
      appName,
      appVersion,
      telemetryLimits,
      logRetentionDays,
      traceRetentionDays,
      metricRetentionDays,
      queryLimits,
      queryMaxWindowHours,
      queryMaxLimit,
      refreshInfo,
      demoMode: isDemo
    }
  }
})
