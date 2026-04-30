<script setup lang="ts">
withDefaults(defineProps<{
  icon?: string
  title: string
  description?: string
}>(), {
  icon: 'i-ph-tray'
})
</script>

<template>
  <div class="vellum-empty flex-1 min-h-0 relative overflow-hidden">
    <!-- Hairline grid backdrop, blueprint-like atmosphere. -->
    <div class="vellum-empty__grid" aria-hidden="true" />

    <div class="relative flex flex-col items-start text-left gap-4 max-w-[55ch] mx-auto px-6 pt-[12vh] pb-10">
      <UIcon
        :name="icon"
        class="size-8 shrink-0"
        style="color: var(--color-graphite-500);"
      />
      <h3 class="text-headline text-default">
        {{ title }}
      </h3>
      <p v-if="description" class="text-body text-muted">
        {{ description }}
      </p>
      <div v-if="$slots.actions" class="mt-2">
        <slot name="actions" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.vellum-empty__grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(to right, color-mix(in oklab, var(--color-graphite-500) 8%, transparent) 1px, transparent 1px),
    linear-gradient(to bottom, color-mix(in oklab, var(--color-graphite-500) 8%, transparent) 1px, transparent 1px);
  background-size: 32px 32px;
  mask-image: radial-gradient(ellipse at center, black 0%, transparent 75%);
  -webkit-mask-image: radial-gradient(ellipse at center, black 0%, transparent 75%);
  pointer-events: none;
}
</style>
