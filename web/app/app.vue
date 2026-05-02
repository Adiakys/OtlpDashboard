<script setup lang="ts">
// `link` is intentionally not set here: nuxt.config.ts's `app.head.link`
// already declares both the SVG favicon (`/favicon.svg`, picked by
// modern browsers) and the ICO fallback (`/favicon.ico`, picked by older
// ones). Re-declaring `rel: icon` here would replace the SVG entry and
// fall back to the rasterised ICO even on browsers that handle SVG.
useHead({
  meta: [
    { name: 'viewport', content: 'width=device-width, initial-scale=1' }
  ]
})
</script>

<template>
  <UApp>
    <NuxtLayout>
      <NuxtPage />
    </NuxtLayout>
  </UApp>
</template>
