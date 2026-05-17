# Visual (UI) plugins

Plugins can contribute React components to the Zeus frontend by:

1. Declaring `ui.modules` + `ui.panels` in `plugin.json`.
2. Shipping one or more compiled ESM JavaScript modules inside the
   plugin zip.
3. Optionally implementing `IUiPlugin` to gate visibility at runtime.

The frontend dynamically imports each module after the plugin
activates, hands it a `ZeusPluginApi` object, and renders the
registered components into named slots.

## Manifest

```json
"ui": {
  "modules": ["ui/amplifier.es.js"],
  "panels": [
    {
      "id": "amplifier.main",
      "title": "Amplifier",
      "icon": "Zap",
      "slot": "workspace.amplifier"
    }
  ]
}
```

- `modules` — relative paths inside the zip. Each is served at
  `/plugins/<plugin-id>/<module-path>` and imported via dynamic
  `import()` after the plugin activates.
- `panels` — declarative panel registrations. The matching React
  component is wired up by the plugin's ESM module via
  `ZeusPluginApi.registerPanel(...)`.

## The module contract

Each ESM module's default export receives a `ZeusPluginApi`:

```ts
// ui/amplifier.es.js (compiled from .tsx by vite)
import { AmplifierPanel } from './AmplifierPanel';

export default function register(api: ZeusPluginApi) {
  api.registerPanel({
    id: 'amplifier.main',
    component: (props) => <AmplifierPanel api={api} {...props} />,
  });
}
```

The host calls `register(api)` once per module after plugin activation.

## `ZeusPluginApi` surface

```ts
interface ZeusPluginApi {
  // Bind a React component to a panel id declared in plugin.json.
  registerPanel(spec: { id: string; component: React.ComponentType }): void;

  // Attach a React component into a named slot without declaring it
  // in plugin.json (rare — useful for top-bar pills or floating UIs).
  registerSlotComponent(slot: string, component: React.ComponentType): void;

  // React hook subscribing to radio state. Re-renders the component
  // on freq / mode / MOX change.
  useRadioState(): { frequencyHz: number; mode: string; mox: boolean };

  // Call into the plugin's own backend (IBackendPlugin endpoints).
  // path is relative — `'/status'` becomes
  // `/api/plugins/<this-plugin-id>/status`.
  callBackend(method: string, path: string, body?: unknown): Promise<Response>;

  // Subscribe to plugin-scoped events from the SignalR hub.
  subscribe(eventName: string, handler: (payload: unknown) => void): () => void;
}
```

The exact `ZeusPluginApi` version is exported on `window` as
`ZEUS_PLUGIN_API_VERSION`. Bump-on-breaking-change is in lockstep
with `Zeus.Plugins.Contracts.AbiVersion.Current`.

## Known slot names

Slots are named like `<surface>.<region>`:

| Slot | What renders there |
|---|---|
| `workspace.amplifier` | A pane in the main workspace; multiple plugins can stack. |
| `workspace.tools` | TX-side tools panel (paired with the Mic / TX Audio Tools). |
| `settings.plugins.body` | Inside the **Settings → Plugins** page (below the registry browser). |
| `topbar.right` | Pill at the right side of the top bar. Use sparingly — top bar real estate is precious. |

The frontend ignores unknown slot names. Adding a new slot is
non-breaking — old plugins keep working in the slots they know.

## Building the ESM module

Most plugin authors write TypeScript + JSX and bundle with vite or
esbuild:

```bash
npx vite build --target esnext --format es --outDir dist/ui
```

The output `dist/ui/amplifier.es.js` goes into the plugin zip under
`ui/amplifier.es.js`. Imports of `react` / `react-dom` resolve to the
host's bundled copies — your build should declare these as
externals:

```js
// vite.config.ts
export default {
  build: {
    rollupOptions: {
      external: ['react', 'react-dom'],
    },
  },
};
```

## Theming

Zeus uses CSS custom properties from `zeus-web/src/styles/tokens.css`.
Your plugin's CSS should reference them by name, never raw hex:

```css
.amp-card {
  background: var(--panel-bot);
  border: 1px solid var(--border-mid);
  color: var(--text-primary);
}
.amp-card .accent { color: var(--accent); }
```

The full token list is in the file above. Notable:

- `--panel-top` / `--panel-bot` — chrome gradient.
- `--bg-app` — blue-gray workspace background.
- `--accent` — focus / state blue.
- `--tx` — TX / gain-reduction red.
- `--power` — output-power yellow.

**Do not use the amber `#FFA028`** — that's reserved for the
panadapter trace + meter peak ticks per
`docs/lessons/dev-conventions.md`.

## Hot-reload (dev)

In dev mode (`vite` running), changes to a checked-in plugin's `.tsx`
source under `samples/plugins/*/ui/` are picked up by Zeus's vite
proxy without rebuilding. For zipped plugins installed via the Plugin
Browser, you must rebuild the zip and reinstall.

## Security note

For v1, plugin UI modules run in the same origin as Zeus — they can
`fetch('/api/radio/...')`, read `localStorage`, register event
handlers, etc. This is trust-based, matching the v1 philosophy of
"vetted plugins from the registry, BYOP at operator discretion".

A future revision (post-v1) will iframe-sandbox UI modules and
funnel all host interaction through `postMessage`. Plugin authors
should write code that would still work under a postMessage RPC
(i.e. never reach into the DOM of the host shell directly).
