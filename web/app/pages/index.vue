<script setup lang="ts">
// Placeholder. The actual `/` → `/dashboard` (or `/login`) redirect is
// handled by `app/middleware/auth.global.ts`, which runs before this
// page mounts. Keeping the page as a no-op avoids the Suspense / layout-
// transition glitches that an `await navigateTo()` in setup used to
// trigger when SPA boot landed here under a baseURL subpath.
</script>

<template>
  <div />
</template>
