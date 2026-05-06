<script setup lang="ts">
/**
 * SVG renderer for the service map. Force layout is owned by
 * <see cref="useForceLayout"/> — this component is concerned with
 * geometry (where the circle goes, how thick the arrow is) and
 * interaction (drag, zoom, click). Stays in pure SVG + Vue + CSS
 * variables so the graph inherits the rest of the app's palette and
 * dark-mode behaviour without library theming.
 */
import { computed, ref, onMounted, onUnmounted, watch } from 'vue'
import type { ServiceMapDto } from '~/services/types'
import {
  useForceLayout,
  type PositionedEdge,
  type PositionedNode
} from '../composables/useForceLayout'

const props = defineProps<{
  data: ServiceMapDto
  selected?: string | null
}>()

const emit = defineEmits<{
  'select': [service: string]
}>()

const { t } = useI18n()

// Fallback for nodes whose source emitted no `service.name`. Shown on
// the canvas and inside <title> tooltips so empty circles aren't
// silently nameless. Defensive against null even though the DTO type
// is `string` — without it a single null in the API response crashes
// the render function and the entire SVG goes blank.
function displayName(service: string | null | undefined): string {
  return service && service.trim().length > 0 ? service : t('serviceMap.unnamedLabel')
}
function isUnnamedNode(service: string | null | undefined): boolean {
  return !service || service.trim().length === 0
}

// ---- Container size ------------------------------------------------------
const svgRef = ref<SVGSVGElement | null>(null)
const width = ref(800)
const height = ref(600)
let resizeObserver: ResizeObserver | null = null

onMounted(() => {
  if (!svgRef.value) return
  const update = () => {
    const rect = svgRef.value!.getBoundingClientRect()
    if (rect.width > 0) width.value = rect.width
    if (rect.height > 0) height.value = rect.height
  }
  update()
  resizeObserver = new ResizeObserver(update)
  resizeObserver.observe(svgRef.value)
})
onUnmounted(() => resizeObserver?.disconnect())

// ---- Force layout --------------------------------------------------------
const dataRef = computed(() => props.data)
const { nodes, edges, pinAt, releasePin } = useForceLayout(dataRef, width, height)

// ---- Visual scales -------------------------------------------------------
// Sizes/strokes use sqrt scaling to flatten the long tail — a service
// with 10× the traffic shouldn't be 10× the disk, that crowds the
// rest of the graph into invisibility.
const NODE_MIN_R = 18
const NODE_MAX_R = 36
const maxRequest = computed(() => {
  let m = 1
  for (const n of nodes.value) if (n.requestCount > m) m = n.requestCount
  return m
})
function nodeRadius(n: PositionedNode): number {
  const ratio = Math.sqrt(n.requestCount / Math.max(1, maxRequest.value))
  return NODE_MIN_R + ratio * (NODE_MAX_R - NODE_MIN_R)
}

const EDGE_MIN_W = 1
const EDGE_MAX_W = 6
const maxCall = computed(() => {
  let m = 1
  for (const e of edges.value) if (e.callCount > m) m = e.callCount
  return m
})
function edgeWidth(e: PositionedEdge): number {
  const ratio = Math.sqrt(e.callCount / Math.max(1, maxCall.value))
  return EDGE_MIN_W + ratio * (EDGE_MAX_W - EDGE_MIN_W)
}

function nodeColor(n: PositionedNode): string {
  const rate = n.requestCount === 0 ? 0 : n.errorCount / n.requestCount
  if (rate >= 0.05) return 'var(--color-rust-500)'
  if (rate >= 0.01) return 'var(--color-amber-500)'
  return 'var(--color-sage-500)'
}

function edgeColor(e: PositionedEdge): string {
  const rate = e.callCount === 0 ? 0 : e.errorCount / e.callCount
  if (rate >= 0.05) return 'var(--color-rust-500)'
  if (rate >= 0.01) return 'var(--color-amber-500)'
  return 'var(--color-graphite-400)'
}

// ---- Edge geometry: curved arrow shortened by node radius ----------------
// The curve is purely cosmetic (avoids overlapping straight lines for
// bidirectional pairs). Endpoints are pulled back by the target's
// radius so the arrowhead lands on the circle's edge, not its centre.
interface EdgePath {
  d: string
  midX: number
  midY: number
}
function pathFor(e: PositionedEdge): EdgePath {
  // d3 typings model `source/target` as `string | number | NodeDatum`
  // because the input form before force-init is the bare id; once the
  // simulation runs, both are resolved to PositionedNode objects.
  // We always read after init, so the cast is safe.
  const source = e.source as PositionedNode
  const target = e.target as PositionedNode
  const sx = source.x ?? 0
  const sy = source.y ?? 0
  const tx = target.x ?? 0
  const ty = target.y ?? 0
  const dx = tx - sx
  const dy = ty - sy
  const len = Math.hypot(dx, dy) || 1
  const tr = nodeRadius(target) + 4 // arrowhead clearance
  const sr = nodeRadius(source)
  const ux = dx / len
  const uy = dy / len
  const x1 = sx + ux * sr
  const y1 = sy + uy * sr
  const x2 = tx - ux * tr
  const y2 = ty - uy * tr
  // Quadratic curve via a midpoint offset perpendicular to the edge.
  const mx = (x1 + x2) / 2 - uy * 12
  const my = (y1 + y2) / 2 + ux * 12
  return {
    d: `M${x1.toFixed(1)},${y1.toFixed(1)} Q${mx.toFixed(1)},${my.toFixed(1)} ${x2.toFixed(1)},${y2.toFixed(1)}`,
    midX: mx,
    midY: my
  }
}

