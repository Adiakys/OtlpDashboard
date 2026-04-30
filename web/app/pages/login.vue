<script setup lang="ts">
definePageMeta({ layout: 'empty' })

const { t } = useI18n()
const { $authStore, $logsService, $refreshInfo, $appName, $appVersion } = useNuxtApp()
const route = useRoute()

const nextTarget = computed(() => {
  const next = route.query.next
  return typeof next === 'string' && next.startsWith('/') ? next : '/dashboard'
})

if ($authStore.isAuthenticated()) {
  await navigateTo(nextTarget.value, { replace: true })
}

const password = ref('')
const error = ref<string | null>(null)
const isSubmitting = ref(false)

async function submit() {
  if (!password.value || isSubmitting.value) return

  isSubmitting.value = true
  error.value = null
  $authStore.setToken(password.value)

  try {
    const now = new Date()
    const from = new Date(now.getTime() - 60_000).toISOString()
    await $logsService.listLogs({ from, to: now.toISOString(), limit: 1 })
  } catch (e: unknown) {
    $authStore.clear()
    const status = (e as { statusCode?: number; response?: { status?: number } }).statusCode
      ?? (e as { response?: { status?: number } }).response?.status
    error.value = status === 401
      ? t('auth.wrongPassword')
      : (e instanceof Error ? e.message : String(e))
    password.value = ''
    isSubmitting.value = false
    return
  }

  await $refreshInfo()
  await navigateTo(nextTarget.value, { replace: true })
}
</script>

<template>
  <div class="vellum-login min-h-[100dvh] flex flex-col md:flex-row">
    <!-- LEFT — editorial hero (60% on md+) -->
    <section class="vellum-login__hero relative flex flex-col justify-between md:flex-[3] px-8 md:px-14 py-10 md:py-14">
      <div class="vellum-login__grid" aria-hidden="true" />

      <header class="relative flex items-start gap-5">
        <span class="shrink-0 inline-flex items-center justify-center text-default" aria-hidden="true">
          <svg viewBox="0 0 32 32" fill="none" width="56" height="56">
            <line x1="6" y1="11" x2="26" y2="11" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            <line x1="6" y1="16" x2="20" y2="16" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            <circle cx="22.6" cy="16" r="1.7" fill="var(--color-ember-500)" />
            <line x1="6" y1="21" x2="22" y2="21" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
          </svg>
        </span>
        <div class="flex flex-col gap-1.5 min-w-0">
          <span class="text-overline" style="color: var(--color-graphite-500);">
            {{ t('auth.overline') }}<template v-if="$appVersion"> · v{{ $appVersion }}</template>
          </span>
          <h1 class="text-display text-default truncate">{{ $appName }}</h1>
        </div>
      </header>

      <p class="relative text-body text-muted max-w-[42ch] mt-10 md:mt-0">
        {{ t('auth.tagline') }}
      </p>
    </section>

    <!-- RIGHT — minimal form (40% on md+) -->
    <section class="vellum-login__form md:flex-[2] flex flex-col justify-center px-8 md:px-12 py-10 md:py-16">
      <div class="w-full max-w-sm">
        <span class="text-overline" style="color: var(--color-graphite-500);">
          {{ t('auth.signInLabel') }}
        </span>

        <form class="mt-6 flex flex-col gap-5" @submit.prevent="submit">
          <label class="flex flex-col gap-2">
            <span class="text-caption">{{ t('auth.passwordLabel') }}</span>
            <input
              v-model="password"
              type="password"
              autocomplete="current-password"
              :disabled="isSubmitting"
              autofocus
              class="vellum-login__input"
            />
          </label>

          <p
            v-if="error"
            role="alert"
            class="text-mono-sm"
            style="color: var(--color-rust-700);"
          >
            {{ error }}
          </p>

          <button
            type="submit"
            class="vellum-login__submit vellum-tactile"
            :disabled="!password || isSubmitting"
          >
            <span v-if="!isSubmitting">{{ t('auth.submit') }}</span>
            <span v-else class="inline-flex items-center gap-2">
              <UIcon name="i-ph-circle-notch" class="size-4 animate-spin" />
              {{ t('common.loading') }}
            </span>
          </button>
        </form>
      </div>
    </section>
  </div>
</template>

<style scoped>
.vellum-login__hero {
  background: var(--ui-bg);
  position: relative;
  overflow: hidden;
}

/* Hairline grid backdrop, faded. */
.vellum-login__grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(to right, color-mix(in oklab, var(--color-graphite-500) 10%, transparent) 1px, transparent 1px),
    linear-gradient(to bottom, color-mix(in oklab, var(--color-graphite-500) 10%, transparent) 1px, transparent 1px);
  background-size: 32px 32px;
  mask-image: radial-gradient(ellipse 80% 70% at 30% 60%, black 0%, transparent 80%);
  -webkit-mask-image: radial-gradient(ellipse 80% 70% at 30% 60%, black 0%, transparent 80%);
  pointer-events: none;
}

.vellum-login__form {
  background: var(--ui-bg-elevated);
  border-left: 1px solid color-mix(in oklab, var(--color-graphite-500) 18%, transparent);
}
@media (max-width: 767px) {
  .vellum-login__form {
    border-left: none;
    border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 18%, transparent);
  }
}

/* Hairline-bottom-only input, focus turns ember. */
.vellum-login__input {
  width: 100%;
  background: transparent;
  border: none;
  border-bottom: 1px solid color-mix(in oklab, var(--color-graphite-500) 30%, transparent);
  border-radius: 0;
  padding: 0.5rem 0;
  font-family: var(--font-mono);
  font-size: 0.9375rem;
  color: var(--ui-text);
  outline: none;
  transition: border-color var(--t-fast) var(--ease-out);
}
.vellum-login__input:focus {
  border-bottom-color: var(--color-ember-500);
}
.vellum-login__input:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.vellum-login__submit {
  width: 100%;
  padding: 0.625rem 1rem;
  background: var(--color-ember-500);
  color: oklch(0.985 0.004 65);
  border: none;
  border-radius: var(--radius-sm);
  font-family: var(--font-sans);
  font-size: 0.875rem;
  font-weight: 600;
  letter-spacing: -0.005em;
  cursor: pointer;
  transition:
    background-color var(--t-fast) var(--ease-out),
    transform var(--t-instant) var(--ease-out),
    opacity var(--t-instant) var(--ease-out);
}
.vellum-login__submit:hover:not(:disabled) {
  background: var(--color-ember-400);
}
.vellum-login__submit:active:not(:disabled) {
  transform: translateY(1px);
  opacity: 0.92;
}
.vellum-login__submit:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
