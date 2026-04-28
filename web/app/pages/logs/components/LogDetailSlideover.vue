<script setup lang="ts">
import type { LogRecordDto } from '~/services/types'

const model = defineModel<LogRecordDto | null>({ required: true })

const open = computed({
  get: () => model.value !== null,
  set: (v: boolean) => { if (!v) model.value = null }
})

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : '—'
}
</script>

<template>
  <USlideover v-model:open="open" title="Log record" side="right">
    <template #body>
      <div v-if="model" class="space-y-4 text-sm">
        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Time
          </h3>
          <p class="font-mono">
            {{ formatTime(model.time) }}
          </p>
          <p v-if="model.observedTime" class="text-xs text-muted">
            Observed: {{ formatTime(model.observedTime) }}
          </p>
        </section>

        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Severity
          </h3>
          <p>{{ model.severityText ?? model.severityNumber }} ({{ model.severityNumber }})</p>
        </section>

        <section v-if="model.body" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Body
          </h3>
          <pre class="whitespace-pre-wrap text-sm bg-elevated px-2 py-1 rounded">{{ model.body }}</pre>
        </section>

        <section v-if="model.traceId || model.spanId" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Correlation
          </h3>
          <p v-if="model.traceId" class="font-mono text-xs">
            <NuxtLink :to="`/traces/${model.traceId}`" class="text-primary hover:underline">
              trace {{ model.traceId }}
            </NuxtLink>
          </p>
          <p v-if="model.spanId" class="font-mono text-xs text-muted">
            span {{ model.spanId }}
          </p>
        </section>

        <section v-if="model.scopeName" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Scope
          </h3>
          <p class="text-xs">
            {{ model.scopeName }}
            <span v-if="model.scopeVersion" class="text-muted">@ {{ model.scopeVersion }}</span>
          </p>
        </section>

        <section v-if="Object.keys(model.attributes).length > 0" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Attributes
          </h3>
          <dl class="text-xs space-y-0.5">
            <div v-for="(value, key) in model.attributes" :key="key" class="flex gap-2">
              <dt class="font-mono text-muted">
                {{ key }}
              </dt>
              <dd class="font-mono">
                {{ JSON.stringify(value) }}
              </dd>
            </div>
          </dl>
        </section>

        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Resource hash
          </h3>
          <p class="font-mono text-xs text-muted break-all">
            {{ model.resourceHash }}
          </p>
        </section>
      </div>
    </template>
  </USlideover>
</template>
