<script setup lang="ts">
// Landing redirect — root sends users to the dashboard home.
definePageMeta({ validate: () => true })
await navigateTo('/dashboard', { redirectCode: 302, replace: true })
</script>

<template>
  <div />
</template>
