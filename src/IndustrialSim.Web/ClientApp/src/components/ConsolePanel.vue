<script setup lang="ts">
defineProps<{
  title: string
  meta: string
}>()
</script>

<template>
  <section class="panel">
    <header class="panel-head">
      <div class="panel-title">
        <i class="panel-icon" aria-hidden="true"></i>
        <h2>{{ title }}</h2>
      </div>
      <small>{{ meta }}</small>
    </header>
    <slot />
  </section>
</template>
