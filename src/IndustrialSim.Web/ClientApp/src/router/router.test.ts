import { describe, expect, it } from 'vitest'
import { routes } from './index'

describe('console router', () => {
  it('exposes every platform workspace as an addressable page', () => {
    expect(routes.map(route => route.path)).toEqual([
      '/', '/devices', '/devices/:deviceId', '/templates', '/scenarios', '/protocols', '/events', '/users', '/settings', '/:pathMatch(.*)*',
    ])
    expect(routes.filter(route => route.meta?.navigation).map(route => route.name)).toEqual([
      'overview', 'devices', 'templates', 'scenarios', 'protocols', 'events', 'users', 'settings',
    ])
  })
})
