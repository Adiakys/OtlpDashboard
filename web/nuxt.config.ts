// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: ['@nuxt/ui'],

  // SPA only. No SSR: `nuxi generate` produces static files served by the
  // ASP.NET host from wwwroot/.
  ssr: false,

  devtools: { enabled: true },

  css: ['~/assets/css/main.css'],

  app: {
    head: {
      title: 'OpenTelemetry Dashboard',
      htmlAttrs: { lang: 'en' }
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
