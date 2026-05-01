import type { InjectionKey, Ref } from 'vue'
import type { FQKind } from './types'

/**
 * Injection key used by `WidgetConfigSlot` to publish the fully-qualified
 * kind of the widget being edited. Engine-specific forms (e.g. the HTML
 * template engine) inject it to look up the source `WidgetDefinition`
 * for read-only metadata such as the embedded template / spec; forms
 * that don't care just don't inject. Kept out of the components folder
 * so producers and consumers can import without pulling Vue SFCs into
 * each other's compile graph.
 */
export const WIDGET_KIND_INJECTION_KEY = Symbol('widget-kind') as InjectionKey<Ref<FQKind>>
