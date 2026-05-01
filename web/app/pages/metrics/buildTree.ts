import type { InstrumentDto } from '~/services/types'

export type MetricTreeNode = MetricTreeBranch | MetricTreeLeaf

export interface MetricTreeBranch {
  kind: 'branch'
  /** Last segment of the path, e.g. "Hosting" inside "Microsoft.AspNetCore.Hosting". */
  label: string
  /** Dot-separated path from the root, e.g. "Microsoft.AspNetCore.Hosting". */
  path: string
  children: MetricTreeNode[]
}

export interface MetricTreeLeaf {
  kind: 'leaf'
  label: string
  path: string
  instrument: InstrumentDto
  key: string
}

export const ROOT_BUCKET = '(root)'
export const UNKNOWN_SERVICE = '(unknown)'

/** Stable key matching the server-side InstrumentKey lookup tuple. */
export function instrumentKey(i: InstrumentDto): string {
  return `${i.resourceHash}|${i.scopeName}|${i.name}|${i.kind}`
}

/**
 * Group instruments first by `serviceName` (the application that emitted
 * them) — and, when present, by `serviceName / serviceInstanceId`, so two
 * instruments coming from different resources under the same logical
 * service (e.g. one collector scraping multiple databases under
 * `service.name=postgresql`) split into distinct branches. Within a
 * branch the layout follows `scopeName` dot segments. Instruments
 * without a service land under `(unknown)`; those without a scope land
 * under `(root)`. Branches sort before leaves within a parent.
 */
export function buildTree(instruments: InstrumentDto[]): MetricTreeNode[] {
  const root: MetricTreeBranch = { kind: 'branch', label: '', path: '', children: [] }

  for (const instrument of instruments) {
    const service = instrument.serviceName?.trim() || UNKNOWN_SERVICE
    const instance = instrument.serviceInstanceId?.trim()
    const serviceLabel = instance ? `${service} / ${instance}` : service
    const segments = [serviceLabel, ...splitScope(instrument.scopeName)]
    const parent = ensureBranch(root, segments)
    parent.children.push({
      kind: 'leaf',
      label: instrument.name,
      path: parent.path === '' ? instrument.name : `${parent.path}.${instrument.name}`,
      instrument,
      key: instrumentKey(instrument)
    })
  }

  sortRecursive(root)
  return root.children
}

/**
 * Keep only branches whose subtree contains at least one leaf matching the
 * query. Match is case-insensitive against the leaf's instrument name, the
 * scope path, and the unit. Empty/whitespace queries return the input as-is.
 */
export function filterTree(nodes: MetricTreeNode[], query: string): MetricTreeNode[] {
  const q = query.trim().toLowerCase()
  if (!q) return nodes

  function visit(node: MetricTreeNode): MetricTreeNode | null {
    if (node.kind === 'leaf') {
      const haystack = `${node.instrument.name} ${node.instrument.scopeName} ${node.instrument.unit ?? ''}`.toLowerCase()
      return haystack.includes(q) ? node : null
    }
    const kept: MetricTreeNode[] = []
    for (const child of node.children) {
      const v = visit(child)
      if (v) kept.push(v)
    }
    if (kept.length === 0) return null
    return { kind: 'branch', label: node.label, path: node.path, children: kept }
  }

  const out: MetricTreeNode[] = []
  for (const n of nodes) {
    const v = visit(n)
    if (v) out.push(v)
  }
  return out
}

/** Walk the tree and collect the path of every branch — useful for "expand all". */
export function collectBranchPaths(nodes: MetricTreeNode[]): string[] {
  const paths: string[] = []
  function walk(node: MetricTreeNode) {
    if (node.kind !== 'branch') return
    paths.push(node.path)
    for (const c of node.children) walk(c)
  }
  for (const n of nodes) walk(n)
  return paths
}

/** Total leaf count under a (possibly filtered) tree, used for header counters. */
export function countLeaves(nodes: MetricTreeNode[]): number {
  let n = 0
  for (const node of nodes) {
    if (node.kind === 'leaf') n++
    else n += countLeaves(node.children)
  }
  return n
}

function splitScope(scope: string): string[] {
  if (!scope) return [ROOT_BUCKET]
  const parts = scope.split('.').map(s => s.trim()).filter(s => s.length > 0)
  return parts.length === 0 ? [ROOT_BUCKET] : parts
}

function ensureBranch(root: MetricTreeBranch, segments: string[]): MetricTreeBranch {
  let current = root
  let path = ''
  for (const segment of segments) {
    path = path === '' ? segment : `${path}.${segment}`
    let next = current.children.find(
      (c): c is MetricTreeBranch => c.kind === 'branch' && c.label === segment
    )
    if (!next) {
      next = { kind: 'branch', label: segment, path, children: [] }
      current.children.push(next)
    }
    current = next
  }
  return current
}

function sortRecursive(node: MetricTreeBranch): void {
  node.children.sort(compareNodes)
  for (const c of node.children) {
    if (c.kind === 'branch') sortRecursive(c)
  }
}

function compareNodes(a: MetricTreeNode, b: MetricTreeNode): number {
  if (a.kind !== b.kind) return a.kind === 'branch' ? -1 : 1
  return a.label.localeCompare(b.label, undefined, { sensitivity: 'base' })
}
