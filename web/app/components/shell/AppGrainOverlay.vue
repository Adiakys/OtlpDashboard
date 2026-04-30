<script setup lang="ts">
import grainUrl from '~/assets/svg/grain.svg?url'
</script>

<template>
  <div
    class="grain-overlay"
    :style="{ backgroundImage: `url(${grainUrl})` }"
    aria-hidden="true"
  />
</template>

<style scoped>
/* Fixed full-viewport noise. Sits above page chrome but below dropdowns/modals
   (UPopover/USlideover use higher portal z-indices). Kept fully passive:
   pointer-events: none and a low opacity so the texture is felt, not seen. */
.grain-overlay {
  position: fixed;
  inset: 0;
  z-index: 1;
  pointer-events: none;
  background-repeat: repeat;
  background-size: 256px 256px;
  opacity: 0.025;
}

:global(html.dark) .grain-overlay {
  opacity: 0.018;
}

@media (prefers-reduced-transparency: reduce) {
  .grain-overlay { display: none; }
}
</style>
