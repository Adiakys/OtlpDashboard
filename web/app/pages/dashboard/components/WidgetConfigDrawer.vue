<script setup lang="ts">
import StatConfigForm from '../configs/StatConfigForm.vue'
import LineConfigForm from '../configs/LineConfigForm.vue'
import SparklineConfigForm from '../configs/SparklineConfigForm.vue'
import GaugeConfigForm from '../configs/GaugeConfigForm.vue'
import BarGaugeConfigForm from '../configs/BarGaugeConfigForm.vue'
import PieConfigForm from '../configs/PieConfigForm.vue'
import HeatmapConfigForm from '../configs/HeatmapConfigForm.vue'
import RecentTracesConfigForm from '../configs/RecentTracesConfigForm.vue'
import LogsStreamConfigForm from '../configs/LogsStreamConfigForm.vue'
import TextConfigForm from '../configs/TextConfigForm.vue'
import { WIDGET_METADATA } from '../registry'
import type {
  LogsStreamConfig,
  MetricBarGaugeConfig,
  MetricGaugeConfig,
  MetricHeatmapConfig,
  MetricLineConfig,
  MetricPieConfig,
  MetricSparklineConfig,
  MetricStatConfig,
  RecentTracesConfig,
  TextWidgetConfig,
  WidgetConfig,
  WidgetItem
} from '../types'

const props = defineProps<{
  widget: WidgetItem
  open: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'apply': [config: WidgetConfig]
}>()

const { t } = useI18n()

// Drawer keeps a local draft so the user can tweak fields before pressing
// "Apply" (avoids clobbering layout state on every keystroke; also makes
// "Cancel" trivial — just close without emitting).
//
// JSON round-trip instead of structuredClone: structuredClone trips on Vue's
// reactive Proxies ("Proxy object could not be cloned"), and our config
// shapes are plain data with no Date / Map / etc., so the JSON path is both
// correct and sufficient.
function cloneConfig(c: WidgetConfig): WidgetConfig {
  return JSON.parse(JSON.stringify(c)) as WidgetConfig
}

const draft = ref<WidgetConfig>(cloneConfig(props.widget.config))

watch(() => props.widget.id, () => {
  draft.value = cloneConfig(props.widget.config)
})
watch(() => props.open, isOpen => {
  if (isOpen) draft.value = cloneConfig(props.widget.config)
})

function apply() {
  emit('apply', draft.value)
  emit('update:open', false)
}

function close() {
  emit('update:open', false)
}

const headerIcon = computed(() => WIDGET_METADATA[props.widget.kind].icon)
const headerTitle = computed(() => t(WIDGET_METADATA[props.widget.kind].titleKey))
</script>

<template>
  <USlideover
    :open="open"
    side="right"
    :title="headerTitle"
    @update:open="(v) => emit('update:open', v)"
  >
    <template #header>
      <div class="flex items-center gap-2">
        <UIcon :name="headerIcon" class="size-4 text-primary" />
        <span class="text-sm font-medium">{{ headerTitle }}</span>
      </div>
    </template>

    <template #body>
      <div class="h-full flex flex-col min-h-0">
        <StatConfigForm
          v-if="widget.kind === 'metric-stat'"
          :model-value="draft as MetricStatConfig"
          @update:model-value="(v) => draft = v"
        />
        <LineConfigForm
          v-else-if="widget.kind === 'metric-line'"
          :model-value="draft as MetricLineConfig"
          @update:model-value="(v) => draft = v"
        />
        <SparklineConfigForm
          v-else-if="widget.kind === 'metric-sparkline'"
          :model-value="draft as MetricSparklineConfig"
          @update:model-value="(v) => draft = v"
        />
        <GaugeConfigForm
          v-else-if="widget.kind === 'metric-gauge'"
          :model-value="draft as MetricGaugeConfig"
          @update:model-value="(v) => draft = v"
        />
        <BarGaugeConfigForm
          v-else-if="widget.kind === 'metric-bar-gauge'"
          :model-value="draft as MetricBarGaugeConfig"
          @update:model-value="(v) => draft = v"
        />
        <PieConfigForm
          v-else-if="widget.kind === 'metric-pie'"
          :model-value="draft as MetricPieConfig"
          @update:model-value="(v) => draft = v"
        />
        <HeatmapConfigForm
          v-else-if="widget.kind === 'metric-heatmap'"
          :model-value="draft as MetricHeatmapConfig"
          @update:model-value="(v) => draft = v"
        />
        <RecentTracesConfigForm
          v-else-if="widget.kind === 'recent-traces'"
          :model-value="draft as RecentTracesConfig"
          @update:model-value="(v) => draft = v"
        />
        <LogsStreamConfigForm
          v-else-if="widget.kind === 'logs-stream'"
          :model-value="draft as LogsStreamConfig"
          @update:model-value="(v) => draft = v"
        />
        <TextConfigForm
          v-else-if="widget.kind === 'text'"
          :model-value="draft as TextWidgetConfig"
          @update:model-value="(v) => draft = v"
        />
      </div>
    </template>

    <template #footer>
      <div class="flex items-center justify-end gap-2 w-full">
        <UButton color="neutral" variant="ghost" @click="close">
          {{ t('dashboard.actions.cancel') }}
        </UButton>
        <UButton color="primary" @click="apply">
          {{ t('dashboard.config.apply') }}
        </UButton>
      </div>
    </template>
  </USlideover>
</template>
