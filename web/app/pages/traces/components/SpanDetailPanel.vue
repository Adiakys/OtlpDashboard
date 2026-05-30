<script setup lang="ts">
import type { SpanDto } from '~/services/types'
import AppBadge from '~/components/ui/AppBadge.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const props = defineProps<{ span: SpanDto | null }>()

const { t, locale } = useI18n()

function fmtTime(iso: string): string {
  return dateTimeFormat(iso, 'datetime-long', locale.value)
}

function fmtDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

const hasAttributes = computed(() => props.span && Object.keys(props.span.attributes).length > 0)
</script>

<template>
  <div class="h-full overflow-y-auto">
    <Transition name="fade" mode="out-in">
      <div v-if="span" :key="span.spanId" class="p-5 space-y-5 text-sm">
        <header class="space-y-2">
          <h2 class="text-title break-all">{{ span.name }}</h2>
          <div class="flex items-center gap-2 flex-wrap text-xs text-muted">
            <span>{{ span.kind }}</span>
            <span>·</span>
            <span class="font-mono">{{ fmtDuration(span.durationMs) }}</span>
            <AppBadge :tone="{ kind: 'trace-status', status: span.statusCode }" size="xs">
              {{ span.statusCode }}
            </AppBadge>
          </div>
          <p v-if="span.statusMessage" class="text-xs text-error">
            {{ span.statusMessage }}
          </p>
        </header>

        <section class="space-y-1">
          <h3 class="text-caption uppercase tracking-wide">{{ t('common.time') }}</h3>
          <p class="font-mono text-xs">{{ fmtTime(span.start) }} → {{ fmtTime(span.end) }}</p>
        </section>

        <section class="space-y-1">
          <h3 class="text-caption uppercase tracking-wide">IDs</h3>
          <p class="font-mono text-xs text-muted break-all">span: {{ span.spanId }}</p>
          <p v-if="span.parentSpanId" class="font-mono text-xs text-muted break-all">
            parent: {{ span.parentSpanId }}
          </p>
        </section>

        <section v-if="span.serviceName || span.scopeName" class="space-y-1">
          <h3 class="text-caption uppercase tracking-wide">{{ t('logs.detail.scope') }}</h3>
          <p v-if="span.serviceName" class="text-xs">
            <span class="text-muted">service.name</span>
            <span class="ml-2 font-mono">{{ span.serviceName }}</span>
          </p>
          <p v-if="span.scopeName" class="text-xs">
            <span class="text-muted">scope</span>
            <span class="ml-2 font-mono">
              {{ span.scopeName }}<span v-if="span.scopeVersion" class="text-muted"> @ {{ span.scopeVersion }}</span>
            </span>
          </p>
        </section>

        <section v-if="hasAttributes" class="space-y-1">
          <h3 class="text-caption uppercase tracking-wide">{{ t('common.attributes') }}</h3>
          <dl class="text-xs space-y-0.5">
            <div v-for="(value, key) in span.attributes" :key="key" class="flex gap-2 flex-wrap">
              <dt class="font-mono text-muted">{{ key }}</dt>
              <dd class="font-mono break-all">{{ JSON.stringify(value) }}</dd>
            </div>
          </dl>
        </section>

        <section v-if="span.events.length > 0" class="space-y-1">
          <h3 class="text-caption uppercase tracking-wide">
            {{ t('traces.detail.events') }} ({{ span.events.length }})
          </h3>
          <ul class="text-xs space-y-1">
            <li v-for="(e, i) in span.events" :key="i" class="bg-elevated px-2 py-1.5 rounded-md">
              <div class="flex items-center justify-between gap-2">
                <span class="font-medium">{{ e.name }}</span>
                <span class="font-mono text-muted">{{ fmtTime(e.time) }}</span>
              </div>
              <dl v-if="Object.keys(e.attributes).length > 0" class="mt-1 space-y-0.5">
                <div v-for="(value, key) in e.attributes" :key="key" class="flex gap-2 flex-wrap">
                  <dt class="font-mono text-muted">{{ key }}</dt>
                  <dd class="font-mono break-all">{{ JSON.stringify(value) }}</dd>
                </div>
              </dl>
            </li>
          </ul>
        </section>

        <section v-if="span.links.length > 0" class="space-y-1">
          <h3 class="text-caption uppercase tracking-wide">
            {{ t('traces.detail.links') }} ({{ span.links.length }})
          </h3>
          <ul class="text-xs space-y-1">
            <li v-for="(l, i) in span.links" :key="i" class="bg-elevated px-2 py-1.5 rounded-md">
              <NuxtLink
                :to="`/traces/${l.traceId}`"
                class="font-mono text-primary hover:underline break-all"
              >
                {{ l.traceId }}
              </NuxtLink>
              <p class="font-mono text-muted">span {{ l.spanId }}</p>
            </li>
          </ul>
        </section>
      </div>
      <AppEmptyState
        v-else
        key="empty"
        icon="i-ph-cursor-click"
        :title="t('traces.detail.spanDetail')"
        :description="t('traces.detail.spans')"
      />
    </Transition>
  </div>
</template>
