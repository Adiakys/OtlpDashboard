<script setup lang="ts">
import BaseWidget from '../components/BaseWidget.vue'
import type { LogSeverityFilter, LogsStreamConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { presetToWindow } from '../useWidgetSeries'
import type { LogRecordDto } from '~/services/types'

const props = defineProps<{
  config: LogsStreamConfig
  isEditing: boolean
  liveTick: number
}>()

defineEmits<{
  edit: []
  remove: []
}>()

const { t, locale } = useI18n()
const { $logsService } = useNuxtApp()
const router = useRouter()

const headerTitle = computed(() =>
  props.config.title || t(WIDGET_REGISTRY['logs-stream'].titleKey)
)

const limit = computed(() => Math.max(1, Math.min(500, props.config.limit ?? 50)))
const minSeverityNumber = computed<number>(() => {
  switch (props.config.minSeverity) {
    case 'info': return 9
    case 'warn': return 13
    case 'error': return 17
    case 'fatal': return 21
    default: return 0
  }
})

const logs = ref<LogRecordDto[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
let inFlight = 0

async function load() {
  const ticket = ++inFlight
  loading.value = true
  error.value = null
  try {
    const window = presetToWindow(props.config.range)
    const response = await $logsService.listLogs({
      from: window.from,
      to: window.to,
      limit: limit.value,
      service: props.config.service ?? undefined,
      // Server-side severity filter: the column is indexed, so cutting at
      // Warn / Error here avoids streaming the noisy Info tail. Zero means
      // "no cutoff", which the server treats as a no-op.
      minSeverity: minSeverityNumber.value > 0 ? minSeverityNumber.value : undefined
    })
    if (ticket !== inFlight) return
    logs.value = response.items
  } catch (e) {
    if (ticket === inFlight) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    if (ticket === inFlight) loading.value = false
  }
}

watch(
  () => [props.config.range, props.config.service, props.config.limit, props.config.minSeverity, props.liveTick],
  load,
  { immediate: true }
)

const filtered = computed(() => {
  return logs.value
    .slice(0, limit.value)
    // Most-recent first.
    .sort((a, b) => new Date(b.time).getTime() - new Date(a.time).getTime())
})

function severityBadgeColor(n: number): 'neutral' | 'info' | 'warning' | 'error' {
  if (n >= 17) return 'error'
  if (n >= 13) return 'warning'
  if (n >= 9) return 'info'
  return 'neutral'
}

function severityLabel(l: LogRecordDto): string {
  if (l.severityText && l.severityText.length > 0) return l.severityText
  if (l.severityNumber >= 21) return 'FATAL'
  if (l.severityNumber >= 17) return 'ERROR'
  if (l.severityNumber >= 13) return 'WARN'
  if (l.severityNumber >= 9) return 'INFO'
  if (l.severityNumber >= 5) return 'DEBUG'
  if (l.severityNumber >= 1) return 'TRACE'
  return '·'
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

function bodyText(l: LogRecordDto): string {
  return l.body ?? ''
}

function openTrace(traceId: string | null) {
  if (props.isEditing) return
  if (!traceId) return
  router.push(`/traces/${traceId}`)
}
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['logs-stream'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <div
      v-if="filtered.length === 0"
      class="flex-1 min-h-0 flex items-center justify-center text-mono-sm text-muted px-3 text-center"
    >
      {{ t('dashboard.widgets.noData') }}
    </div>
    <div v-else class="flex-1 min-h-0 overflow-auto vellum-logs-stream">
      <div
        v-for="(l, i) in filtered"
        :key="i"
        class="vellum-log-row flex items-start gap-2.5 px-3 py-1.5"
        :class="{ 'cursor-pointer': l.traceId }"
        @click="openTrace(l.traceId)"
      >
        <span class="text-mono-sm text-muted shrink-0 mt-0.5" style="font-variant-numeric: tabular-nums;">{{ formatTime(l.time) }}</span>
        <UBadge
          :color="severityBadgeColor(l.severityNumber)"
          variant="subtle"
          size="xs"
          class="shrink-0"
          :class="'vellum-badge-mono'"
        >{{ severityLabel(l) }}</UBadge>
        <span v-if="l.serviceName" class="text-mono-sm text-muted shrink-0 truncate max-w-[100px]">{{ l.serviceName }}</span>
        <span class="text-mono-sm text-default break-all leading-snug">{{ bodyText(l) }}</span>
      </div>
    </div>
  </BaseWidget>
</template>

<style scoped>
.vellum-logs-stream > .vellum-log-row + .vellum-log-row {
  border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 10%, transparent);
}
.vellum-log-row:hover {
  background: color-mix(in oklab, var(--color-graphite-500) 6%, transparent);
}
:deep(.vellum-badge-mono) {
  font-family: var(--font-mono);
  letter-spacing: 0.05em;
  text-transform: uppercase;
  font-weight: 500;
}
</style>