// ---- Pan + zoom ----------------------------------------------------------
const tx = ref(0)
const ty = ref(0)
const scale = ref(1)
const transform = computed(() => `translate(${tx.value},${ty.value}) scale(${scale.value})`)

function onWheel(e: WheelEvent) {
  if (!svgRef.value) return
  e.preventDefault()
  const delta = -e.deltaY * 0.001
  const nextScale = Math.max(0.3, Math.min(3, scale.value * (1 + delta)))
  // Zoom toward the cursor, not the origin.
  const rect = svgRef.value.getBoundingClientRect()
  const cx = e.clientX - rect.left
  const cy = e.clientY - rect.top
  const k = nextScale / scale.value
  tx.value = cx - (cx - tx.value) * k
  ty.value = cy - (cy - ty.value) * k
  scale.value = nextScale
}

let panFrom: { x: number; y: number; tx: number; ty: number } | null = null
function onBackgroundDown(e: PointerEvent) {
  // Only pan when the background is the click target — clicking a
  // node bubbles up but we don't pan when that happens.
  if (e.target !== svgRef.value) return
  panFrom = { x: e.clientX, y: e.clientY, tx: tx.value, ty: ty.value }
  svgRef.value?.setPointerCapture(e.pointerId)
}
function onBackgroundMove(e: PointerEvent) {
  if (!panFrom) return
  tx.value = panFrom.tx + (e.clientX - panFrom.x)
  ty.value = panFrom.ty + (e.clientY - panFrom.y)
}
function onBackgroundUp(e: PointerEvent) {
  if (!panFrom) return
  panFrom = null
  svgRef.value?.releasePointerCapture(e.pointerId)
}

// ---- Drag a node ---------------------------------------------------------
let draggingNode: PositionedNode | null = null
function onNodeDown(node: PositionedNode, e: PointerEvent) {
  e.stopPropagation()
  draggingNode = node
  pinAt(node, node.x ?? 0, node.y ?? 0)
  ;(e.target as Element).setPointerCapture?.(e.pointerId)
}
function onNodeMove(e: PointerEvent) {
  if (!draggingNode || !svgRef.value) return
  const rect = svgRef.value.getBoundingClientRect()
  const x = (e.clientX - rect.left - tx.value) / scale.value
  const y = (e.clientY - rect.top - ty.value) / scale.value
  pinAt(draggingNode, x, y)
}
function onNodeUp(node: PositionedNode, e: PointerEvent) {
  if (draggingNode !== node) return
  // Distinguish a click (no drag movement) from a real drag: if the
  // node ended on the same fx/fy it started, treat it as click.
  // d3 mutates x/y during a real drag, so we just compare distance.
  releasePin(node)
  draggingNode = null
  ;(e.target as Element).releasePointerCapture?.(e.pointerId)
}

function onNodeClick(node: PositionedNode) {
  emit('select', node.service)
}

// ---- Reset view on data change ------------------------------------------
// When the data set changes wholesale (different range, new focus),
// reset the pan/zoom — keeping the user's old view zoomed onto a
// node that no longer exists is jarring.
watch(() => props.data.nodes.length, () => {
  tx.value = 0
  ty.value = 0
  scale.value = 1
})
</script>

