<script setup lang="ts">
import BaseWidget from '../components/BaseWidget.vue'
import type { RecentTracesConfig, TraceSortMode } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { presetToWindow } from '../useWidgetSeries'
import type { TraceSummaryDto } from '~/services/types'

const props = defineProps<{
  config: RecentTracesConfig
  isEditing: boolean
  liveTick: number
}>()

defineEmits<{
  edit: []
  remove: []
}>()

const { t, locale } = useI18n()
const { $traceService } = useNuxtApp()

const headerTitle = computed(() =>
  props.config.title || t(WIDGET_REGISTRY['recent-traces'].titleKey)
)

const limit = computed(() => Math.max(1, Math.min(200, props.config.limit ?? 20)))
const sort = computed<TraceSortMode>(() => props.config.sort ?? 'recent')

const traces = ref<TraceSummaryDto[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
let inFlight = 0

async function load() {
  const ticket = ++inFlight
  loading.value = true
  error.value = null
  try {
    const window = presetToWindow(props.config.range)
    const response = await $traceService.listTraces({
      from: window.from,
      to: window.to,
      // Fetch enough to sort client-side without paging through.
      limit: Math.min(200, limit.value * 3),
      service: props.config.service ?? undefined
    })
    if (ticket !== inFlight) return
    traces.value = response.items
  } catch (e) {
    if (ticket === inFlight) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    if (ticket === inFlight) loading.value = false
  }
}

watch(
  () => [props.config.range, props.config.service, props.liveTick],
  load,
  { immediate: true }
)

const sorted = computed(() => {
  const items = [...traces.value]
  switch (sort.value) {
    case 'slowest':
      items.sort((a, b) => b.durationMs - a.durationMs)
      break
    case 'errors-first':
      items.sort((a, b) => {
        const ae = a.rootStatusCode === 'Error' ? 0 : 1
        const be = b.rootStatusCode === 'Error' ? 0 : 1
        if (ae !== be) return ae - be
        return new Date(b.start).getTime() - new Date(a.start).getTime()
      })
      break
    case 'recent':
    default:
      items.sort((a, b) => new Date(b.start).getTime() - new Date(a.start).getTime())
  }
  return items.slice(0, limit.value)
})

function formatTime(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleTimeString(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)} µs`
  if (ms < 1000) return `${ms.toFixed(1)} ms`
  return `${(ms / 1000).toFixed(2)} s`
}

function statusColor(code: string): 'error' | 'success' | 'neutral' {
  switch (code) {
    case 'Error': return 'error'
    case 'Ok': return 'success'
    default: return 'neutral'
  }
}

const router = useRouter()
function openTrace(traceId: string) {
  // In edit mode the user is rearranging the layout — swallow the click so
  // they don't get navigated away while dragging the grid.
  if (props.isEditing) return
  router.push(`/traces/${traceId}`)
}
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['recent-traces'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <div
      v-if="sorted.length === 0"
      class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center"
    >
      {{ t('dashboard.widgets.noData') }}
    </div>
    <div v-else class="flex-1 min-h-0 overflow-auto">
      <table class="w-full text-xs">
        <thead class="sticky top-0 bg-default border-b border-default">
          <tr class="text-left text-muted">
            <th class="px-2 py-1 font-normal">{{ t('dashboard.col.time') }}</th>
            <th class="px-2 py-1 font-normal">{{ t('dashboard.col.service') }}</th>
            <th class="px-2 py-1 font-normal">{{ t('dashboard.col.span') }}</th>
            <th class="px-2 py-1 font-normal text-right">{{ t('dashboard.col.duration') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="tr in sorted"
            :key="tr.traceId"
            class="border-b border-default/40 hover:bg-elevated/30 cursor-pointer"
            @click="openTrace(tr.traceId)"
          >
            <td class="px-2 py-1 tabular-nums whitespace-nowrap">{{ formatTime(tr.start) }}</td>
            <td class="px-2 py-1 truncate max-w-[120px]">{{ tr.serviceName ?? '—' }}</td>
            <td class="px-2 py-1 truncate max-w-[200px]">
              <UBadge
                v-if="tr.rootStatusCode === 'Error'"
                color="error"
                variant="subtle"
                size="xs"
                class="mr-1"
              >Err</UBadge>
              <UBadge
                v-else-if="tr.rootStatusCode === 'Ok'"
                :color="statusColor(tr.rootStatusCode)"
                variant="subtle"
                size="xs"
                class="mr-1"
              >Ok</UBadge>
              {{ tr.rootSpanName }}
            </td>
            <td class="px-2 py-1 text-right tabular-nums whitespace-nowrap">
              {{ formatDuration(tr.durationMs) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </BaseWidget>
</template>
