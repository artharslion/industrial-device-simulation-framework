<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { routes } from '../router'
import { useWorkspaceStore } from '../stores/workspace'
import { useThemeStore, type ThemePreference } from '../stores/theme'

const route = useRoute()
const workspace = useWorkspaceStore()
const theme = useThemeStore()
const navigation = computed(() => routes.filter(item => item.meta?.navigation && (!item.meta.admin || workspace.isAdmin)))
const groups = computed(() => [...new Set(navigation.value.map(item => String(item.meta?.section)))])
const currentLabel = computed(() => String(route.meta.label ?? 'Workspace'))

onMounted(() => { void workspace.loadSession() })
</script>

<template>
  <div class="app-shell">
    <aside class="workspace-sidebar" aria-label="Workspace navigation">
      <RouterLink class="brand" to="/" aria-label="Industrial Sim overview">
        <div class="brand-mark">IS</div>
        <div class="brand-copy"><strong>Industrial Sim</strong><span>Model · Run · Inspect</span></div>
      </RouterLink>
      <template v-for="group in groups" :key="group">
        <div class="sidebar-label">{{ group }}</div>
        <nav class="sidebar-nav">
          <RouterLink
            v-for="item in navigation.filter(link => link.meta?.section === group)"
            :key="String(item.name)"
            class="nav-item"
            :to="item.path"
          >{{ item.meta?.label }}</RouterLink>
        </nav>
      </template>
      <div class="sidebar-spacer"></div>
      <div class="environment-card">
        <span>{{ workspace.session.mode }}</span>
        <strong>{{ workspace.session.userName ?? 'Sign in required' }}</strong>
        <small>{{ workspace.session.roles.join(' · ') || 'No active role' }}</small>
      </div>
    </aside>

    <section class="command-center">
      <header class="workspace-header">
        <div class="breadcrumb"><strong>Workspace</strong><span class="slash">/</span><span>{{ currentLabel }}</span></div>
        <div class="header-meta">
          <label class="theme-control"><span>Theme</span><select :value="theme.preference" aria-label="Theme" @change="theme.setPreference(($event.target as HTMLSelectElement).value as ThemePreference)"><option value="system">System</option><option value="light">Light</option><option value="dark">Dark</option></select></label>
          <i class="sync-dot" aria-hidden="true"></i><span>Control plane online</span>
        </div>
      </header>
      <Transition name="notice"><div v-if="workspace.notice" class="global-notice" role="status">{{ workspace.notice }}</div></Transition>
      <RouterView />
    </section>
  </div>
</template>
