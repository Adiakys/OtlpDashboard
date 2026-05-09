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
 * Group instruments first by `serviceName` (the application that
 * emitted them), then — only when a service is observed under TWO
 * OR MORE distinct `serviceInstanceId` values — by instance as a
 * dedicated nested level. A service with a single instance (or
 * none at all) hangs the scope directly off the service node, so
 * trivial single-process apps don't pay for an extra collapsable
 * level the user gains nothing from. Within an instance / service
 * the layout follows `scopeName` dot segments. Instruments without
 * a service land under `(unknown)`; those without a scope land
 * under `(root)`. Branches sort before leaves within a parent.
 */
export function buildTree(instruments: InstrumentDto[]): MetricTreeNode[] {
  // First pass: count distinct non-null instance ids per service.
  // The instance segment is inserted in the second pass only for
  // services where this count is ≥ 2; everywhere else we flatten,
  // including the mixed case (some instruments with an instance,
  // some without) — there's still a single identity in play, so
  // the extra level adds noise.
  const instancesByService = new Map<string, Set<string>>()
  for (const instrument of instruments) {
    const service = instrument.serviceName?.trim() || UNKNOWN_SERVICE
    const instance = instrument.serviceInstanceId?.trim()
    if (!instance) continue
    let set = instancesByService.get(service)
    if (!set) {
      set = new Set<string>()
      instancesByService.set(service, set)
    }
    set.add(instance)
  }

  const root: MetricTreeBranch = { kind: 'branch', label: '', path: '', children: [] }
  for (const instrument of instruments) {
    const service = instrument.serviceName?.trim() || UNKNOWN_SERVICE
    const instance = instrument.serviceInstanceId?.trim()
    const showInstance = instance != null && instance.length > 0
      && (instancesByService.get(service)?.size ?? 0) >= 2
    const segments = showInstance
      ? [service, instance!, ...splitScope(instrument.scopeName)]
      : [service, ...splitScope(instrument.scopeName)]
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

/**
 * Flatten the (possibly filtered) tree to the underlying instrument
 * list — used by the JSON export so the file mirrors what the user
 * sees in the panel after the search filter is applied.
 */
export function collectInstruments(nodes: MetricTreeNode[]): InstrumentDto[] {
  const out: InstrumentDto[] = []
  function walk(node: MetricTreeNode) {
    if (node.kind === 'leaf') out.push(node.instrument)
    else for (const c of node.children) walk(c)
  }
  for (const n of nodes) walk(n)
  return out
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
