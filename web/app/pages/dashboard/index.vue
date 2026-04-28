<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'

const { t } = useI18n()
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar :title="t('dashboard.title')" />
    </template>
    <AppEmptyState
      icon="i-lucide-layout-dashboard"
      :title="t('dashboard.emptyTitle')"
      :description="t('dashboard.emptyDescription')"
    />
  </AppPage>
</template>
