<script setup lang="ts">
import { computed } from 'vue'
import BaseWidget from '../components/BaseWidget.vue'
import type { TextWidgetConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { escapeHtml } from '~/lib/escapeHtml'

const props = withDefaults(defineProps<{
  config: TextWidgetConfig
  isEditing: boolean
  preview?: boolean
}>(), { preview: false })

defineEmits<{
  edit: []
  remove: []
}>()

const { t } = useI18n()

const headerTitle = computed(() => props.config.title || t(WIDGET_REGISTRY.text.titleKey))
const isCenter = computed(() => props.config.align === 'center')

// Tiny markdown renderer: bold (**…**), italics (*…* / _…_), inline code (`…`),
// h1/h2/h3 (#, ##, ###), unordered lists (-), and explicit line breaks.
// Intentionally narrow — pulling in a full markdown library would be overkill
// for tenant-trusted text panels and would balloon the bundle.
//
// Safety: input is HTML-escaped *before* the regex pass, so nothing the user
// types can introduce raw tags. The inline transforms only inject known
// classes, never user content.
function renderMarkdown(input: string): string {
  const escaped = escapeHtml(input)

  const lines = escaped.split(/\r?\n/)
  const out: string[] = []
  let inList = false
  for (const raw of lines) {
    const line = raw.trimEnd()
    if (/^\s*-\s+/.test(line)) {
      if (!inList) { out.push('<ul class="list-disc pl-5 my-1">'); inList = true }
      out.push(`<li>${inline(line.replace(/^\s*-\s+/, ''))}</li>`)
      continue
    }
    if (inList) { out.push('</ul>'); inList = false }

    if (line.startsWith('### ')) out.push(`<h3 class="text-base font-semibold mt-2">${inline(line.slice(4))}</h3>`)
    else if (line.startsWith('## ')) out.push(`<h2 class="text-lg font-semibold mt-2">${inline(line.slice(3))}</h2>`)
    else if (line.startsWith('# ')) out.push(`<h1 class="text-xl font-bold mt-2">${inline(line.slice(2))}</h1>`)
    else if (line.length === 0) out.push('<div class="h-2"></div>')
    else out.push(`<p class="my-1">${inline(line)}</p>`)
  }
  if (inList) out.push('</ul>')
  return out.join('\n')
}

function inline(s: string): string {
  return s
    .replaceAll(/`([^`]+)`/g, '<code class="px-1 py-0.5 rounded bg-elevated text-xs">$1</code>')
    .replaceAll(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replaceAll(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>')
    .replaceAll(/(^|[^_])_([^_]+)_/g, '$1<em>$2</em>')
}

const html = computed(() => renderMarkdown(props.config.markdown ?? ''))
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY.text.icon"
    :is-editing="isEditing"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <div class="vellum-preview-text">
        <span class="vellum-preview-text__heading">## Title</span>
        <span class="vellum-preview-text__line vellum-preview-text__line--full" />
        <span class="vellum-preview-text__line vellum-preview-text__line--mid" />
        <span class="vellum-preview-text__line vellum-preview-text__line--short" />
      </div>
    </template>
    <div class="flex-1 min-h-0 min-w-0 overflow-auto px-4 py-3 text-body text-default vellum-text-widget" :class="{ 'text-center': isCenter }">
      <!-- eslint-disable-next-line vue/no-v-html -->
      <div v-html="html" />
    </div>
  </BaseWidget>
</template>

<style scoped>
.vellum-text-widget :deep(code) {
  font-family: var(--font-mono);
  font-size: 0.85em;
  background: color-mix(in oklab, var(--color-graphite-500) 12%, transparent);
  padding: 0.1em 0.35em;
  border-radius: var(--radius-xs);
}
.vellum-text-widget :deep(h1),
.vellum-text-widget :deep(h2),
.vellum-text-widget :deep(h3) {
  letter-spacing: -0.01em;
}

.vellum-preview-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  padding: 0.5rem 0.6rem;
  justify-content: center;
}
.vellum-preview-text__heading {
  font-family: var(--font-mono);
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--color-graphite-700);
}
:global(html.dark) .vellum-preview-text__heading { color: var(--color-graphite-300); }
.vellum-preview-text__line {
  height: 0.35rem;
  border-radius: var(--radius-pill);
  background: color-mix(in oklab, var(--color-graphite-500) 22%, transparent);
}
.vellum-preview-text__line--full  { width: 100%; }
.vellum-preview-text__line--mid   { width: 75%; }
.vellum-preview-text__line--short { width: 50%; }
</style>
