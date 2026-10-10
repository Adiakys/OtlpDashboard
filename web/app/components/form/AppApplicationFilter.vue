<script setup lang="ts">
/**
 * Multi-select picker over `service.name` values, with a collapsible list of
 * `service.instance.id` values under each service that reports more than one.
 * The model mixes service names and `service:instanceId` entries, like the
 * `services=` API parameter. Three states: "all" (no filter, empty model and
 * noneSelected false), "none" (noneSelected true, every row hidden) and an
 * explicit subset. The selection rules live in `~/lib/applicationSelection`.
 */
import type { ServiceInstancesDto } from '~/services/types'
import {
  allState,
  isInstanceChecked,
  onlyInstance,
  onlyService,
  selectedInstanceCount,
  serviceState,
  summarize,
  toggleAll as toggleAllSelection,
  toggleInstance as toggleInstanceSelection,
  toggleService as toggleServiceSelection,
  toList,
  toSelection,
  type ApplicationSelection,
  type CheckState
} from '~/lib/applicationSelection'

const props = defineProps<{
  modelValue: string[]
  options: ServiceInstancesDto[]
  noneSelected?: boolean
  /** Optional any-span / root match mode binding; the toggle row is
   *  hidden when omitted. */
  matchMode?: 'root' | 'any'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string[]]
  'update:noneSelected': [value: boolean]
  'update:matchMode': [value: 'root' | 'any']
}>()

const { t } = useI18n()
const isOpen = ref(false)
const expanded = ref(new Set<string>())

const selection = computed(() => toSelection(props.modelValue, props.noneSelected === true, props.options))

function sameList(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((v, i) => v === b[i])
}

function apply(next: ApplicationSelection) {
  const list = toList(next)
  if (!sameList(list, props.modelValue)) emit('update:modelValue', list)
  if (next.none !== (props.noneSelected === true)) emit('update:noneSelected', next.none)
}

function toggleExpanded(service: string) {
  const next = new Set(expanded.value)
  if (next.has(service)) next.delete(service)
  else next.add(service)
  expanded.value = next
}

const toggleAll = () => apply(toggleAllSelection(selection.value))
const toggleService = (service: string) => apply(toggleServiceSelection(selection.value, props.options, service))
const toggleInstance = (service: string, instance: string) =>
  apply(toggleInstanceSelection(selection.value, props.options, service, instance))
const selectOnlyService = (service: string) => apply(onlyService(props.options, service))
const selectOnlyInstance = (service: string, instance: string) => apply(onlyInstance(props.options, service, instance))

function checkIcon(state: CheckState): string {
  if (state === 'checked') return 'i-ph-check-square'
  if (state === 'partial') return 'i-ph-minus-square'
  return 'i-ph-square'
}

function ariaChecked(state: CheckState): 'true' | 'false' | 'mixed' {
  if (state === 'partial') return 'mixed'
  return state === 'checked' ? 'true' : 'false'
}

const buttonLabel = computed(() => {
  const summary = summarize(selection.value, props.options)
  switch (summary.kind) {
    case 'none': return t('filter.applicationNone')
    case 'all': return t('filter.applicationAll')
    case 'service': return summary.service
    case 'instance': return `${summary.service} · ${summary.instance}`
    case 'instances': return t('filter.applicationInstancesOf', { service: summary.service, count: summary.count })
    case 'services': return t('filter.applicationCount', { count: summary.count })
  }
})

const supportsMatchMode = computed(() => props.matchMode !== undefined)
const isAnySpan = computed(() => props.matchMode === 'any')

function toggleMatchMode() {
  emit('update:matchMode', isAnySpan.value ? 'root' : 'any')
}
</script>

