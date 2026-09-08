import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { platformApi } from '../api'
import UsersView from './UsersView.vue'

describe('users view', () => {
  it('explains disabled authentication and presents the effective local admin without a create form', async () => {
    vi.spyOn(platformApi, 'session').mockResolvedValue({ mode: 'Disabled', authenticated: true, userName: 'local-developer', roles: ['Admin'] })
    const users = vi.spyOn(platformApi, 'users')
    const wrapper = mount(UsersView, { global: { plugins: [createPinia()] } })
    await flushPromises()
    expect(wrapper.text()).toContain('Authentication disabled')
    expect(wrapper.text()).toContain('local-developer')
    expect(wrapper.text()).toContain('Admin')
    expect(wrapper.text()).not.toContain('Add user')
    expect(users).not.toHaveBeenCalled()
  })
})
