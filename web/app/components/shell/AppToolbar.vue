<script setup lang="ts">
import type { ActionDescriptor, BreadcrumbItem, FilterDescriptor } from '~/types/toolbar'
import AppApplicationFilter from '~/components/form/AppApplicationFilter.vue'
import AppDateTimeRangePicker from '~/components/form/AppDateTimeRangePicker.vue'
import AppLimitSelect from '~/components/form/AppLimitSelect.vue'
import AppSeveritySelect from '~/components/form/AppSeveritySelect.vue'
import AppStatusSelect from '~/components/form/AppStatusSelect.vue'
import AppDurationFilter from '~/components/form/AppDurationFilter.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import AppAttributesFilter from '~/components/form/AppAttributesFilter.vue'
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
  <div class="flex flex-col gap-3">
    <!-- Title row: editorial display + actions far right. Asymmetric. -->
    <div class="flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
      <div class="flex flex-col min-w-0">
        <nav
          v-if="breadcrumb && breadcrumb.length"
          class="flex items-center gap-1.5 text-overline mb-1.5"
          style="color: var(--color-graphite-500);"
        >
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
              name="i-ph-caret-right"
              class="size-3"
            />
          </template>
        </nav>
        <slot name="title">
          <h1 v-if="title" class="text-headline truncate text-default">{{ title }}</h1>
        </slot>
        <p v-if="subtitle" class="text-caption truncate mt-0.5">{{ subtitle }}</p>
      </div>

      <div v-if="actions.length || $slots['actions-extra']" class="flex items-center gap-1.5">
        <template v-for="(a, idx) in actions" :key="`a-${idx}`">
          <UButton
            v-if="a.kind === 'refresh'"
            size="sm"
            color="neutral"
            variant="subtle"
            icon="i-ph-arrow-clockwise"
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
          <!-- Split button: primary on the left, caret on the right. The
               two buttons share borders (negative margin + rounded sides)
               so they read as one control. UDropdownMenu handles keyboard
               nav and focus return. -->
          <div v-else-if="a.kind === 'split'" class="inline-flex isolate">
            <UButton
              size="sm"
              :color="a.color ?? 'neutral'"
              :variant="a.variant ?? 'subtle'"
              :icon="a.icon"
              :loading="a.loading?.value"
              :disabled="a.disabled?.value"
              class="rounded-r-none transition-colors"
              @click="a.onClick"
            >
              {{ t(a.labelKey) }}
            </UButton>
            <UDropdownMenu
              :items="[a.items.map(it => ({ label: t(it.labelKey), icon: it.icon, onSelect: it.onClick }))]"
            >
              <UButton
                size="sm"
                :color="a.color ?? 'neutral'"
                :variant="a.variant ?? 'subtle'"
                icon="i-ph-caret-down"
                :disabled="a.disabled?.value || a.loading?.value"
                class="rounded-l-none border-l border-default/40 -ml-px"
                :aria-label="t('common.moreOptions')"
              />
            </UDropdownMenu>
          </div>
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

    <!-- Filters row: hairline border-top above, divide-x between groups.
         Sits in negative space, no card chrome. -->
    <div
      v-if="filters.length || $slots['filters-extra']"
      class="flex flex-wrap items-center gap-x-3 gap-y-2 pt-3"
      style="border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 18%, transparent);"
    >
      <template v-for="(f, idx) in filters" :key="idx">
        <AppApplicationFilter
          v-if="f.kind === 'application'"
          :model-value="f.modelValue.value"
          :options="f.options.value"
          :match-mode="f.matchMode?.value"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
          @update:match-mode="f.matchMode ? (f.matchMode.value = $event) : undefined"
        />
        <AppDateTimeRangePicker
          v-else-if="f.kind === 'time-range'"
          :model-value="f.modelValue.value"
          :disabled="f.disabled?.value"
          :retention-days="f.retentionDays?.value ?? null"
          :max-window-hours="f.maxWindowHours?.value ?? null"
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
        <AppAttributesFilter
          v-else-if="f.kind === 'attributes'"
          :model-value="f.modelValue.value"
          :disabled="f.disabled?.value"
          @update:model-value="f.modelValue.value = $event"
        />
      </template>

      <slot name="filters-extra" />
    </div>
  </div>
</template>
