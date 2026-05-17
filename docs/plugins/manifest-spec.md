# `plugin.json` reference

Every Openhpsdr-Zeus plugin ships with a top-level `plugin.json`. This
document covers every field; the JSON Schema in
[`plugin.schema.json`](plugin.schema.json) is the machine-checkable
truth (validated by the registry CI on every PR).

The minimum viable manifest:

```json
{
  "schemaVersion": 1,
  "id": "com.example.helloworld",
  "name": "Hello World",
  "version": "1.0.0",
  "sdk": { "abi": 1, "minVersion": "1.0.0" },
  "entrypoint": { "assembly": "HelloWorld.dll" }
}
```

## Required fields

### `schemaVersion` (integer, const `1`)
Currently always `1`. Bumped only on incompatible JSON-shape changes.

### `id` (string, reverse-DNS)
Stable, globally unique plugin id. Pattern: `^[a-z][a-z0-9.]*[a-z0-9]$`.
Used as:
- the storage directory under `~/.local/share/zeus/plugins/<id>/`
- the URL prefix for the plugin's backend endpoints
  (`/api/plugins/<id>/...`)
- the LiteDB collection name for the plugin's scoped settings
- the registry entry key

Never change a plugin's id after release — it breaks operator settings.

### `name` (string)
Human-readable name shown in **Settings → Plugins**. Free-form.

### `version` (string, SemVer)
SemVer triple with optional pre-release / build tag:
`1.0.0` / `1.0.0-rc.1` / `1.0.0+exp.sha.5114f85`. The registry refuses
listings whose `versions[].version` doesn't match
`^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$`.

### `sdk.abi` (integer)
Must equal the host's `Zeus.Plugins.Contracts.AbiVersion.Current`.
Currently `1`. Plugins built against any other value are refused at
load with `PluginLoadException`. Bumped only on breaking changes to
the Contracts assembly.

### `sdk.minVersion` (string, strict SemVer)
The minimum host SDK semver the plugin needs. Must satisfy:
- same major as the host's `SdkVersion`
- `<=` the host's `SdkVersion`

Effectively: declare the lowest SDK release whose APIs you use.

### `entrypoint.assembly` (string, plain filename ending in `.dll`)
Relative filename inside the plugin zip. The loader rejects absolute
paths and any `..` segments. The assembly is loaded into a private
`AssemblyLoadContext`; transitive dependencies in the same directory
are resolved by `AssemblyDependencyResolver`.

## Optional fields

### `author` (string)
Free-form attribution. The registry surfaces this on the catalog
card.

### `description` (string)
Plain-text 1–2 sentence summary. Surfaced in **Settings → Plugins**
and the registry card.

### `homepage` (string, URI)
Plugin source repo / docs URL. Surfaced as a clickable link on the
plugin's settings card.

### `license` (string)
[SPDX license identifier](https://spdx.org/licenses/) if possible
(e.g. `"GPL-2.0-or-later"`, `"MIT"`, `"Apache-2.0"`). The registry
rejects plugins whose declared license disagrees with the embedded
LICENSE file's first line.

### `entrypoint.type` (string, fully-qualified .NET type)
Optional. If omitted the loader picks the first public type in the
assembly that implements `IZeusPlugin`. Specify when your assembly
defines multiple `IZeusPlugin`s or you want explicit safety.

```json
"entrypoint": { "assembly": "Amp.dll", "type": "Example.Amp.AmpPlugin" }
```

### `capabilities` (string array)
Capability flags the plugin needs. See
[`capabilities.md`](capabilities.md) for the full list and what each
one gates. Unknown values are silently dropped for forward-compat
(an old host loading a plugin built against a newer SDK).

```json
"capabilities": ["ReadRadioState", "ControlRadio", "NetworkAccess"]
```

`PersistSettings` is granted to every plugin implicitly — list it for
documentation symmetry only.

### `permissions` (object)
Coarser-grained gates. Currently honored:

```json
"permissions": {
  "network": false,
  "fileSystemRead": false,
  "fileSystemWrite": false
}
```

These complement the capability flags (which are typed). The host's
permission model is a future enhancement; for v1 these are advisory
metadata the operator sees in the install prompt.

### `ui` (object)
Frontend contributions. See [`visual-plugins.md`](visual-plugins.md).

```json
"ui": {
  "modules": ["ui/amplifier.es.js"],
  "panels": [
    { "id": "amp.main", "title": "Amplifier", "icon": "Zap", "slot": "workspace.amplifier" }
  ]
}
```

- `modules` — relative paths to ESM JavaScript modules. Each is
  served at `/plugins/<id>/<module-path>` and dynamically imported by
  the Zeus frontend on plugin activation.
- `panels` — declarative panel registrations. The matching React
  component is wired up by the plugin's ESM module via the
  `ZeusPluginApi.registerPanel(...)` call.

Known slot names: `workspace.amplifier`, `settings.plugins.body`,
`topbar.right`. Slot names a plugin doesn't recognise are silently
dropped by the frontend.

### `audio` (object)
Audio chain participation. See [`audio-plugins.md`](audio-plugins.md).

For a plugin that hosts a bundled VST3 with zero C# audio code:

```json
"audio": {
  "vst3Path": "vst3/MyEffect.vst3",
  "slot": "tx.post-leveler",
  "channels": 1,
  "sampleRate": 48000
}
```

- `vst3Path` — relative path inside the plugin zip to the VST3
  bundle or single-file plugin.
- `slot` — where in the chain the plugin lives. Known values:
  `tx.post-leveler`, `tx.pre-cfc`, `rx.post-demod`. Operator-visible
  reorder lands in a later release.
- `channels` — 1 or 2.
- `sampleRate` — 44100, 48000, 88200, or 96000.

For plugins that implement `IAudioPlugin` directly (custom C# DSP),
declare `audio` to opt in to the chain but leave `vst3Path` null.

## Forward-compat rules

Adding a new field is non-breaking — old hosts ignore it. Removing or
changing the semantics of an existing field requires bumping the SDK
ABI. The host honors `schemaVersion` strictly: a manifest with
`schemaVersion: 2` is refused by a v1 host even if every other field
is recognisable.
