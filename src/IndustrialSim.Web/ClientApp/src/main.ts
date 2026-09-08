import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { useThemeStore } from './stores/theme'
import './styles.css'

const pinia = createPinia()
useThemeStore(pinia).initialize()
createApp(App).use(pinia).use(router).mount('#app')
