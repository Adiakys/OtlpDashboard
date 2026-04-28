<script setup lang="ts">
import AppBrand from '~/components/shell/AppBrand.vue'

definePageMeta({ layout: 'empty' })

const { t } = useI18n()
const { $authStore, $logsService, $refreshInfo } = useNuxtApp()
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
  <div class="min-h-screen flex items-center justify-center p-4 bg-muted">
    <Transition name="scale-fade" appear>
      <div class="w-full max-w-sm bg-default border border-default rounded-xl shadow-sm p-6">
        <header class="flex flex-col items-center gap-3 mb-5">
          <AppBrand hero />
          <p class="text-sm text-muted text-center">
            {{ t('auth.subtitle') }}
          </p>
        </header>

        <form class="space-y-3" @submit.prevent="submit">
          <UInput
            v-model="password"
            type="password"
            :placeholder="t('auth.passwordLabel')"
            autocomplete="current-password"
            :disabled="isSubmitting"
            autofocus
            class="w-full"
            size="md"
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
            size="md"
          >
            {{ t('auth.submit') }}
          </UButton>
        </form>
      </div>
    </Transition>
  </div>
</template>
