// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: ['@nuxt/ui', '@nuxtjs/i18n'],

  // SPA only. No SSR: `nuxi generate` produces static files served by the
  // ASP.NET host from wwwroot/.
  ssr: false,

  devtools: { enabled: true },

  css: ['~/assets/css/main.css'],

  app: {
    head: {
      title: 'OpenTelemetry Dashboard',
      htmlAttrs: { lang: 'it' },
      link: [
        { rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' },
        { rel: 'alternate icon', type: 'image/x-icon', href: '/favicon.ico' }
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
