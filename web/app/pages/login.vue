<script setup lang="ts">
// Minimal password form. Validation is a single API call to the read-side:
// if the backend accepts the token we persist it and redirect; if it
// returns 401 we show the error locally (the global 401 interceptor skips
// redirects while the user is already on /login, so no loops).

definePageMeta({ layout: 'empty' })

const { $authStore, $logsService, $appName, $refreshInfo } = useNuxtApp()
const route = useRoute()

const nextTarget = computed(() => {
  const next = route.query.next
  return typeof next === 'string' && next.startsWith('/') ? next : '/dashboard'
})

// If the user lands here already authenticated, skip the form.
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
    // Cheap validation call: if the token is wrong we get 401 → caught below.
    const now = new Date()
    const from = new Date(now.getTime() - 60_000).toISOString()
    await $logsService.listLogs({ from, to: now.toISOString(), limit: 1 })
  } catch (e: unknown) {
    $authStore.clear()
    const status = (e as { statusCode?: number, response?: { status?: number } }).statusCode
      ?? (e as { response?: { status?: number } }).response?.status
    error.value = status === 401
      ? 'Password errata.'
      : `Errore di rete: ${e instanceof Error ? e.message : String(e)}`
    password.value = ''
    isSubmitting.value = false
    return
  }

  // Pull the authenticated info (version is server-side auth-gated) before
  // leaving, so the sidebar shows it without needing a page reload.
  await $refreshInfo()

  await navigateTo(nextTarget.value, { replace: true })
}
</script>

<template>
  <div class="min-h-screen flex items-center justify-center p-4 bg-muted">
    <UCard class="w-full max-w-sm">
      <template #header>
        <div class="flex items-center gap-2">
          <UIcon name="i-lucide-gauge" class="size-5 text-primary shrink-0" />
          <h1 class="text-base font-semibold truncate" :title="$appName">
            {{ $appName }}
          </h1>
        </div>
        <p class="text-xs text-muted mt-1">
          Inserisci la password per accedere.
        </p>
      </template>

      <form class="space-y-3" @submit.prevent="submit">
        <UInput
          v-model="password"
          type="password"
          placeholder="Password"
          autocomplete="current-password"
          :disabled="isSubmitting"
          autofocus
          class="w-full"
        />

        <UAlert
          v-if="error"
          color="error"
          variant="subtle"
          icon="i-lucide-alert-triangle"
          :title="error"
        />

        <UButton
          type="submit"
          :loading="isSubmitting"
          :disabled="!password || isSubmitting"
          block
        >
          Entra
        </UButton>
      </form>
    </UCard>
  </div>
</template>
