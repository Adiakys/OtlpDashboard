<script setup lang="ts">
import { computed } from 'vue'
import BaseWidget from '../components/BaseWidget.vue'
import type { TextWidgetConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { escapeHtml } from '~/lib/escapeHtml'

const props = defineProps<{
  config: TextWidgetConfig
  isEditing: boolean
}>()

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
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <div class="flex-1 min-h-0 min-w-0 overflow-auto p-3 text-sm text-default" :class="{ 'text-center': isCenter }">
      <!-- eslint-disable-next-line vue/no-v-html -->
      <div v-html="html" />
    </div>
  </BaseWidget>
</template>
