import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import ConsoleLayout from '../layouts/ConsoleLayout.vue'

export const routes: RouteRecordRaw[] = [
  { path: '/', name: 'overview', component: () => import('../views/OverviewView.vue'), meta: { navigation: true, label: 'Overview', section: 'Operate' } },
  { path: '/devices', name: 'devices', component: () => import('../views/DevicesView.vue'), meta: { navigation: true, label: 'Devices', section: 'Operate' } },
  { path: '/templates', name: 'templates', component: () => import('../views/TemplatesView.vue'), meta: { navigation: true, label: 'Templates', section: 'Model' } },
  { path: '/scenarios', name: 'scenarios', component: () => import('../views/ScenariosView.vue'), meta: { navigation: true, label: 'Scenarios', section: 'Model' } },
  { path: '/protocols', name: 'protocols', component: () => import('../views/ProtocolsView.vue'), meta: { navigation: true, label: 'Protocols', section: 'Observe' } },
  { path: '/events', name: 'events', component: () => import('../views/EventsView.vue'), meta: { navigation: true, label: 'Events', section: 'Observe' } },
  { path: '/users', name: 'users', component: () => import('../views/UsersView.vue'), meta: { navigation: true, label: 'Users', section: 'Admin', admin: true } },
  { path: '/settings', name: 'settings', component: () => import('../views/SettingsView.vue'), meta: { navigation: true, label: 'Settings', section: 'Admin', admin: true } },
  { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('../views/NotFoundView.vue') },
]

export const router = createRouter({ history: createWebHistory(), routes: [{ path: '/', component: ConsoleLayout, children: routes }] })
