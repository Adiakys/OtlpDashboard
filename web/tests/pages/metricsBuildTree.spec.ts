import { describe, expect, it } from 'vitest'
import {
  ROOT_BUCKET,
  UNKNOWN_SERVICE,
  buildTree,
  type MetricTreeBranch,
  type MetricTreeNode
} from '~/pages/metrics/buildTree'
import type { InstrumentDto } from '~/services/types'

function instrument(overrides: Partial<InstrumentDto> = {}): InstrumentDto {
  return {
    resourceHash: 'r',
    serviceName: 'svc',
    serviceInstanceId: null,
    scopeName: 'Scope',
    name: 'metric',
    kind: 'Gauge',
    description: null,
    unit: null,
    isMonotonic: false,
    temporality: 'Cumulative',
    pointCount: 1,
    ...overrides
  }
}

function findBranch(nodes: MetricTreeNode[], label: string): MetricTreeBranch {
  const match = nodes.find(n => n.kind === 'branch' && n.label === label)
  if (!match) throw new Error(`branch '${label}' not found`)
  return match as MetricTreeBranch
}

describe('buildTree', () => {
  it('nests serviceInstanceId between service and scope', () => {
    const tree = buildTree([
      instrument({ resourceHash: 'a', serviceInstanceId: 'i-1', scopeName: 'Web', name: 'a' }),
      instrument({ resourceHash: 'b', serviceInstanceId: 'i-2', scopeName: 'Web', name: 'b' }),
      instrument({ resourceHash: 'c', serviceInstanceId: 'i-1', scopeName: 'Db',  name: 'c' })
    ])
    const svc = findBranch(tree, 'svc')
    const i1 = findBranch(svc.children, 'i-1')
    const i2 = findBranch(svc.children, 'i-2')
    // i-1 hosts two scopes (Web and Db); i-2 hosts only Web.
    expect(i1.children.map(c => c.label).sort()).toEqual(['Db', 'Web'])
    expect(i2.children.map(c => c.label)).toEqual(['Web'])
  })

  it('skips the instance level when serviceInstanceId is absent', () => {
    const tree = buildTree([
      instrument({ serviceName: 'svc', serviceInstanceId: null, scopeName: 'Web', name: 'a' })
    ])
    const svc = findBranch(tree, 'svc')
    // The scope branch sits directly under the service; no synthetic
    // instance node was inserted.
    expect(svc.children).toHaveLength(1)
    expect(svc.children[0]!.kind).toBe('branch')
    expect((svc.children[0] as MetricTreeBranch).label).toBe('Web')
  })

  it('flattens when a service is observed under a single instance id', () => {
    const tree = buildTree([
      instrument({ resourceHash: 'a', serviceName: 'svc', serviceInstanceId: 'i-1', scopeName: 'Web', name: 'a' }),
      instrument({ resourceHash: 'b', serviceName: 'svc', serviceInstanceId: 'i-1', scopeName: 'Db',  name: 'b' })
    ])
    const svc = findBranch(tree, 'svc')
    // A single distinct instance id ⇒ no synthetic instance node;
    // scope branches sit directly under the service.
    expect(svc.children.map(c => c.label).sort()).toEqual(['Db', 'Web'])
  })

  it('flattens when a service mixes one instance id with bare emitters', () => {
    const tree = buildTree([
      instrument({ resourceHash: 'a', serviceName: 'svc', serviceInstanceId: 'i-1', scopeName: 'Web', name: 'a' }),
      instrument({ resourceHash: 'b', serviceName: 'svc', serviceInstanceId: null,  scopeName: 'Db',  name: 'b' })
    ])
    const svc = findBranch(tree, 'svc')
    // One distinct identity in play ('i-1' + null) — still flatten,
    // an extra instance level would split a single logical app
    // into two halves for no UX gain.
    expect(svc.children.map(c => c.label).sort()).toEqual(['Db', 'Web'])
  })

  it('falls back to (unknown) and (root) for missing identifiers', () => {
    const tree = buildTree([
      instrument({ serviceName: null, serviceInstanceId: null, scopeName: '', name: 'm' })
    ])
    const unknown = findBranch(tree, UNKNOWN_SERVICE)
    expect(unknown.children).toHaveLength(1)
    const root = unknown.children[0] as MetricTreeBranch
    expect(root.label).toBe(ROOT_BUCKET)
  })
})
