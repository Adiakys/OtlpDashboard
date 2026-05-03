/**
 * Tiny seeded PRNG so the demo is deterministic per (seed) — the same
 * URL with the same query gives the same data every visit, no flicker on
 * refresh. mulberry32 is a 32-bit hash-based PRNG; not crypto-grade,
 * which is exactly right for fixture data.
 */
export function mulberry32(seed: number): () => number {
  let s = seed >>> 0
  return function next() {
    s = (s + 0x6d2b79f5) >>> 0
    let t = s
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

/**
 * Stable 32-bit hash of a string. Used to derive a seed from a path /
 * query so every endpoint is deterministic without sharing global state.
 */
export function hashString(input: string): number {
  let h = 2166136261 >>> 0
  for (let i = 0; i < input.length; i++) {
    h ^= input.charCodeAt(i)
    h = Math.imul(h, 16777619) >>> 0
  }
  return h >>> 0
}

/** Gaussian sample (Box-Muller) — mean 0, std-dev 1. */
export function gaussian(rand: () => number): number {
  const u = Math.max(rand(), 1e-9)
  const v = rand()
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v)
}

/** Uniform on [lo, hi). */
export function range(rand: () => number, lo: number, hi: number): number {
  return lo + (hi - lo) * rand()
}

/** Pick one item uniformly. */
export function pick<T>(rand: () => number, items: readonly T[]): T {
  return items[Math.floor(rand() * items.length)]!
}

/**
 * Weighted pick. Weights don't need to sum to 1 — they're normalised
 * implicitly. Returns the value associated with the chosen weight.
 */
export function pickWeighted<T>(
  rand: () => number,
  items: readonly { value: T; weight: number }[]
): T {
  const total = items.reduce((s, i) => s + i.weight, 0)
  let acc = rand() * total
  for (const item of items) {
    acc -= item.weight
    if (acc <= 0) return item.value
  }
  return items[items.length - 1]!.value
}
