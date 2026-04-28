<script setup lang="ts">
import type { ActionDescriptor, BreadcrumbItem, FilterDescriptor } from '~/types/toolbar'
import AppApplicationFilter from '~/components/form/AppApplicationFilter.vue'
import AppDateTimeRangePicker from '~/components/form/AppDateTimeRangePicker.vue'
import AppLimitSelect from '~/components/form/AppLimitSelect.vue'
import AppSeveritySelect from '~/components/form/AppSeveritySelect.vue'
import AppStatusSelect from '~/components/form/AppStatusSelect.vue'
import AppDurationFilter from '~/components/form/AppDurationFilter.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import AppLiveToggle from '~/components/ui/AppLiveToggle.vue'

withDefaults(defineProps<{
  title?: string
  subtitle?: string
  filters?: FilterDescriptor[]
  actions?: ActionDescriptor[]
  breadcrumb?: BreadcrumbItem[]
}>(), {
  filters: () => [],
  actions: () => []
})

const { t } = useI18n()
</script>

<template>
  <div class="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
    <div class="flex flex-col min-w-0">
      <nav v-if="breadcrumb && breadcrumb.length" class="flex items-center gap-1 text-xs text-muted mb-1">
        <template v-for="(item, idx) in breadcrumb" :key="idx">
          <NuxtLink
            v-if="item.to"
            :to="item.to"
            class="inline-flex items-center gap-1 hover:text-default transition-colors"
          >
            <UIcon v-if="item.icon" :name="item.icon" class="size-3" />
            {{ item.labelKey ? t(item.labelKey) : item.label }}
          </NuxtLink>
          <span v-else class="inline-flex items-center gap-1 text-default">
            <UIcon v-if="item.icon" :name="item.icon" class="size-3" />
            {{ item.labelKey ? t(item.labelKey) : item.label }}
          </span>
          <UIcon
            v-if="idx < breadcrumb.length - 1"
            name="i-lucide-chevron-right"
            class="size-3"
          />
        </template>
      </nav>
      <slot name="title">
        <h1 v-if="title" class="text-title truncate">{{ title }}</h1>
      </slot>
      <p v-if="subtitle" class="text-caption truncate mt-0.5">{{ subtitle }}</p>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <template v-for="(f, idx) in filters" :key="idx">
        <AppApplicationFilter
          v-if="f.kind === 'application'"
          :model-value="f.modelValue.value"
          :options="f.options.value"
          :include-all="f.includeAll ?? true"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
        <AppDateTimeRangePicker
          v-else-if="f.kind === 'time-range'"
          :model-value="f.modelValue.value"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
        <AppLimitSelect
          v-else-if="f.kind === 'limit'"
          :model-value="f.modelValue.value"
          :options="f.options"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
        <AppSeveritySelect
          v-else-if="f.kind === 'severity'"
          :model-value="f.modelValue.value"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
        <AppStatusSelect
          v-else-if="f.kind === 'status'"
          :model-value="f.modelValue.value"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
        <AppDurationFilter
          v-else-if="f.kind === 'duration'"
          :model-value="f.modelValue.value"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
        <AppSearchInput
          v-else-if="f.kind === 'search'"
          :model-value="f.modelValue.value"
          :placeholder="f.placeholder"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
      </template>

      <slot name="filters-extra" />

      <div v-if="actions.length || $slots['actions-extra']" class="flex items-center gap-2 pl-1 ml-1 border-l border-default">
        <template v-for="(a, idx) in actions" :key="`a-${idx}`">
          <UButton
            v-if="a.kind === 'refresh'"
            size="sm"
            color="neutral"
            variant="subtle"
            icon="i-lucide-refresh-cw"
            :loading="a.loading.value"
            :disabled="a.disabled?.value"
            class="transition-colors"
            @click="a.onClick"
          >
            {{ t('common.refresh') }}
          </UButton>
          <AppLiveToggle
            v-else-if="a.kind === 'live'"
            :is-live="a.isLive.value"
            @toggle="a.onToggle"
          />
          <UButton
            v-else
            size="sm"
            :color="a.color ?? 'neutral'"
            :variant="a.variant ?? 'subtle'"
            :icon="a.icon"
            :loading="a.loading?.value"
            :disabled="a.disabled?.value"
            class="transition-colors"
            @click="a.onClick"
          >
            {{ t(a.labelKey) }}
          </UButton>
        </template>

        <slot name="actions-extra" />
      </div>
    </div>
  </div>
</template>
