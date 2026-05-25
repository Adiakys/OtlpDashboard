/**
 * Mints a fresh v4 GUID. Backend columns are `uniqueidentifier` and the
 * deserializer rejects anything that isn't a real GUID, so this function
 * must always return a parseable one.
 *
 * `crypto.randomUUID` is the natural choice, but it's gated by the browser's
 * secure-context rules — it's only defined on HTTPS, `localhost`, and `file://`
 * origins. A production deploy on plain HTTP (e.g. IIS over an intranet
 * hostname) leaves it `undefined`, even though the `crypto` object itself is
 * still present. `crypto.getRandomValues`, on the other hand, is available in
 * every context, so we use it to build a conformant v4 UUID when the
 * convenience API is missing. The pure-`Math.random` branch is only there for
 * truly exotic environments without Web Crypto at all.
 */
export function newGuid(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const b = new Uint8Array(16)
    crypto.getRandomValues(b)
    b[6] = (b[6]! & 0x0f) | 0x40
    b[8] = (b[8]! & 0x3f) | 0x80
    const h = Array.from(b, x => x.toString(16).padStart(2, '0')).join('')
    return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = (Math.random() * 16) | 0
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16)
  })
}
