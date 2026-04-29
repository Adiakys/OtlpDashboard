/**
 * HTML-escape a string for safe interpolation into AG Charts tooltip renderers
 * (which receive pre-rendered HTML) and v-html sinks. Covers the OWASP-recommended
 * minimum set; do not use for attributes inside `<script>` or `<style>` tags.
 */
const ENTITIES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;'
}

export function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ENTITIES[c]!)
}
