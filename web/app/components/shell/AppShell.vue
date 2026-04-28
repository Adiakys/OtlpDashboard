<script setup lang="ts">
import AppSidebar from './AppSidebar.vue'
</script>

<template>
  <div class="h-screen w-screen flex overflow-hidden bg-default text-default">
    <AppSidebar />
    <main class="flex-1 min-w-0 min-h-0 flex flex-col overflow-hidden">
      <slot />
    </main>
  </div>
</template>
