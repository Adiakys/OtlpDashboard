// https://nuxt.com/docs/api/configuration/nuxt-config

declare const process: { env: Record<string, string | undefined> }

// Build-time base URL. Defaults to '/' (the ASP.NET host serves the SPA
// from wwwroot/). The GitHub Pages demo workflow sets NUXT_APP_BASE_URL
// to '/<repo>/' so the bundle resolves under a subpath.
const APP_BASE_URL = process.env.NUXT_APP_BASE_URL || '/'

export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxtjs/i18n'],

  // SPA only. No SSR: `nuxi generate` produces static files served by the
  // ASP.NET host from wwwroot/.
  ssr: false,

  // Disable sourcemaps in production builds: they ~30% the bundle, leak
  // source structure to anyone with devtools, and are the trigger for
  // the `@tailwindcss/vite` "sourcemap is likely to be incorrect"
  // warnings (the plugin doesn't emit maps for its transforms). Dev
  // keeps full maps via Vite's defaults — this only affects `nuxi build`
  // / `nuxi generate`.
  sourcemap: {
    client: false,
    server: false
  },

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
