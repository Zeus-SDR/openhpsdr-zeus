// SPDX-License-Identifier: GPL-2.0-or-later
//
// Runtime that loads installed-plugin UI modules at app startup. The
// host serves each module at /api/plugins/{id}/ui/{file}; we
// dynamic-import them, hand the module's default export a small API
// surface (registerPanel + callBackend), and capture every panel the
// plugin registers so it can show up in the workspace Add Panel modal.

import type { ComponentType } from 'react';
import { fetchInstalledPlugins, type PluginDto, type PluginPanelDto } from '../api/plugins';

export interface ZeusPluginApi {
  registerPanel(spec: { id: string; component: ComponentType }): void;
  callBackend(method: string, path: string, body?: unknown): Promise<Response>;
}

export interface RegisteredPluginPanel {
  panelId: string;
  pluginId: string;
  title: string;
  icon: string;
  category: string;
  // Manifest's panel slot — e.g. "workspace.amplifier" for an Add-Panel
  // tile, "tx-audio-tools.chain" for a TX audio-chain block. Consumers
  // filter on this when rendering plugin contributions into a specific
  // surface (the chain panel below CFC, for instance).
  slot: string;
  component: ComponentType;
}

type PluginModule = {
  default?: (api: ZeusPluginApi) => void;
};

const registered = new Map<string, RegisteredPluginPanel>();
const listeners = new Set<() => void>();
let loaded = false;
let loading: Promise<void> | null = null;

function emit() {
  for (const fn of listeners) fn();
}

function makeApi(plugin: PluginDto, registerPanel: (spec: { id: string; component: ComponentType }) => void): ZeusPluginApi {
  return {
    registerPanel,
    callBackend(method, path, body) {
      const url = `/api/plugins/${encodeURIComponent(plugin.id)}${path.startsWith('/') ? path : '/' + path}`;
      const init: RequestInit = { method };
      if (body !== undefined) {
        init.headers = { 'content-type': 'application/json' };
        init.body = JSON.stringify(body);
      }
      return fetch(url, init);
    },
  };
}

async function loadOne(plugin: PluginDto): Promise<void> {
  if (!plugin.ui || plugin.ui.modules.length === 0) return;

  const panelMetaById = new Map<string, PluginPanelDto>();
  for (const p of plugin.ui.panels) panelMetaById.set(p.id, p);

  const captured = new Map<string, ComponentType>();
  const register = (spec: { id: string; component: ComponentType }) => {
    captured.set(spec.id, spec.component);
  };
  const api = makeApi(plugin, register);

  for (const modulePath of plugin.ui.modules) {
    const url = `/api/plugins/${encodeURIComponent(plugin.id)}/${modulePath.replace(/^\/+/, '')}`;
    try {
      const mod = (await import(/* @vite-ignore */ url)) as PluginModule;
      if (typeof mod.default === 'function') {
        mod.default(api);
      } else {
        console.warn(`[plugin/${plugin.id}] module ${modulePath} has no default export`);
      }
    } catch (err) {
      console.error(`[plugin/${plugin.id}] failed to load ${modulePath}`, err);
    }
  }

  for (const [panelId, component] of captured) {
    const meta = panelMetaById.get(panelId);
    if (!meta) {
      console.warn(`[plugin/${plugin.id}] registered panel '${panelId}' not declared in manifest`);
      continue;
    }
    registered.set(`${plugin.id}::${panelId}`, {
      panelId,
      pluginId: plugin.id,
      title: meta.title,
      icon: meta.icon,
      category: meta.category,
      slot: meta.slot,
      component,
    });
  }
}

export async function loadInstalledPluginUis(): Promise<void> {
  if (loading) return loading;
  loading = (async () => {
    const list = await fetchInstalledPlugins();
    await Promise.all(list.plugins.map(loadOne));
    loaded = true;
    emit();
  })();
  return loading;
}

export function listRegisteredPanels(): RegisteredPluginPanel[] {
  return Array.from(registered.values());
}

export function isLoaded(): boolean {
  return loaded;
}

export function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}
