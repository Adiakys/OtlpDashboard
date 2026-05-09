import { describe, expect, it } from 'vitest'
import { buildIconResolver } from '~/pages/serviceMap/composables/useIconResolver'
import type { PackDto, PackIconDto } from '~/services/types'

function pack(id: string, ...icons: PackIconDto[]): PackDto {
  return {
    id,
    name: id,
    version: '1.0.0',
    author: null,
    license: null,
    description: null,
    homepage: null,
    installSource: 'Filesystem',
    gitUrl: null,
    gitRef: null,
    gitRefResolved: null,
    gitSubPath: null,
    installedAt: null,
    removable: false,
    libraries: [],
    dashboards: [],
    icons
  }
}

const POSTGRES_ICON: PackIconDto = {
  id: 'postgres',
  name: 'PostgreSQL',
  imageUrl: '/api/v1/packs/default/assets/icons/postgres/postgres.svg',
  match: [
    { serviceName: 'postgresql' },
    { namePattern: '^postgres' }
  ]
}

describe('useIconResolver', () => {
  it('returns null when no packs declare matching icons', () => {
    const { resolve } = buildIconResolver([])
    expect(resolve('postgresql')).toBeNull()
  })

  it('matches by exact serviceName', () => {
    const { resolve } = buildIconResolver([pack('default', POSTGRES_ICON)])
    expect(resolve('postgresql')).toBe(POSTGRES_ICON.imageUrl)
  })

  it('falls back to namePattern when serviceName misses', () => {
    const { resolve } = buildIconResolver([pack('default', POSTGRES_ICON)])
    expect(resolve('postgres-primary')).toBe(POSTGRES_ICON.imageUrl)
  })

  it('returns null for unrelated service names', () => {
    const { resolve } = buildIconResolver([pack('default', POSTGRES_ICON)])
    expect(resolve('redis')).toBeNull()
    expect(resolve('sample-server')).toBeNull()
  })

  it('returns null for empty service input', () => {
    const { resolve } = buildIconResolver([pack('default', POSTGRES_ICON)])
    expect(resolve('')).toBeNull()
    expect(resolve(null)).toBeNull()
    expect(resolve(undefined)).toBeNull()
  })

  it('first pack wins on cross-pack collision', () => {
    const alpha: PackIconDto = {
      id: 'pg',
      name: 'Alpha',
      imageUrl: '/alpha.svg',
      match: [{ serviceName: 'postgres' }]
    }
    const beta: PackIconDto = {
      id: 'pg',
      name: 'Beta',
      imageUrl: '/beta.svg',
      match: [{ serviceName: 'postgres' }]
    }
    const { resolve } = buildIconResolver([pack('alpha', alpha), pack('beta', beta)])
    expect(resolve('postgres')).toBe('/alpha.svg')
  })

  it('survives invalid regex in a pack manifest', () => {
    const broken: PackIconDto = {
      id: 'broken',
      name: 'Broken',
      imageUrl: '/broken.svg',
      match: [{ namePattern: '(unclosed' }]
    }
    const { resolve } = buildIconResolver([
      pack('broken-pack', broken),
      pack('default', POSTGRES_ICON)
    ])
    // The bad regex matcher silently disables, so the search continues
    // into the next pack and finds postgres normally.
    expect(resolve('postgresql')).toBe(POSTGRES_ICON.imageUrl)
  })
})
