import { onUnmounted, shallowRef, triggerRef, watch, type Ref } from 'vue'
import {
  forceCenter,
  forceCollide,
  forceLink,
  forceManyBody,
  forceSimulation,
  type Simulation,
  type SimulationLinkDatum,
  type SimulationNodeDatum
} from 'd3-force'
import type { ServiceMapDto } from '~/services/types'

/**
 * Node as carried by the force simulation. d3 mutates `x/y/vx/vy` in
 * place at every tick — Vue is told via `triggerRef` (we use
 * `shallowRef` so component re-renders without per-property reactivity
 * hot loops). `fx`/`fy` are d3's "pin" channels: setting them locks
 * the node at a position; setting back to `null` releases it.
 */
export interface PositionedNode extends SimulationNodeDatum {
  service: string
  kind: 'service' | 'dependency'
  requestCount: number
  errorCount: number
}

export interface PositionedEdge extends SimulationLinkDatum<PositionedNode> {
  fromService: string
  toService: string
  callCount: number
  errorCount: number
}

export interface UseForceLayoutOptions {
  /** Repulsion between nodes — more negative = more spread out. */
  chargeStrength?: number
  /** Target distance between linked nodes. */
  linkDistance?: number
  /** Minimum distance between any two node circles (collision radius). */
  collisionRadius?: number
}

const DEFAULTS: Required<UseForceLayoutOptions> = {
  chargeStrength: -400,
  linkDistance: 130,
  collisionRadius: 44
}

/**
 * Wraps a d3-force simulation in a Vue-friendly composable. The
 * <c>data</c> ref is the source of truth (e.g. fresh from the API);
 * any time it changes, the simulation is restarted with the new
 * graph. Existing node positions are preserved across reloads when a
 * service is still present, so a refresh doesn't reshuffle the
 * layout the user just got used to.
 *
 * Returns shallow refs that the renderer iterates each tick. Because
 * d3 mutates the same node objects in place, we use <c>shallowRef</c>
 * + <c>triggerRef</c> to opt out of deep reactivity — re-renders fire
 * once per simulation tick instead of once per property write.
 */
export function useForceLayout(
  data: Ref<ServiceMapDto>,
  width: Ref<number>,
  height: Ref<number>,
  options: UseForceLayoutOptions = {}
) {
  const opts = { ...DEFAULTS, ...options }

  // Position cache survives reloads so the user's mental model of
  // "Postgres is bottom-right, Redis bottom-left" stays put across
  // a manual refresh that returns the same set of services.
  const positionCache = new Map<string, { x: number; y: number }>()

  const nodes = shallowRef<PositionedNode[]>([])
  const edges = shallowRef<PositionedEdge[]>([])
  let simulation: Simulation<PositionedNode, PositionedEdge> | null = null

  watch(
    () => [data.value, width.value, height.value] as const,
    () => rebuild(),
    { immediate: true, deep: true }
  )

  function rebuild() {
    const w = Math.max(100, width.value)
    const h = Math.max(100, height.value)

    // Build/refresh nodes; preserve cached positions when the
    // service was already present, otherwise drop the node near the
    // centre so the simulation pulls it out organically.
    const newNodes: PositionedNode[] = []
    const byService = new Map<string, PositionedNode>()
    for (const dto of data.value.nodes) {
      const cached = positionCache.get(dto.service)
      const node: PositionedNode = {
        service: dto.service,
        kind: dto.kind,
        requestCount: dto.requestCount,
        errorCount: dto.errorCount,
        x: cached?.x ?? w / 2 + (Math.random() - 0.5) * 40,
        y: cached?.y ?? h / 2 + (Math.random() - 0.5) * 40
      }
      newNodes.push(node)
      byService.set(dto.service, node)
    }

    // Edges reference node objects (not strings) — d3-force expects
    // the actual datum once `forceLink.id(...)` resolves, but we
    // pre-resolve here so consumers get back ready-to-render edges
    // without typing ambiguity.
    const newEdges: PositionedEdge[] = []
    for (const dto of data.value.edges) {
      const source = byService.get(dto.fromService)
      const target = byService.get(dto.toService)
      if (!source || !target) continue
      newEdges.push({
        fromService: dto.fromService,
        toService: dto.toService,
        callCount: dto.callCount,
        errorCount: dto.errorCount,
        source,
        target
      })
    }

    nodes.value = newNodes
    edges.value = newEdges

    simulation?.stop()
    simulation = forceSimulation<PositionedNode, PositionedEdge>(newNodes)
      .force(
        'link',
        forceLink<PositionedNode, PositionedEdge>(newEdges)
          .id(n => n.service)
          .distance(opts.linkDistance)
          .strength(0.5)
      )
      .force('charge', forceManyBody().strength(opts.chargeStrength))
      .force('center', forceCenter(w / 2, h / 2))
      .force('collide', forceCollide<PositionedNode>().radius(opts.collisionRadius))
      .alpha(1)
      .alphaDecay(0.03) // settle in ~80 ticks
      .on('tick', () => {
        triggerRef(nodes)
        triggerRef(edges)
      })
      .on('end', () => {
        // Snapshot positions so the next reload starts where we
        // left off (only when the user hasn't pinned anyone).
        for (const n of newNodes) {
          positionCache.set(n.service, { x: n.x ?? 0, y: n.y ?? 0 })
        }
      })
  }

  /** Pin a node at <c>(x,y)</c> and bump simulation alpha so the
   *  graph reflows around it in real time. Used by the drag handler
   *  in the renderer; release is <see cref="releasePin"/>. */
  function pinAt(node: PositionedNode, x: number, y: number) {
    node.fx = x
    node.fy = y
    simulation?.alphaTarget(0.3).restart()
  }

  function releasePin(node: PositionedNode) {
    node.fx = null
    node.fy = null
    simulation?.alphaTarget(0)
  }

  onUnmounted(() => simulation?.stop())

  return { nodes, edges, pinAt, releasePin }
}
