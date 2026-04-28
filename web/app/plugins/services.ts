import { AuthStore } from '~/services/AuthStore'
import { HttpClientService } from '~/services/HttpClientService'
import { InfoService } from '~/services/InfoService'
import { LogsService } from '~/services/LogsService'
import { MetricsService } from '~/services/MetricsService'
import { TraceService } from '~/services/TraceService'

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
export default defineNuxtPlugin(() => {
  const config = useRuntimeConfig()
  const authStore = new AuthStore()

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

  const fetcher = $fetch.create({
    onResponseError({ response }) {
      if (response?.status !== 401) return
      // Skip the redirect if we're already on /login so the login form can
      // surface the 401 locally (wrong password) without bouncing forever.
      if (import.meta.client && window.location.pathname === '/login') return

      authStore.clear()

      if (import.meta.client) {
        const next = window.location.pathname + window.location.search
        navigateTo({ path: '/login', query: { next } }, { replace: true })
      }
    }
  })

  const http = new HttpClientService(config.public.apiBaseUrl, () => authStore.getToken(), fetcher)
  const infoService = new InfoService(http)

  // Defaults keep the UI coherent while the /info call is in flight (or if
  // the endpoint is unreachable). Fire-and-forget: we don't block the plugin
  // on the network.
  const appName = ref('OTel Dashboard')
  const appVersion = ref('')

  async function refreshInfo() {
    try {
      const info = await infoService.getInfo()
      if (info.applicationName) appName.value = info.applicationName
      // Version is null when unauthenticated — clear the ref so the sidebar
      // v-if hides the version label for logged-out users.
      appVersion.value = info.version ?? ''
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
      appName,
      appVersion,
      refreshInfo
    }
  }
})
