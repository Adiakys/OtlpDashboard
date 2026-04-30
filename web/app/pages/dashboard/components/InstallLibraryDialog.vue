<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  installed: []
}>()

const { t } = useI18n()
const { $widgetService } = useNuxtApp()

const url = ref('')
const gitRef = ref('')
const error = ref<string | null>(null)
const isSubmitting = ref(false)

// Reset state every time the dialog re-opens — surprises the user less
// than carrying half-typed URLs across sessions.
watch(() => props.open, (isOpen) => {
  if (isOpen) {
    url.value = ''
    gitRef.value = ''
    error.value = null
    isSubmitting.value = false
  }
})

const refLooksLikeBranch = computed(() => {
  const r = gitRef.value.trim()
  if (!r) return false
  const isHexSha = /^[0-9a-fA-F]{7,40}$/.test(r)
  const looksLikeTag = /^v?\d/.test(r)
  return !isHexSha && !looksLikeTag
})

const canSubmit = computed(() =>
  url.value.trim().length > 0 && gitRef.value.trim().length > 0 && !isSubmitting.value)

async function submit() {
  if (!canSubmit.value) return
  isSubmitting.value = true
  error.value = null
  try {
    await $widgetService.installLibrary({
      url: url.value.trim(),
      ref: gitRef.value.trim()
    })
    emit('installed')
    emit('update:open', false)
  } catch (err: unknown) {
    error.value = extractMessage(err)
  } finally {
    isSubmitting.value = false
  }
}

/**
 * The server returns RFC 7807 ProblemDetails for 400/409/422; ofetch
 * surfaces the parsed body via `data`. Falls back to the generic message
 * for unexpected shapes.
 */
function extractMessage(err: unknown): string {
  const candidate = (err as { data?: { detail?: unknown } } | undefined)?.data?.detail
  if (typeof candidate === 'string' && candidate.length > 0) return candidate
  return err instanceof Error ? err.message : String(err)
}

function close() {
  if (isSubmitting.value) return
  emit('update:open', false)
}

// Esc closes — same affordance UModal gave for free.
function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.open) close()
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="open"
        class="vellum-install-overlay"
        @mousedown.self="close"
      />
    </Transition>
    <Transition name="install-pop">
      <div
        v-if="open"
        class="vellum-install-shell bg-default text-default"
        role="dialog"
        :aria-label="t('widgets.picker.installFromGit')"
      >
        <header class="vellum-install-headbar">
          <UIcon name="i-ph-cloud-arrow-down" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
          <h2 class="text-headline truncate flex-1">{{ t('widgets.picker.installFromGit') }}</h2>
          <UButton
            size="xs"
            color="neutral"
            variant="ghost"
            icon="i-ph-x"
            square
            :disabled="isSubmitting"
            :aria-label="t('common.close')"
            @click="close"
          />
        </header>

        <form class="vellum-install-body" @submit.prevent="submit">
          <label class="flex flex-col gap-1.5">
            <span class="text-caption">{{ t('widgets.picker.installUrlLabel') }}</span>
            <UInput
              v-model="url"
              type="url"
              size="sm"
              :placeholder="t('widgets.picker.installUrlPlaceholder')"
              :disabled="isSubmitting"
              autofocus
            />
            <span class="text-mono-sm" style="color: var(--color-graphite-500);">
              {{ t('widgets.picker.installUrlHint') }}
            </span>
          </label>

          <label class="flex flex-col gap-1.5">
            <span class="text-caption">{{ t('widgets.picker.installRefLabel') }}</span>
            <UInput
              v-model="gitRef"
              size="sm"
              placeholder="v1.2.0"
              :disabled="isSubmitting"
            />
            <span class="text-mono-sm" style="color: var(--color-graphite-500);">
              {{ t('widgets.picker.installRefHint') }}
            </span>
            <span
              v-if="refLooksLikeBranch"
              class="text-mono-sm"
              style="color: var(--color-amber-700);"
            >
              {{ t('widgets.picker.installRefBranchWarning') }}
            </span>
          </label>

          <p
            v-if="error"
            role="alert"
            class="text-mono-sm"
            style="color: var(--color-rust-700);"
          >
            {{ error }}
          </p>

          <div class="flex justify-end gap-2 pt-2">
            <UButton
              type="button"
              color="neutral"
              variant="ghost"
              size="sm"
              :disabled="isSubmitting"
              @click="close"
            >
              {{ t('common.cancel') }}
            </UButton>
            <UButton
              type="submit"
              color="primary"
              size="sm"
              :loading="isSubmitting"
              :disabled="!canSubmit"
            >
              {{ t('widgets.picker.installSubmit') }}
            </UButton>
          </div>
        </form>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* Backdrop — opaque enough to mute the picker behind it so the user
   knows the install dialog is the active surface. z-index sits above
   the picker shell (z-40) and below any future system-level overlays. */
.vellum-install-overlay {
  position: fixed;
  inset: 0;
  z-index: 60;
  background: oklch(0.115 0.006 40 / 0.55);
}

.vellum-install-shell {
  position: fixed;
  top: 50%;
  left: 50%;
  z-index: 70;
  transform: translate(-50%, -50%);
  display: flex;
  flex-direction: column;
  width: min(560px, 92vw);
  max-height: 85vh;
  border: 1px solid color-mix(in oklab, var(--color-graphite-500) 22%, transparent);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-3), var(--shadow-inset-edge);
  overflow: hidden;
}

.vellum-install-headbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
}

.vellum-install-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.install-pop-enter-active,
.install-pop-leave-active {
  transition: opacity var(--t-base) var(--ease-out);
}
.install-pop-enter-from,
.install-pop-leave-to {
  opacity: 0;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity var(--t-base) var(--ease-out);
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
