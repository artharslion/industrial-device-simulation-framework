<script setup lang="ts">
defineProps<{ loading?: boolean; error?: string; empty?: boolean; emptyText?: string }>()
</script>

<template>
  <div v-if="loading" class="resource-state" role="status"><i class="sync-dot syncing"></i> Loading control-plane data…</div>
  <div v-else-if="error" class="resource-state error" role="alert">{{ error }}</div>
  <div v-else-if="empty" class="resource-state">{{ emptyText ?? 'No records yet.' }}</div>
  <slot v-else />
</template>
