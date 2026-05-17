# Plugin capabilities

Capabilities are typed permission flags a plugin declares in its
manifest. The host grants each capability on first load (user prompt)
and surfaces it as a non-null service on `IPluginContext`. Plugins
that try to use an ungranted capability either get a `null` service
(and should null-check) or — if they reflect over the context — see
a `PluginPermissionException`.

The enum lives in `Zeus.Plugins.Contracts/PluginCapabilities.cs`.

## The full list

| Flag | What it grants | Risk |
|---|---|---|
| `PersistSettings` | Plugin-scoped key/value storage via `IPluginContext.Settings`. Backed by a private LiteDB collection (`plugin_<id>_*`). Implicit — granted to every plugin. | Low. Storage is per-plugin; cross-plugin reads return `default`. |
| `ReadRadioState` | Non-null `IPluginContext.Radio` exposing current frequency, mode, band, MOX + change events. | Low. Read-only; no operator-visible side effects. |
| `ControlRadio` | Non-null `IPluginContext.RadioController` for `SetFrequencyAsync` / `SetModeAsync` / `SetMoxAsync`. | **High.** A buggy plugin can key the transmitter or jump bands. Reviewer scrutiny required. |
| `AudioStream` | Plugin can implement `IAudioPlugin` and contribute to the TX/RX audio chain. Realtime: no allocation, no locks, no IO inside `Process()`. | **High.** A misbehaving plugin glitches the on-air audio. |
| `NetworkAccess` | Plugin may open outbound sockets / HTTP clients. | Medium. Plugins can phone home, exfiltrate data. Operators see this in the install prompt. |
| `FileSystemRead` | Plugin may read files outside its own directory. | Medium. Plugin can read configs, logs, sometimes credentials. |
| `FileSystemWrite` | Plugin may write files outside its own directory. | Medium-high. Can corrupt operator data; can chain with read for credential exfiltration. |

A plugin's own directory (`IPluginContext.PluginRootPath`) is always
read/write without declaring `FileSystem*` — that's its own data
turf. The flags above only gate access outside that root.

## Declaring capabilities

In `plugin.json`:

```json
"capabilities": ["ReadRadioState", "ControlRadio"]
```

Names are PascalCase, exactly as in the enum. Unknown names are
silently dropped (forward-compat for plugins built against a newer
SDK).

## How the host honors capabilities

`IPluginContext.GrantedCapabilities` reflects the intersection of:
- what the manifest declared
- what the user granted at first load (for v1, "first load auto-grants
  everything declared"; an interactive prompt is iter-9 polish)

Ungranted capabilities surface as `null` on the context:

```csharp
public Task InitializeAsync(IPluginContext ctx, CancellationToken ct)
{
    if (ctx.RadioController is null)
    {
        ctx.Logger.LogWarning("ControlRadio not granted — read-only mode");
    }
    return Task.CompletedTask;
}
```

This is intentional: the plugin gracefully degrades to "do the
read-only subset" instead of crashing. Reflection-based access (via
`PluginPermissionException`) is a backstop for plugins that don't
null-check.

## Practical guidance

- **Declare the minimum set** your plugin actually uses. Operators
  see the union in the install prompt; smaller asks land faster.
- **Don't declare `ControlRadio` "just in case"** — it's a red flag
  in registry review. Add it later in a minor version bump if you
  legitimately need it.
- **`AudioStream` implies a realtime contract**. The host calls your
  `Process()` on the audio thread; allocation will glitch the
  transmitter. See [`audio-plugins.md`](audio-plugins.md).

## Future

The capability set is intentionally small for v1. A planned iter-2
revision adds:

- `ReadCredentials` — controlled access to specific credential keys
  the operator pre-approves.
- `ControlPaSettings` — write access to per-band PA gain / max-watts
  values (currently behind `ControlRadio` for safety).
- Per-capability rate limits + audit logs.

Adding a new capability is non-breaking: old plugins keep working,
they just can't use the new feature without declaring it.
