import { computed, ref, type Ref } from 'vue'
import type { PackDto, PackIconDto } from '~/services/types'

/**
 * Returns a `(serviceName) => iconUrl | null` resolver built from the
 * server-supplied pack catalog. Pack-load order (the order
 * `/v1/packs` returns) defines priority across packs; declaration
 * order inside `pack.icons` and `icon.match` defines priority within
 * a pack and an icon. First hit wins.
 *
 * Lives in the service-map composables folder because that's the one
 * page rendering icons today; if another consumer arrives we can lift
 * this into a shared composable without changing the contract.
 */
export function useIconResolver(packs: Ref<readonly PackDto[]>) {
  const regexCache = new Map<string, RegExp | null>()
  // The pack DTO ships imageUrl as a root-absolute path
  // (e.g. /icons/default/postgres/postgres.svg or /api/v1/packs/.../assets/...).
  // Under a subpath deploy (Nuxt's app.baseURL like /OtlpDashboard/) we have
  // to fold the base in front of those paths or the browser resolves them
  // against the domain root and 404s.
  const baseURL = resolveBaseURL()

  // Pre-flatten `(pack, icon)` tuples so the hot resolve path is one
  // linear walk instead of two nested loops.
  const flat = computed<Array<{ icon: PackIconDto }>>(() => {
    const out: Array<{ icon: PackIconDto }> = []
    for (const pack of packs.value) {
      for (const icon of pack.icons ?? []) out.push({ icon })
    }
    return out
  })

  function withBaseURL(url: string): string {
    // Only rewrite root-absolute paths. External URLs (http://, https://,
    // protocol-relative `//`, data: …) pass through untouched.
    if (!url.startsWith('/') || url.startsWith('//')) return url
    if (baseURL === '/') return url
    return (baseURL + url.replace(/^\/+/, '')).replace(/\/{2,}/g, '/')
  }

  function compile(pattern: string): RegExp | null {
    if (regexCache.has(pattern)) return regexCache.get(pattern)!
    let compiled: RegExp | null = null
    try {
      compiled = new RegExp(pattern)
    } catch {
      // A bad pattern in a third-party pack manifest shouldn't crash
      // the service-map page; silently disable that matcher and move
      // on. The backend parser already rejects invalid patterns at
      // load time, so this only fires for hand-edited data.
      compiled = null
    }
    regexCache.set(pattern, compiled)
    return compiled
  }

  function matches(icon: PackIconDto, service: string): boolean {
    for (const entry of icon.match) {
      if (entry.serviceName != null && entry.serviceName === service) return true
      if (entry.namePattern != null) {
        const re = compile(entry.namePattern)
        if (re && re.test(service)) return true
      }
    }
    return false
  }

  function resolve(service: string | null | undefined): string | null {
    if (!service) return null
    for (const entry of flat.value) {
      if (matches(entry.icon, service)) return withBaseURL(entry.icon.imageUrl)
    }
    return null
  }

  return { resolve }
}

/** Convenience wrapper for non-reactive callers (tests, derived
 *  data). */
export function buildIconResolver(packs: readonly PackDto[]) {
  const ref0 = ref(packs) as unknown as Ref<readonly PackDto[]>
  return useIconResolver(ref0)
}

/** Reads <c>app.baseURL</c> via Nuxt's runtime config when available,
 *  falling back to <c>'/'</c> when the composable runs outside a Nuxt
 *  context (vitest unit tests, ad-hoc Node usage). The auto-import is
 *  declared by <c>@nuxt/schema</c> at type-check time but isn't injected
 *  into plain vitest workers — guard with a runtime check rather than
 *  forcing every test to mock the global. */
function resolveBaseURL(): string {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const g = globalThis as any
  const fn = typeof g.useRuntimeConfig === 'function'
    ? g.useRuntimeConfig
    : null
  if (fn === null) return '/'
  try {
    const raw = fn().app?.baseURL ?? '/'
    return raw.replace(/\/+$/, '/')
  } catch {
    return '/'
  }
}
