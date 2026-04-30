<script setup lang="ts">
defineProps<{
  isLive: boolean
}>()

defineEmits<{ toggle: [] }>()

const { t } = useI18n()
</script>

<template>
  <button
    type="button"
    class="vellum-live"
    :class="isLive ? 'vellum-live--on' : 'vellum-live--off'"
    @click="$emit('toggle')"
  >
    <span
      class="vellum-live__dot"
      :class="isLive ? 'vellum-pulse' : ''"
      aria-hidden="true"
    />
    <span class="text-overline">{{ isLive ? t('common.live') : t('common.goLive') }}</span>
  </button>
</template>

<style scoped>
.vellum-live {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.3125rem 0.75rem;
  border-radius: var(--radius-pill);
  border: 1px solid;
  font-family: var(--font-sans);
  transition:
    background-color var(--t-fast) var(--ease-out),
    border-color var(--t-fast) var(--ease-out),
    color var(--t-fast) var(--ease-out),
    transform var(--t-instant) var(--ease-out);
}
.vellum-live:active:not(:disabled) {
  transform: translateY(1px);
}

.vellum-live--off {
  border-color: color-mix(in oklab, var(--color-graphite-500) 22%, transparent);
  color: var(--ui-text-muted);
  background: transparent;
}
.vellum-live--off:hover {
  color: var(--ui-text);
  border-color: color-mix(in oklab, var(--color-graphite-500) 35%, transparent);
}

.vellum-live--on {
  border-color: color-mix(in oklab, var(--color-ember-500) 35%, transparent);
  background: color-mix(in oklab, var(--color-ember-500) 10%, transparent);
  color: var(--color-ember-700);
}
:global(html.dark) .vellum-live--on {
  color: var(--color-ember-300);
}

.vellum-live__dot {
  display: inline-block;
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 9999px;
  background: color-mix(in oklab, var(--color-graphite-500) 60%, transparent);
}
.vellum-live--on .vellum-live__dot {
  background: var(--color-ember-500);
}
</style>
