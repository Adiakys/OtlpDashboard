<script setup lang="ts">
import type { LogRecordDto } from '~/services/types'
import { severityBucketFromNumber } from '~/types/filters'
import AppBadge from '~/components/ui/AppBadge.vue'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const props = defineProps<{ record: LogRecordDto }>()

const { t, locale } = useI18n()

function fmt(iso: string | null): string {
  return iso ? dateTimeFormat(iso, 'datetime-long', locale.value) : '·'
}

const bucket = computed(() => severityBucketFromNumber(props.record.severityNumber))

const copied = ref(false)
async function copyBody() {
  if (!props.record.body) return
  await navigator.clipboard.writeText(props.record.body)
  copied.value = true
  setTimeout(() => { copied.value = false }, 1200)
}
</script>

<template>
  <div class="space-y-5 text-sm">
    <section class="space-y-1">
      <h3 class="text-caption uppercase tracking-wide">{{ t('common.time') }}</h3>
      <p class="font-mono text-xs">{{ fmt(record.time) }}</p>
      <p v-if="record.observedTime" class="text-xs text-muted">
        {{ t('logs.detail.observedTime') }}: {{ fmt(record.observedTime) }}
      </p>
    </section>

    <section class="space-y-1">
      <h3 class="text-caption uppercase tracking-wide">{{ t('logs.col.severity') }}</h3>
      <div class="flex items-center gap-2">
        <AppBadge :tone="{ kind: 'severity', bucket }" size="sm">
          {{ record.severityText ?? record.severityNumber }}
        </AppBadge>
        <span class="text-xs text-muted">({{ record.severityNumber }})</span>
      </div>
    </section>

    <section v-if="record.body" class="space-y-1">
      <div class="flex items-center justify-between">
        <h3 class="text-caption uppercase tracking-wide">{{ t('logs.col.body') }}</h3>
        <UButton size="xs" color="neutral" variant="ghost" :icon="copied ? 'i-ph-check' : 'i-ph-copy'" @click="copyBody">
          {{ copied ? t('common.copied') : t('common.copy') }}
        </UButton>
      </div>
      <pre class="whitespace-pre-wrap break-words text-sm bg-elevated px-3 py-2 rounded-md max-h-72 overflow-auto">{{ record.body }}</pre>
    </section>

    <section v-if="record.traceId || record.spanId" class="space-y-1">
      <h3 class="text-caption uppercase tracking-wide">{{ t('logs.detail.correlation') }}</h3>
      <NuxtLink
        v-if="record.traceId"
        :to="`/traces/${record.traceId}`"
        class="inline-flex items-center gap-1.5 text-primary hover:underline font-mono text-xs"
      >
        <UIcon name="i-ph-tree-structure" class="size-3.5" />
        {{ record.traceId }}
      </NuxtLink>
      <p v-if="record.spanId" class="font-mono text-xs text-muted">
        span {{ record.spanId }}
      </p>
    </section>

    <section v-if="record.serviceName || record.scopeName" class="space-y-1">
      <h3 class="text-caption uppercase tracking-wide">{{ t('logs.detail.scope') }}</h3>
      <p v-if="record.serviceName" class="text-xs">
        <span class="text-muted">service.name</span>
        <span class="ml-2 font-mono">{{ record.serviceName }}</span>
      </p>
      <p v-if="record.scopeName" class="text-xs">
        <span class="text-muted">scope</span>
        <span class="ml-2 font-mono">
          {{ record.scopeName }}<span v-if="record.scopeVersion" class="text-muted"> @ {{ record.scopeVersion }}</span>
        </span>
      </p>
    </section>

    <section v-if="Object.keys(record.attributes).length > 0" class="space-y-1">
      <h3 class="text-caption uppercase tracking-wide">{{ t('common.attributes') }}</h3>
      <dl class="text-xs space-y-0.5">
        <div v-for="(value, key) in record.attributes" :key="key" class="flex gap-2 flex-wrap">
          <dt class="font-mono text-muted">{{ key }}</dt>
          <dd class="font-mono break-all">{{ JSON.stringify(value) }}</dd>
        </div>
      </dl>
    </section>

    <section class="space-y-1">
      <h3 class="text-caption uppercase tracking-wide">{{ t('logs.detail.resource') }}</h3>
      <p class="font-mono text-xs text-muted break-all">{{ record.resourceHash }}</p>
    </section>
  </div>
</template>
