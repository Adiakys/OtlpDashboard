// https://nuxt.com/docs/api/configuration/nuxt-config

declare const process: { env: Record<string, string | undefined> }

// Build-time base URL. Defaults to '/' (the ASP.NET host serves the SPA
// from wwwroot/). The GitHub Pages demo workflow sets NUXT_APP_BASE_URL
// to '/<repo>/' so the bundle resolves under a subpath.
const APP_BASE_URL = process.env.NUXT_APP_BASE_URL || '/'

// Nuxt UI renders its default icons from node_modules, where `scan` can't see
// them; without this they'd hit the Iconify API at runtime.
function bundleNuxtUiIcons(_options: unknown, nuxt: any) {
  nuxt.hook('icon:clientBundleIcons', (icons: Set<string>) => {
    const uiIcons = nuxt.options.appConfig?.ui?.icons ?? {}
    for (const value of Object.values(uiIcons)) {
      if (typeof value !== 'string') continue
      const id = value.replace(/^i-/, '')
      const dash = id.indexOf('-')
      if (dash === -1) continue
      icons.add(`${id.slice(0, dash)}:${id.slice(dash + 1)}`)
    }
  })
}

export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxtjs/i18n', bundleNuxtUiIcons],

  // SPA only. No SSR: `nuxi generate` produces static files served by the
  // ASP.NET host from wwwroot/.
  ssr: false,

  devtools: { enabled: true },

  css: ['~/assets/css/main.css'],

  // `app.baseURL` is the root the static bundle is served from. Favicon
  // hrefs include the base URL so they resolve under any subpath (Nuxt
  // doesn't auto-prepend the base URL to head.link entries).
  app: {
    baseURL: APP_BASE_URL,
    head: {
      title: 'OpenTelemetry Dashboard',
      htmlAttrs: { lang: 'it' },
      link: [
        { rel: 'icon', type: 'image/svg+xml', href: `${APP_BASE_URL}favicon.svg` },
        { rel: 'alternate icon', type: 'image/x-icon', href: `${APP_BASE_URL}favicon.ico` }
      ]
    }
  },

  colorMode: {
    preference: 'system',
    fallback: 'dark',
    classSuffix: '',
    storageKey: 'oteldash-color-mode'
  },

  icon: {
    fallbackToApi: true,
    clientBundle: {
      // .ts: icon names also live as string data in .ts catalogs, which the
      // default scan globs skip.
      scan: {
        globInclude: ['**/*.{vue,jsx,tsx,ts,md,mdc,mdx,yml,yaml}']
      },
      sizeLimitKb: 512
    }
  },

  i18n: {
    strategy: 'no_prefix',
    defaultLocale: 'it',
    locales: [
      { code: 'it', name: 'Italiano', file: 'it.json' },
      { code: 'en', name: 'English', file: 'en.json' }
    ],
    detectBrowserLanguage: {
      useCookie: true,
      cookieKey: 'oteldash-locale',
      redirectOn: 'no prefix',
      fallbackLocale: 'en'
    },
    // The default emits absolute filesystem paths into the bundled
    // payload (visible in `index.html` as `/home/<user>/.../i18n/...`),
    // leaking the build host's username and repo layout to every
    // visitor. 'relative' surfaces only the path within the project.
    experimental: {
      generatedLocaleFilePathFormat: 'relative'
    }
  },

  // Same-origin path. In dev, proxied to :4318 via nitro.devProxy below.
  // In prod, same-origin because served from the ASP.NET host.
  runtimeConfig: {
    public: {
      apiBaseUrl: '/api'
    }
  },

  nitro: {
    devProxy: {
      '/api': {
        target: 'http://localhost:4318/api',
        changeOrigin: true
      }
    }
  },

  compatibilityDate: '2025-01-15'
})
