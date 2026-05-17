# Audio Voice Chain — UX & Integration Design (Phase 0)

Status: Research proposal (awaiting maintainer sign-off)
Authors: KB2UKA
Date: 2026-05-17
Companion: [01-rfc.md](./01-rfc.md), [02-block-catalog.md](./02-block-catalog.md)

This document proposes how the v1 audio processing chain (5 blocks: EQ, Compressor, Exciter, Bass Enhancer, Reverb) slots into Zeus's existing UI vocabulary and integrates with the WDSP DSP engine. It makes no architectural commitments — all visual and wire-level choices are flagged for Brian (EI6LF) sign-off.

---

## 1. Design Language Analysis

### 1.1 Global Palette (source of truth: `tokens.css`)

Zeus's visual design is a **near-black beveled-panel aesthetic** matching the Hermes Lite 2 hardware front panel. The operator reads the interface as *lit instruments on a dark bench*.

**Palette summary:**
- **Workspace background:** `--bg-app` (#0a0a0c) — app chrome backdrop; same as workspace canvas so panels "lift off" the background via `--bg-1` (#111114).
- **Panel base:** `--bg-1` (#111114) — the raised surface all controls sit on; gives ~3dB visual lift over workspace.
- **Control surfaces:** `--bg-2` (#1a1a1e) for button rest, `--bg-3` (#232328) for hover. These are incremental lifts.
- **Deepest wells:** `--bg-inset` (#060608) for panadapter, gauge backgrounds; and `--bg-meter` (#050507) for LED meter wells — "lit instruments in dark recesses."
- **Accent:** `--accent` (#0c5f9c, dark blue) for UI focus and active state. When active, buttons adopt `--accent` fill + white text. This is the ONLY global UI accent; never use it to tint backgrounds.
- **TX/gain-reduction:** `--tx` (#ff4a59, warm red) — semantic meaning "you are transmitting" or "gain reduction active." Never a mere decoration.
- **Power output:** `--power` (#ffb13c, warm yellow) — meter peak indicator and output-power dial.
- **Signal visualization:** Amber `#FFA028` (via `--immersive-warn` fallback) applied **by varying alpha only**, never by hue shift. Low signal = dim; high signal = full. This matches the panadapter trace color.
- **Foreground text:** `--fg-0` (#ffffff) for headings, `--fg-1` (#cccccc) for body, `--fg-2` (#8a8a90) for muted labels, `--fg-3` (#5a5a60) for extra-muted borders.
- **Lines & separators:** `--line` (#1f1f23) for panel dividers, `--line-soft` (#16161a) for subtle background lines, `--line-strong` (#2c2c32) for stronger definition.

**Critical rule:** All color must come from `tokens.css` variables. Raw hex is forbidden except where the token system is unavailable (e.g., inline SVG `fill` attributes that can't reference CSS vars — those use the canonical hex constants from the token file). Do NOT invent new colors.

### 1.2 Panel Structure Vocabulary

Existing panels (CFC, DspPanel, TxFilterPanel) establish three control patterns:

#### Header with master enable/disable
CFC and DspPanel both begin with a **labeled toggle** that turns the whole feature on or off:
```
[✓ toggle]  <LABEL>    [optional: subtitle or tagline]
```

The toggle is a native HTML checkbox styled per `tokens.css` button semantics (flat `--bg-2`, `--accent` when checked). Label sits to the right. The font is `--font-sans` (Inter) at 12px body weight, 400–600 depending on prominence.

#### Section dividers
CFC uses `var(--line)` horizontal rules to separate logical sections. Padding is 12px above and below.

#### Control rows (slider + label)
Slider is a native `<input type="range">` styled per `tokens.css`: dark `--line` track, white round thumb with shadow. Label on the left at 12px `--fg-1`. Optional unit suffix (e.g. "dB", "ms", "Hz") to the right in `--fg-3`.

#### Preset / preset-chip selector
CFC exposes preset chips as flat buttons. When active, the chip shows `--accent` fill + white text. Inactive chips are `--bg-2` with `--fg-1` text.

#### Advanced / expandable sections
A right-chevron disclosure button (`▶ Advanced`) that toggles a conditional render. The main controls stay visible; tertiary controls hide by default.

### 1.3 Meter Primitives

Zeus has meter widget types suitable for audio-chain monitoring:

1. **HBarMeter** — horizontal bar with numeric readout, scale ticks, and peak-hold tick. Used in TX Stage Meters. Great for gain reduction (shows compressor action) and level monitors. Fill gradient: good (green) → warn (yellow) → tx (red). Label on the left, readout on the right.

2. **Activity LED** — small indicator light (16×16 px), filled with `--ok` (green) or `--fg-4` (dark gray). Used for binary on/off states (e.g. exciter is active, bass enhancer is synthesizing).

**Meter-per-block recommendation (v1 blocks):**
- **EQ:** Optional graphical EQ-curve display (Phase 1 or Phase 2 open question). No real-time meter required for v1.
- **Compressor:** HBarMeter for gain reduction (GR). Shows how much the compressor is pushing down the signal. Gradient: good (no GR) → tx (heavy GR).
- **Exciter:** Activity LED (green when adding harmonic content, gray when silent/off). Optional: small spectrum zoom on upper band (Phase 2).
- **Bass Enhancer:** Activity LED (green when synthesizing bass harmonics, gray when idle). Meter shows energy in the synthesized band (Phase 2).
- **Reverb:** No dedicated meter (reverb tail is audible, difficult to meter visually in v1).

---

## 2. Chain Panel Proposal (v1: 5 blocks)

### 2.1 Where the panel lives

**Decision: Tab in the Settings menu, under "TX Audio Tools"** (alongside CFC and VST host).

In the browser:
```
Settings menu (gear icon)
  └─ TX Audio Tools (tab)
      ├─ CFC Settings Panel
      ├─ Audio Voice Chain Panel  ← NEW (v1: EQ, Comp, Exciter, Bass, Reverb)
      └─ VST Host Submenu (if available)
```

### 2.2 Top-level chain structure

```
┌─────────────────────────────────────────────────────┐
│  [✓ Chain]    Audio Voice Chain    [Presets ▼]      │
├─────────────────────────────────────────────────────┤
│  Flow visualization:                                 │
│  [MIC] → [EQ] → [Comp] → [Exciter] → [Bass] → [Rev] │
│          ↑       ↑        ↑           ↑       ↑      │
│         ON      ON       OFF         OFF      OFF    │
│                                                      │
│  Block 1: Parametric EQ (10 bands)      [bypass]    │
│  ├─ Band 1 (100 Hz):   __________ dB, Q: ____       │
│  ├─ Band 2 (200 Hz):   __________ dB, Q: ____       │
│  └─ [Show more bands ▼] [Advanced ▶]                │
│                                                      │
│  Block 2: Compressor                    [bypass]    │
│  ├─ Threshold: __________ dB                        │
│  ├─ Ratio:     __________ :1                        │
│  ├─ Attack:    __________ ms  [Advanced ▶]          │
│  └─ GR Meter:  [███░░░░░░░░░░░]  −2.4 dB            │
│                                                      │
│  Block 3: Exciter                       [bypass]    │
│  ├─ Frequency: __________ Hz                        │
│  ├─ Amount:    __________ %    [Activity indicator] │
│                                                      │
│  Block 4: Bass Enhancer                 [bypass]    │
│  ├─ Split freq: __________ Hz                       │
│  ├─ Amount:     __________ %    [Activity indicator]│
│                                                      │
│  Block 5: Reverb                        [bypass]    │
│  ├─ Size:  __________ %                             │
│  ├─ Decay: __________ ms        [Advanced ▶]        │
│  ├─ Mix:   __________ % (Dry/Wet)                   │
│                                                      │
└─────────────────────────────────────────────────────┘
```

**Chain enable toggle (top-left):** Labeled `[✓ Chain]` or `[✓ Audio Voice Chain]`. When disabled, all blocks report "OFF" in the flow visualization but retain their settings.

**Flow visualization (below header):** A single-line ordered diagram showing all 5 blocks' on/off status at a glance. Shows:
- Block order left → right: `[MIC] → [EQ] → [Comp] → [Exciter] → [Bass] → [Rev] → [OUT]`.
- Status indicator (small colored dot) per block: bright (`--accent` or `--ok`) for on, dim `--fg-4` for off.
- Clicking a status indicator toggles that block's enable without opening its settings.

**Preset dropdown (top-right):** Label "Presets" with a `▼` chevron. Opens a dropdown with predefined chains:
- "Default" (all off, chain disabled)
- "Podcast voice" (EQ + Compressor tuned for clarity, Exciter subtle, Bass/Reverb off)
- "SSB contest" (light EQ, fast compressor, all others off)
- "FT8 macro" (minimal coloration, light compression only)
- "Custom" (shown when the operator edits away from a preset)

Preset selection applies all block settings at once. Operator can then tweak and the label reverts to "Custom".

**Persistence story:** Per the established server-authoritative pattern (from `project_drive_tune_persistence`), chain config persists to `zeus-prefs.db` via `RadioStateStore`. Browser fetches the saved config on connect; operator edits push via `/api/tx/audio-chain` (new endpoint, Phase 1). Preset library lives on the server.

### 2.3 Per-block layout

Each block is a collapsible card with header and body:

#### Header
```
[✓ toggle]  Parametric EQ          [bypass]  [Advanced ▶]
```

- Left: checkbox toggle (enable/disable this block).
- Center: block name (e.g. "Parametric EQ", "Compressor"). 12px `--font-sans` bold.
- Right: two buttons:
  - "Bypass" button: toggles per-block bypass independently of block enable. Styled as a small flat button; when active (bypass ON), shows `--tx` red background to indicate block is off.
  - "Advanced ▶" disclosure button (if the block has secondary controls): right-chevron, 10px. Click to expand the Advanced section.

#### Body: Parametric EQ (10 bands)

Primary display: 10 rows, one per band (100 Hz, 200 Hz, 300 Hz, 500 Hz, 750 Hz, 1 kHz, 2 kHz, 3 kHz, 5 kHz, 8 kHz — standard modern voice frequencies).

```
Band 1 (100 Hz):   Gain [slider] dB    Q [input] ___
Band 2 (200 Hz):   Gain [slider] dB    Q [input] ___
...
Band 10 (8 kHz):   Gain [slider] dB    Q [input] ___
```

- Gain: ±12 dB range, centered at 0 dB (flat). Label "Gain", unit "dB".
- Q (quality factor): 0.5–4.0 range (broader to narrower), default 1.0. Label "Q", no unit (it's a ratio).
- Gain slider takes up most width; Q is a smaller input box to the right (read numeric value, not a slider, for precision).

**Open question for sign-off:** Should the v1 EQ expose a **graphical curve display** (showing all 10 band filters overlaid on a dB-vs-Hz graph)? This would be Phase 1 or Phase 2 work. For v1, the operator adjusts by ear (slider per band, no visualization). Recommend deferring the curve to Phase 1.

#### Body: Compressor

```
Threshold: [slider]       dB
Ratio:     [slider]       :1
Attack:    [Advanced ▶]   ms
Release:   [Advanced ▶]   ms
Makeup gain: [Advanced ▶] dB

GR Meter:  [████░░░░░░░░░░]  −2.4 dB
```

- Threshold: −60 to 0 dB range, default −18 dB. Label "Threshold", unit "dB".
- Ratio: 1:1 to 8:1 range, default 3:1. Label "Ratio", unit ":1".
- Attack: 10 ms to 1 s range, default 50 ms. Hidden in Advanced section.
- Release: 50 ms to 2 s range, default 300 ms. Hidden in Advanced section.
- Makeup gain: ±12 dB, default 0 dB (operated calculates from ratio on some plugins, but we expose it for operator override). Hidden in Advanced section.

**Meter:** HBarMeter showing gain reduction in real-time. Label "GR" (gain reduction), readout in dB (0 dB = no GR, −X dB when compressing).

#### Body: Exciter

```
Frequency: [slider]    Hz
Amount:    [slider]    %

[Activity: ● (green when active, gray when silent)]
```

- Frequency: 2 kHz to 12 kHz range, default 5 kHz. Controls the center of the harmonic boost. Label "Frequency", unit "Hz".
- Amount: 0–100%, default 20%. Controls the intensity of the added harmonics. Label "Amount", unit "%".

**Indicator:** Small activity LED. Green when the exciter is adding noticeable harmonic content (signal energy above the boost frequency); gray when silent or amount is 0.

#### Body: Bass Enhancer

```
Split frequency: [slider]  Hz
Amount:          [slider]  %

[Activity: ● (green when synthesizing, gray when idle)]
```

- Split frequency: 40 Hz to 200 Hz range, default 100 Hz. The boundary below which psychoacoustic bass synthesis is applied. Label "Split freq", unit "Hz".
- Amount: 0–100%, default 30%. Controls the intensity of synthesized bass harmonics. Label "Amount", unit "%".

**Indicator:** Small activity LED. Green when the bass enhancer is synthesizing noticeable harmonics; gray when idle.

**Design note:** The bass enhancer is conceptually hard to understand (Aphex 204 / MaxxBass synthesizes harmonics of low-frequency content, so the TX bandwidth can stay narrow while the operator *hears* bass). The UI should be minimal: two controls, one indicator. Avoid technical jargon in labels.

#### Body: Reverb

```
Size:  [slider]     %
Decay: [Advanced ▶] ms
Mix:   [slider]     % (Dry/Wet)
```

- Size: 0–100%, default 30%. "Room size" in Freeverb terminology — larger = longer tail.
- Decay: 0.5 s to 5 s range, default 2 s. Time for the reverb tail to decay to silence. Hidden in Advanced.
- Mix (Dry/Wet): 0–100%, default 15% (subtle on TX, as convention). At 0%, reverb is off (signal is dry). At 100%, signal is fully wet (reverb only, no dry voice).

**No preset selection** for reverb types (spring/plate/hall). In v1, the operator uses one reverb algorithm; presets are handled by the preset library (e.g. preset "Podcast voice" applies the full chain with reverb disabled).

**Design note:** On TX, reverb is traditionally subtle (15–30% mix) to avoid sounding cavernous. Sensible default: Mix = 15%, Size = 30%, Decay = 2 s. Operator can increase if they want a dramatic effect, but the default shouldn't scare them.

#### Advanced section (hidden by default)

Example (Compressor Advanced):
```
┌─ Advanced ▼
│  Attack:       [slider]  ms
│  Release:      [slider]  ms
│  Makeup gain:  [slider]  dB
└─ (end Advanced section)
```

Styled identically to main controls but indented or in a sub-card with `--bg-2` background for visual grouping. Typography and spacing match the body section.

---

## 3. Integration: WDSP Insertion Point

### 3.1 TX block-processing seam

**Location:** `Zeus.Dsp/Wdsp/WdspDspEngine.cs`, method `ProcessTxBlock`, lines 2354–2369.

Current code structure (simplified):
```csharp
// Line ~2354–2369: VST plugin-host seam
if (_vstChainEnabled)
{
    ProcessTxMicVstChain(iin, inSize, _txaInputRateHz);  // mic buffer in-place mutation
}

NativeMethods.fexchange2(txa, ref iin[0], ref qin[0], ref iout[0], ref qout[0], out int err);
// ... (TX-stage meters, monitor, analyzer follow)
```

**The WDSP call sequence at this seam:**

1. **Input:** `iin` (float array, mono mic buffer, length `inSize`)
   - Size depends on the radio profile: P1 = 1024 samples (48 kHz, ~21 ms), P2 = 2048 samples (96 kHz, ~21 ms).
   - Sample rate: always 48 kHz for the mic input (`_txaInputRateHz` is the source-of-truth).
   - Ownership: Zeus owns the buffer; audio-chain blocks are allowed to mutate in-place.

2. **WDSP TXA processing:** `fexchange2(txa, &iin[0], &qin[0], &iout[0], &qout[0], &err)`
   - Takes the mic buffer `iin`, IQ buffers `qin`, `iout`, `qout`.
   - Internal WDSP stages (in order per TXA.c):
     - Meter (MIC_PK, MIC_AV)
     - Equalizer (if enabled)
     - Leveler (xleveler, always enabled in Zeus)
     - Compressor (if enabled; always off in shipped Zeus)
     - CFC (Continuous Frequency Compressor, if enabled)
     - Pre-distortion (if PureSignal enabled, P2 only)
     - IQ output (CFIR re-sampling)
   - Output: `iout`, `qout` (IQ complex samples, ready for TX modulation)

### 3.2 Proposed integration hook signature

**Placement:** Between VST host seam (line ~2364) and `fexchange2` call (line ~2371).

```csharp
// New audio-chain seam (line 2370, before fexchange2)
if (_audioChainEnabled)
{
    ProcessAudioChain(iin, inSize, _txaInputRateHz);  // mic buffer in-place or new buffer
}
```

**Hook signature (proposed):**
```csharp
private void ProcessAudioChain(
    float[] iin,           // input: mono mic buffer (in-place mutation OK)
    int frameCount,        // sample count in iin (typically 1024 for P1, 2048 for P2)
    int sampleRateHz)      // 48000 (always, for mic input)
```

**Buffer semantics:**
- Input buffer `iin` is shared with the caller (ProcessTxBlock). The chain MAY mutate it in-place.
- No output buffer; the chain writes back to `iin` (same ownership as VST host).
- Ownership remains with ProcessTxBlock; the chain does not allocate or free.
- Thread-safety: called from the WDSP worker thread (inside `_txaLock`). The chain must not block or allocate on the TX path.

### 3.3 Per-block processing order (v1) and wire contract

The chain processes blocks in this order:

```
MIC input (48 kHz mono float32, frameCount samples)
  ↓
[1] Parametric EQ (10-band shelving + peaking)
  ↓
[2] Compressor (VCA-style ratio-based gain reduction)
  ↓
[3] Exciter (harmonic enhancement at selectable frequency)
  ↓
[4] Bass Enhancer (psychoacoustic synthesis of low-frequency harmonics)
  ↓
[5] Reverb (room simulation, subtle on TX)
  ↓
[Output to fexchange2 → TX modulation]
```

**Open question for sign-off:** Is this the canonical v1 block order? Should any blocks be reordered? For example, some engineers prefer Reverb before Compressor (so the compressor doesn't "hear" the reverb tail). Current proposal: EQ → Comp → Exciter → Bass → Reverb. Signal this is an open question if the order should differ.

**Per-block contract:**
- Each block receives a float buffer (either in-place or a work buffer).
- Each block outputs the same buffer (mutated in-place).
- No allocations per block, per frame. All per-block state (meters, coefficients, IIR history) is pre-allocated in the initializer.
- Blocks are lockfree; no cross-block communication during audio processing.

### 3.4 Meter & telemetry reporting

Each block exposes read-only meters via an immutable snapshot:

```csharp
public struct AudioChainBlockMetrics
{
    public float InputLevel { get; }        // input level (dBFS)
    public float OutputLevel { get; }       // output level (dBFS)
    public float GainReduction { get; }     // for compressor (dB, ≤ 0)
    public bool IsActive { get; }           // for exciter, bass, reverb (true when producing effect)
}
```

Meters are sampled once per DSP tick and pushed to the frontend via SignalR (same pattern as existing TX Stage Meters). Sample rate: ~48 packets/sec (one per 21 ms block at P1).

### 3.5 Thread-safety and CPU budget

**Thread:** Runs on the WDSP worker thread inside `ProcessTxBlock`, under `_txaLock`. No cross-thread coordination needed; the chain is a single-threaded hot path.

**CPU budget per block (v1 proposal):**
- **EQ (10 bands):** 3–4% per tick (10 biquad IIR filters cascaded).
- **Compressor:** 2–3% per tick (envelope follower + gain curve, makeup gain).
- **Exciter:** 2–3% per tick (narrow bandpass filter on input, harmonic synthesizer).
- **Bass Enhancer:** 3–4% per tick (psychoacoustic analysis, harmonic synthesis in low band).
- **Reverb:** 6–8% per tick (Freeverb, largest per-block cost in v1).

**Total budget (all 5 blocks, all on):** ~16–22% per tick, leaving headroom for WDSP's core TX path (CFC, leveler, CFIR resampling ≈ 15–20%), panadapter render (~5%), and radio protocol overhead.

Real-world Zeus TX today runs ~14–34% single-core on a 12-core M-series Mac (from `project_drive_tune_persistence` telemetry). Adding the chain should push peak to ~35–45%, still well below saturation on modern systems.

---

## 4. Operator Workflow & First-Run Experience

### 4.1 Discovery and initial setup

**First time an operator opens Settings > TX Audio Tools:**
- CFC panel is visible (always available, can be off).
- Audio chain panel appears below it.
- Chain is **disabled by default** (master toggle OFF).
- All 5 blocks are visible in the flow visualization but disabled individually.

**Operator flow (minimal friction):**
1. Click the chain master toggle to enable.
2. Preset dropdown defaults to "Default (all off)". Operator can pick "Podcast voice" or "SSB contest" for sensible starting point.
3. Operator tunes the compressor threshold while monitoring the GR meter.
4. Operator enables per-block bypass to compare "with chain" vs. "without."

### 4.2 Sensible defaults (v1)

When the chain is first enabled, all blocks start with neutral (no-effect) settings:
- **EQ:** All 10 bands at 0 dB gain, Q = 1.0 (flat response).
- **Compressor:** Ratio 3:1, Threshold −18 dB, Attack 50 ms, Release 300 ms, Makeup 0 dB (net effect: light to medium compression). GR meter reads 0 dB (idle).
- **Exciter:** Frequency 5 kHz, Amount 0% (off, no effect). Activity LED gray.
- **Bass Enhancer:** Split freq 100 Hz, Amount 0% (off). Activity LED gray.
- **Reverb:** Size 30%, Decay 2 s, Mix 0% (off, all dry).

This ensures the operator hears **zero audible change** on first enable (all blocks except compressor at neutral; compressor is light enough to be subtle). Presets then offer opinionated starting points:
- "Podcast voice": EQ sculpted for clarity, compressor moderate, exciter subtle (30%), bass enhancer subtle (20%), reverb off.
- "SSB contest": EQ minimal, compressor fast & tight, all others off.
- "FT8 macro": light compression only, all others off.

### 4.3 Persistence

**Server-authoritative model** (established pattern in `project_drive_tune_persistence`):
- Chain config (enabled, preset name, per-block settings, per-block bypass state) persists to `zeus-prefs.db` via the RadioStateStore.
- Browser sends PUT requests to `/api/tx/audio-chain` with the full chain state.
- On reconnect, browser fetches the saved config via `/api/tx/audio-chain` (GET) and renders the UI in the saved state.

**Preset persistence:**
- Preset library (names + block configs) also lives in `zeus-prefs.db`.
- Operator can save the current chain state as a new named preset ("My SSB EQ", "Field day settings", etc.).
- Presets are immutable once saved; editing reverts to "Custom" and allows the operator to save a new variant or overwrite a preset.

### 4.4 A-B comparison affordance (bypass & quick toggle)

**Per-block bypass:** Each block's header has a "Bypass" button. When active (highlighted in `--tx` red), the block is skipped on the audio path (input feeds directly to output, no processing). Independent of the block's enable toggle and allows quick A/B comparison during TX.

Use case: Operator enables the compressor and monitors GR meter. To compare "with compression" vs. "without," they toggle the Bypass button repeatedly without losing settings.

**Whole-chain bypass:** The main chain header also has a "Bypass" button. When active, all blocks are skipped (entire chain is pass-through). Allows flip between "pristine mic" and "processed mic" with one click.

---

## 5. CPU Budget Justification

WDSP TXA fires at the radio sample rate. For the two common profiles:

- **P1 (Protocol 1, 48 kHz):** 1024 frames per block ≈ 21 ms per tick.
- **P2 (Protocol 2, 96 kHz):** 2048 frames per block ≈ 21 ms per tick.

Existing Zeus TX pipeline (per 21 ms block):
- WDSP TXA leveler + CFC: ~15–20% single-core CPU.
- Mic input / output buffering: ~1%.
- TCP egress (protocol push): ~2–3%.
- Panadapter render + WebGL: ~5%.

**Proposed v1 chain add-on budget:**
- EQ (10 bands) + Compressor + Exciter + Bass Enhancer + Reverb: ~16–22% cumulative.

**Total TX CPU post-chain:** ~40–48% single-core (worst case, all blocks active at once).

On the test machine (12-core M3 Max Mac), baseline TX is 14–34% single-core. Adding the chain brings peak to ~40–48%, still **below saturation** on a modern multi-core system. Headroom remains for disk I/O, UI updates, and radio protocol housekeeping.

If in-field telemetry later shows sustained > 80% single-core usage, blocks can be disabled by default (e.g., reverb off, bass enhancer off; EQ + comp on). The architecture supports this tuning without code changes.

---

## 6. Open Questions for Maintainer Sign-off

> **Open question for sign-off:** Should the audio-chain panel be **collapsible (accordion) within TxAudioToolsPanel**, or a **separate scrollable section** below CFC? The current proposal assumes separate section (better mobile responsiveness, less nesting). If Brian prefers accordion (fewer visual sections), I can revise the layout sketch accordingly.

> **Open question for sign-off:** The flow visualization (line diagram of 5 blocks with on/off indicators) is a novel UI pattern in Zeus. Is this the right level of abstraction? Alternative: hide the flow diagram by default and only show it on hover (less visual clutter, more detail on demand). Or: replace it with a simple "X blocks active" counter badge?

> **Open question for sign-off:** Should the **Parametric EQ expose a graphical curve display** (all 10 band filters overlaid on a dB-vs-Hz graph)? This would be Phase 1 or Phase 2 work. For v1, the operator adjusts by ear (slider per band, no visualization). Recommend deferring the curve to Phase 1 or Phase 2 based on operator feedback.

> **Open question for sign-off:** Per-band **Q factor (quality) in the EQ** — should operators edit it (current proposal), or should it be fixed per band for simplicity? Recommend allowing operator tuning (more flexibility, matches modern EQ UX), but confirm with Brian.

> **Open question for sign-off:** The v1 block order: **EQ → Comp → Exciter → Bass → Reverb.** Should any blocks be reordered? For example, some engineers prefer Reverb before Compressor (so the compressor doesn't "hear" the reverb tail). Is the proposed order sensible for voice TX?

> **Open question for sign-off:** Preset persistence: should presets be **per-band** (one set of presets shared across all bands, applied to whatever is selected) or **per-band-stashed** (band A has "SSB contesting", band B has "FT8 macro", each band remembers its own preset)? Current proposal is per-band-stashed (more flexible). Confirm with Brian.

> **Open question for sign-off:** The bass enhancer is conceptually hard to understand (Aphex 204 / MaxxBass). Should we add a brief tooltip or blurb explaining what it does (e.g. "Synthesizes low-frequency harmonics for perceived bass without transmitting extreme lows")? Or keep the UI minimal (2 controls, 1 indicator) and rely on presets to demonstrate the effect?

> **Open question for sign-off:** UI color for the exciter / bass enhancer activity indicators — should they use `--ok` (green, "effect is active") or something more neutral? The red `--tx` is wrong (that's "transmitting", not "activity"). Current proposal: `--ok` green when active, `--fg-4` dark gray when idle. Confirm with Brian.

---

## 7. Design System Compliance

All colors reference `tokens.css` variables. No raw hex.

All fonts: `--font-sans` (Inter) for UI, `--font-mono` (JetBrains Mono) for numerics. Per PR #327.

All spacing: 8px or 12px grid. No arbitrary padding.

All controls: use existing patterns (sliders, toggles, buttons) from CFC and DspPanel.

Meter styling: reuse `HBarMeter` from the immersive-meters package. Same fill gradient (good → warn → tx) and LED segment lines as TX Stage Meters.

---

## Summary

The v1 audio voice chain (5 blocks: Parametric EQ, Compressor, Exciter, Bass Enhancer, Reverb) slots into Zeus's existing TX Audio Tools tab as a collapsible section with a master enable toggle and a flow visualization showing all five blocks' on/off status. Each block is a card with primary controls, optional meters (HBarMeter for compressor GR, activity LEDs for exciter/bass), and an Advanced disclosure for secondary settings. Presets ("Podcast voice", "SSB contest", "FT8 macro") allow one-click setup with sensible defaults.

The integration point in `WdspDspEngine.ProcessTxBlock` is between the VST host seam and the WDSP `fexchange2` call. The chain processes a single 48 kHz mono float buffer in-place, respecting the same buffer ownership and thread-safety constraints as the existing VST host. Per-block CPU budget totals ~16–22% per 21 ms tick (worst case, all on), leaving headroom in the current 14–34% TX baseline.

All visual design defers to existing tokens and established panel vocabulary (CFC, DspPanel). No new colors, fonts, or layout patterns are introduced. Six open questions are flagged for Brian's sign-off: accordion vs. section layout, flow diagram prominence, EQ graphical curve display timing, block order, preset stashing strategy, and bass enhancer UI explanation.

