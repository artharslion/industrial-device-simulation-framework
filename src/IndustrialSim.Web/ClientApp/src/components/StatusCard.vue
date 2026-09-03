<script setup lang="ts">
defineProps<{
  label: string
  value: string
  status: string
}>()
</script>

<template>
  <article class="status-card">
    <div class="status-card-label">
      <span>{{ label }}</span>
      <i class="lamp" :class="status.toLowerCase()" aria-hidden="true"></i>
    </div>
    <strong>{{ value }}</strong>
  </article>
</template>