<template>
  <div class="vellum-svc-map">
    <svg
      ref="svgRef"
      class="vellum-svc-map__svg"
      :class="{ 'vellum-svc-map__svg--panning': panFrom }"
      @wheel="onWheel"
      @pointerdown="onBackgroundDown"
      @pointermove="onBackgroundMove"
      @pointerup="onBackgroundUp"
      @pointercancel="onBackgroundUp"
    >
      <defs>
        <!-- One arrow marker per color so the head matches the edge. -->
        <marker
          v-for="color in ['rust', 'amber', 'graphite']"
          :id="`arrow-${color}`"
          :key="color"
          markerWidth="10"
          markerHeight="10"
          refX="8"
          refY="5"
          orient="auto"
          markerUnits="userSpaceOnUse"
        >
          <path
            d="M0,0 L0,10 L10,5 z"
            :fill="`var(--color-${color}-${color === 'graphite' ? '400' : '500'})`"
          />
        </marker>
      </defs>

      <g :transform="transform">
        <!-- Edges -->
        <g class="vellum-svc-map__edges">
          <g v-for="e in edges" :key="`${e.fromService}->${e.toService}`">
            <title>
              {{ e.fromService }} → {{ e.toService }}: {{ e.callCount }} calls,
              {{ e.errorCount }} errors
            </title>
            <path
              :d="pathFor(e).d"
              :stroke="edgeColor(e)"
              :stroke-width="edgeWidth(e)"
              fill="none"
              :marker-end="`url(#arrow-${edgeColor(e).includes('rust') ? 'rust' : edgeColor(e).includes('amber') ? 'amber' : 'graphite'})`"
              opacity="0.85"
            />
          </g>
        </g>

        <!-- Nodes. Services render as circles; dependencies as
             rounded squares so the eye separates "things that emit"
             from "things that get called". Same colour palette
             (sage/amber/rust by error rate), same labels, same hover
             halo — the shape is the only visual cue. -->
        <g class="vellum-svc-map__nodes">
          <g
            v-for="n in nodes"
            :key="n.service"
            :transform="`translate(${n.x ?? 0},${n.y ?? 0})`"
            class="vellum-svc-map__node"
            :class="[
              { 'vellum-svc-map__node--selected': selected === n.service },
              n.kind === 'dependency' ? 'vellum-svc-map__node--dependency' : 'vellum-svc-map__node--service'
            ]"
            @pointerdown="onNodeDown(n, $event)"
            @pointermove="onNodeMove"
            @pointerup="onNodeUp(n, $event)"
            @click="onNodeClick(n)"
          >
            <title>
              {{ displayName(n.service) }} ({{ n.kind }}): {{ n.requestCount }} spans,
              {{ n.errorCount }} errors
            </title>

            <template v-if="n.kind === 'service'">
              <circle
                :r="nodeRadius(n) + 3"
                fill="var(--ui-bg)"
                :stroke="nodeColor(n)"
                stroke-width="0"
                opacity="0"
                class="vellum-svc-map__node-halo"
              />
              <circle
                :r="nodeRadius(n)"
                :fill="nodeColor(n)"
                fill-opacity="0.18"
                :stroke="nodeColor(n)"
                stroke-width="2"
              />
            </template>
            <template v-else>
              <rect
                :x="-(nodeRadius(n) + 3)"
                :y="-(nodeRadius(n) + 3)"
                :width="(nodeRadius(n) + 3) * 2"
                :height="(nodeRadius(n) + 3) * 2"
                rx="6"
                ry="6"
                fill="var(--ui-bg)"
                :stroke="nodeColor(n)"
                stroke-width="0"
                opacity="0"
                class="vellum-svc-map__node-halo"
              />
              <rect
                :x="-nodeRadius(n)"
                :y="-nodeRadius(n)"
                :width="nodeRadius(n) * 2"
                :height="nodeRadius(n) * 2"
                rx="4"
                ry="4"
                :fill="nodeColor(n)"
                fill-opacity="0.18"
                :stroke="nodeColor(n)"
                stroke-width="2"
                stroke-dasharray="3 2"
              />
            </template>

            <text
              text-anchor="middle"
              :y="nodeRadius(n) + 14"
              class="vellum-svc-map__label"
              :class="{ 'vellum-svc-map__label--unnamed': isUnnamedNode(n.service) }"
            >{{ displayName(n.service) }}</text>
            <text
              text-anchor="middle"
              y="4"
              class="vellum-svc-map__count"
            >{{ n.requestCount }}</text>
          </g>
        </g>
      </g>
    </svg>
  </div>
</template>

<style scoped>
.vellum-svc-map {
  position: relative;
  flex: 1;
  min-height: 0;
  overflow: hidden;
  background:
    radial-gradient(circle at 20% 20%, color-mix(in oklab, var(--color-graphite-500) 4%, transparent), transparent 60%),
    var(--ui-bg);
}
.vellum-svc-map__svg {
  width: 100%;
  height: 100%;
  cursor: grab;
  touch-action: none;
  user-select: none;
}
.vellum-svc-map__svg--panning {
  cursor: grabbing;
}
.vellum-svc-map__node {
  cursor: pointer;
}
.vellum-svc-map__node:hover .vellum-svc-map__node-halo {
  opacity: 0.5;
  stroke-width: 4;
}
.vellum-svc-map__node--selected .vellum-svc-map__node-halo {
  opacity: 0.7;
  stroke-width: 5;
}
.vellum-svc-map__label {
  font-family: var(--font-sans);
  font-size: 11px;
  fill: var(--ui-text);
  font-weight: 500;
}
.vellum-svc-map__label--unnamed {
  fill: var(--ui-text-muted);
  font-style: italic;
}
.vellum-svc-map__count {
  font-family: var(--font-mono);
  font-size: 10px;
  fill: var(--ui-text-muted);
  font-variant-numeric: tabular-nums;
  pointer-events: none;
}
</style>