<template>
  <UPopover v-model:open="isOpen" :content="{ align: 'start' }">
    <button
      type="button"
      class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      :disabled="disabled"
    >
      <UIcon name="i-ph-stack" class="size-4 text-muted" />
      <span
        class="truncate max-w-[14rem]"
        style="font-family: var(--font-mono); font-size: 12px; letter-spacing: 0.04em;"
      >{{ buttonLabel }}</span>
      <UIcon name="i-ph-caret-down" class="size-3.5 text-muted" />
    </button>

    <template #content>
      <div class="p-2 w-72 space-y-0.5 max-h-96 overflow-y-auto">
        <button
          type="button"
          role="checkbox"
          :aria-checked="ariaChecked(allState(selection))"
          class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm hover:bg-elevated transition-colors"
          @click="toggleAll"
        >
          <UIcon
            :name="checkIcon(allState(selection))"
            class="size-4 shrink-0"
            :class="allState(selection) === 'unchecked' ? 'text-muted' : 'text-primary'"
          />
          <span class="font-medium">{{ t('filter.applicationAll') }}</span>
        </button>
        <div
          v-if="options.length > 0"
          class="my-1 border-t border-default"
        />

        <template v-for="opt in options" :key="opt.service">
          <div class="group flex items-center rounded-md hover:bg-elevated transition-colors">
            <button
              type="button"
              role="checkbox"
              :aria-checked="ariaChecked(serviceState(selection, opt))"
              class="flex-1 min-w-0 flex items-center gap-2 px-2 py-1.5 text-left"
              @click="toggleService(opt.service)"
            >
              <UIcon
                :name="checkIcon(serviceState(selection, opt))"
                class="size-4 shrink-0"
                :class="serviceState(selection, opt) === 'unchecked' ? 'text-muted' : 'text-primary'"
              />
              <span class="truncate font-mono text-xs" :title="opt.service">{{ opt.service }}</span>
            </button>
            <button
              type="button"
              class="shrink-0 px-1.5 py-1 rounded text-[11px] text-muted hover:text-primary opacity-0 group-hover:opacity-100 focus-visible:opacity-100 transition-opacity"
              @click="selectOnlyService(opt.service)"
            >{{ t('filter.applicationOnly') }}</button>
            <button
              v-if="opt.instances.length > 1"
              type="button"
              class="shrink-0 mr-1 inline-flex items-center gap-1 px-1.5 py-1 rounded text-[11px] tabular-nums text-muted hover:text-default hover:bg-accented transition-colors"
              :aria-expanded="expanded.has(opt.service)"
              :aria-label="t('filter.applicationToggleInstances', { service: opt.service })"
              :title="t('filter.applicationInstanceCount', { count: opt.instances.length })"
              @click="toggleExpanded(opt.service)"
            >
              <span :class="serviceState(selection, opt) === 'partial' ? 'text-primary' : ''">
                {{ serviceState(selection, opt) === 'partial'
                  ? `${selectedInstanceCount(selection, opt)}/${opt.instances.length}`
                  : opt.instances.length }}
              </span>
              <UIcon
                name="i-ph-caret-right"
                class="size-3 transition-transform"
                :class="expanded.has(opt.service) ? 'rotate-90' : ''"
              />
            </button>
          </div>

          <div
            v-if="opt.instances.length > 1 && expanded.has(opt.service)"
            class="ml-4 pl-1.5 border-l border-default space-y-0.5"
          >
            <div
              v-for="inst in opt.instances"
              :key="inst"
              class="group flex items-center rounded-md hover:bg-elevated transition-colors"
            >
              <button
                type="button"
                role="checkbox"
                :aria-checked="isInstanceChecked(selection, opt.service, inst)"
                class="flex-1 min-w-0 flex items-center gap-2 px-2 py-1 text-left"
                @click="toggleInstance(opt.service, inst)"
              >
                <UIcon
                  :name="isInstanceChecked(selection, opt.service, inst) ? 'i-ph-check-square' : 'i-ph-square'"
                  class="size-3.5 shrink-0"
                  :class="isInstanceChecked(selection, opt.service, inst) ? 'text-primary' : 'text-muted'"
                />
                <span class="truncate font-mono text-[11px] text-toned" :title="inst">{{ inst }}</span>
              </button>
              <button
                type="button"
                class="shrink-0 mr-1 px-1.5 py-0.5 rounded text-[11px] text-muted hover:text-primary opacity-0 group-hover:opacity-100 focus-visible:opacity-100 transition-opacity"
                @click="selectOnlyInstance(opt.service, inst)"
              >{{ t('filter.applicationOnly') }}</button>
            </div>
          </div>
        </template>

        <div
          v-if="options.length === 0"
          class="px-2 py-1.5 text-xs text-muted"
        >{{ t('filter.applicationEmpty') }}</div>

        <template v-if="supportsMatchMode">
          <div class="my-1 border-t border-default" />
          <button
            type="button"
            class="w-full flex items-start gap-2 px-2 py-1.5 rounded-md text-left hover:bg-elevated transition-colors"
            :title="t('filter.applicationMatchModeHint')"
            @click="toggleMatchMode"
          >
            <UIcon
              :name="isAnySpan ? 'i-ph-check-square' : 'i-ph-square'"
              class="size-4 mt-0.5 shrink-0"
              :class="isAnySpan ? 'text-primary' : 'text-muted'"
            />
            <span class="flex flex-col">
              <span class="font-medium text-sm">{{ t('filter.applicationMatchModeAny') }}</span>
              <span class="text-xs text-muted">{{ t('filter.applicationMatchModeHint') }}</span>
            </span>
          </button>
        </template>
      </div>
    </template>
  </UPopover>
</template>
