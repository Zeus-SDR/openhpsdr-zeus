# WDSP Gap Analysis: TX Voice Audio Chain v1 Blocks (Issue #332)

**Status:** Phase 0 research output for issue #332
**Scope:** Gap analysis for the 5 v1 voice audio chain blocks: parametric EQ, compressor, exciter, bass enhancer, and reverb. Inventory of WDSP TX chain primitives available for reuse.

---

## 1. Current WDSP TX Chain Inventory

### 1.1 TX Audio Path (TXA Channel)

The Zeus TX audio chain (TXA channel) operates at 48 kHz mono input and processes through WDSP's TXA block. The insertion point for Phase 1 hand-authored blocks is post-Leveler / pre-CFC per `WdspDspEngine.cs:2354-2369`:

```
Mic Input (48 kHz mono)
  ↓
[Panel Gain] — SetTXAPanelGain1 (operator: -40..+10 dB)
  ↓
[ALC] — SetTXAALCSt (always on; fixed: 1 ms attack, 10 ms decay, +3 dB max)
  ↓
[Leveler/WCPAGC] — SetTXALevelerSt / SetTXALevelerTop (operator: 0..+15 dB max-gain)
  ↓
[**VST SEAM — Phase 1 hand-authored blocks inserted here**]
  ↓
[CFC] — SetTXACFCOMPRun + profile (10-band multiband compressor; operator-tunable)
  ↓
[Output] → fexchange2 modulation → TX IQ
```

### 1.2 WDSP Stages Exposed via P/Invoke

The NativeMethods.cs exposes the following TXA control functions:

| Stage | P/Invoke Functions | Run Toggle | Tuning Functions | Meter Output | Status |
|---|---|---|---|---|---|
| **Mic Gain** | `SetTXAPanelGain1(ch, linear_gain)` | N/A | Direct dB→linear setter | MIC_PK, MIC_AV | Wired; operator-controllable |
| **ALC** | `SetTXAALCSt` / `SetTXAALCMaxGain` / `SetTXAALCAttack` / `SetTXAALCDecay` | `SetTXAALCSt(run)` | MaxGain, Attack, Decay available | ALC_PK, ALC_AV, ALC_GAIN | On; params hardcoded (not operator-tunable) |
| **Leveler** | `SetTXALevelerSt` / `SetTXALevelerTop` | `SetTXALevelerSt(run)` | MaxGain setter available | LVLR_PK, LVLR_AV, LVLR_GAIN | Wired; operator-tunable (0..+15 dB) |
| **Compressor** | `SetTXACompressorRun(ch, run)` | Yes | **None visible**; params opaque | COMP_PK, COMP_AV | Off; no tuning API exposed |
| **EQ** | `SetTXAEQRun(ch, run)` | Yes | **None visible**; params opaque | EQ_PK, EQ_AV | Off; no tuning API exposed |
| **AMSQ (Gate)** | `SetTXAAMSQRun(ch, run)` | Yes | **None visible**; params opaque | (none) | Off; not operator-tuned for SSB |
| **CFC** | `SetTXACFCOMPRun` / `SetTXACFCOMPprofile` / `SetTXACFCOMPPrecomp` / `SetTXACFCOMPPrePeq` / `SetTXACFCOMPPeqRun` | `SetTXACFCOMPRun(run)` | Full profile (10 bands × compression/gain, pre-scalars) | CFC_PK, CFC_AV, CFC_GAIN | Operator-tunable; always post-new-chain |
| **Bandpass** | `SetTXABandpassFreqs` / `SetTXABandpassWindow` / `SetTXABandpassRun` | `SetTXABandpassRun(run)` | Frequency setter | (none) | BP0 always on; coupled to TX filter |
| **CFIR** | `SetTXACFIRRun(ch, run)` | Yes | (none) | (none) | P1: off; P2: on (corrects upsample droop) |
| **PHROT** | `SetTXAPHROTRun(ch, run)` | Yes | (none) | (none) | Off; unclear purpose in TX context |
| **osctrl** | `SetTXAosctrlRun(ch, run)` | Yes | (none) | (none) | Off; unclear purpose |
| **PostGen (TUN/2-tone)** | `SetTXAPostGenRun` / `SetTXAPostGenMode` / `SetTXAPostGenTone*` / `SetTXAPostGenTT*` | Yes | Tone freq/mag setters | (none) | Disabled; used for TUN carrier + two-tone test |

**Key finding:** WDSP exposes only **on/off toggles** for Compressor and EQ (no tuning API). CFC is the only fully parametric multi-band compressor in the TX chain.

### 1.3 Sample Rate and Block Constraints

| Profile | Mic Input | WDSP DSP Rate | IQ Output | Block Size | Notes |
|---|---|---|---|---|---|
| **P1** | 1024@48k | 1024@48k | 1024@48k | 1024 frames | Flat 48 kHz; CFIR off |
| **P2** | 512@48k | 1024@96k | 2048@192k | 512 mic / 1024 DSP / 2048 IQ | 48k→96k→192k upsample; CFIR on |

**Phase 1 blocks must handle:** 48 kHz mono input (VSTseam rate); can optionally run at WDSP's internal DSP rate (96 kHz on P2) if they need higher precision.

---

## 2. Gap Analysis: The 5 v1 Blocks

### 2.1 Block 1: Parametric EQ (8–10 Band)

**v1 Requirement:** Multiband parametric equalization. Likely GUI: slider per band (center freq fixed, gain variable ±12 dB).

#### WDSP EQ Primitive

**Current exposure:** `SetTXAEQRun(ch, run)` only. No tuning API visible in NativeMethods.cs or comments.

**Assessment:** WDSP likely has an EQ stage internally (pihpsdr / Thetis may use it on RX), but the TX-side tuning functions are not P/Invoke'd here. Requires:
1. Audit of WDSP library sources (`wdsp/eqp.c` if available) to confirm what knobs exist
2. If knobs exist: extend NativeMethods + RadioService (150–200 lines)
3. If opaque: HAND-AUTHOR

**Recommendation:** 
- **REUSE (conditional):** IF WDSP exposes 8–10 parametric band setters (likely names: `SetTXAEQBand`, `SetTXAEQGain`, etc.), then extend P/Invoke and wire UI. Effort: **Moderate** (200 lines).
- **HAND-AUTHOR (likely):** If WDSP EQ is opaque or doesn't exist on TX, implement 8–10 Butterworth biquad cascade. Effort: **Significant** (300–400 lines for biquad design + phase/gain compensation).

**CPU Cost (estimate):** 10 biquads @ 48 kHz ~= 30–50 MAC/sample (acceptable on modern CPU).

---

### 2.2 Block 2: Compressor (Single-Stage VCA-style)

**v1 Requirement:** Single broadband compressor. Typical controls: ratio, threshold, attack, release, makeup gain.

#### WDSP Compressor Primitive

**Current exposure:** `SetTXACompressorRun(ch, run)` only. No ratio/threshold/attack/release setters visible.

**Assessment:** WDSP has a Compressor stage (likely `compressor.c`), but like EQ, only the run toggle is exposed. Requires same audit as EQ.

**Recommendation:**
- **REUSE (conditional):** IF WDSP exposes `SetTXACompRatio`, `SetTXACompThreshold`, etc., extend P/Invoke. Effort: **Moderate** (200 lines).
- **HAND-AUTHOR (likely):** If opaque, implement VCA-style soft-knee compressor (peak detector → log domain → soft knee → makeup). Effort: **Significant** (250–350 lines for state machine + time constants).

**CPU Cost (estimate):** VCA compressor ~= 10–20 MAC/sample (acceptable).

---

### 2.3 Block 3: Exciter (Aural Exciter-style Top-End Enhancement)

**v1 Requirement:** Harmonic enhancement in high frequencies (typically 4–8 kHz and upper midrange). Adds "presence" and sizzle to voice.

#### WDSP Harmonic Primitives

**Current exposure:** No explicit harmonic generation or top-end boosting in TXA NativeMethods. RX side has EMNR (noise reduction with psychoacoustic post-processing), but no harmonic synthesis.

**Assessment:** WDSP does not expose a harmonic enhancement or "exciter" function. Must HAND-AUTHOR from scratch.

**Algorithm outline:**
```
1. High-pass filter (e.g., 4 kHz Butterworth high-pass)
2. Soft-clipping or waveshaper (tanh / polynomial) to generate harmonics
3. Level-matched re-injection into main path (mix dry + processed)
Tuning: amount (0–100%), saturation drive, feedback loop
```

**Recommendation:** **HAND-AUTHOR.** Effort: **Moderate–Significant** (150–250 lines for filter + waveshaper + mixing).

**CPU Cost (estimate):** High-pass filter + soft-clip ~= 20–30 MAC/sample (acceptable).

---

### 2.4 Block 4: Bass Enhancer (Psychoacoustic Lower-Octave Synthesis)

**v1 Requirement:** Aphex 204 "Big Bottom" style. Synthesizes upper harmonics of low-frequency content (typically sub-100 Hz fundamentals) so the ear perceives bass depth without the radio transmitting actual lows. Critical for SSB where the radio may not pass sub-200 Hz well.

#### WDSP Bass/Psychoacoustic Primitives

**Current exposure:** 
- `SetRXAEMNRaeRun`, `SetRXAEMNRpost2Run` etc. hint at psychoacoustic noise reduction on RX, but these are post-processing, not synthesis.
- No explicit bass enhancement, harmonic doubler, or octave synthesizer in TXA.

**Assessment:** WDSP does not expose bass-enhancement synthesis. Must HAND-AUTHOR.

**Algorithm outline:**
```
1. Low-pass filter (e.g., 200 Hz cutoff, steep rolloff for sub-200 separation)
2. Harmonic doubler / octave shifter on the low-pass output
   (Pitch-shift by +1 octave, or heterodyne-style synthesis at 2×f)
3. High-pass filtered copy of original (remove true lows)
4. Level-matched re-injection (psychoacoustic level matching ~= -6 dB for octave shift)
5. Mix: attenuated low-pass + doubled/shifted high-octave content back into main path
Tuning: amount (intensity of synthesis, 0–100%), low-pass cutoff (100–300 Hz), makeup level
```

**Complexity:** Requires pitch-shift or harmonic synthesis (more complex than simple saturation). Typical approaches:
- **Phase vocoder variant:** FFT-based pitch shift (heavy CPU, ~100+ MAC/sample). Acceptable but overkill for low-freq synth.
- **Time-domain pitch doubling (heterodyne):** Modulate low-pass signal with 2× frequency sine wave, extract envelope (200–300 MAC/sample). Acceptable.
- **Simplified octave synth:** Just attenuate low-pass and mix (treats as "pseudo-bass": not true harmonics, but mimics presence). Simplest, ~30 MAC/sample.

**Recommendation:** **HAND-AUTHOR.** Start with simplified octave synth (fast enough); prototype with time-domain heterodyne if needed for harmonics. Effort: **Significant** (250–400 lines depending on algorithm choice).

**CPU Cost (estimate):** Simplified (attenuation + mixing): 20–30 MAC/sample. Full heterodyne: 100–150 MAC/sample. Both acceptable on modern CPU.

**⚠️ Key gap:** Bass enhancer is the most algorithmically specific block in the v1 chain. There is **no WDSP primitive** to reuse. Must author from scratch with careful design of the frequency split and harmonic synthesis method.

---

### 2.5 Block 5: Reverb

**v1 Requirement:** Spatial reverb effect. Typical: small room (100–300 ms decay). Used to add warmth / presence to voice SSB.

#### WDSP Reverb Primitives

**Current exposure:** None. No reverb stage in TXA or RXA (Thetis doesn't ship reverb either; it's typically a post-processing effect in the audio interface).

**Assessment:** WDSP does not include reverb. Must use external library or HAND-AUTHOR.

**Options:**
1. **EXTERNAL-DEP (Freeverb GPL):** Widely available, proven algorithm, easy to integrate. ~500 lines of C glue code. Latency ~50 ms (acceptable for SSB). CPU ~= 50–100 MAC/sample moderate-complexity IR.
2. **HAND-AUTHOR (Schroeder reverberator):** Classic Schroeder design (parallel comb + series allpass filters). Algorithm complexity is high but well-documented. Effort: **Significant** (300–400 lines for filter chains + parameter mapping).

**Recommendation:** **EXTERNAL-DEP preferred.** Freeverb is GPL-compatible and battle-tested. If licensing concerns, deferred to sibling doc `01-aethersdr-and-external-deps.md`. Effort to integrate: **Moderate** (200 lines wrapper + parameter mapping).

**CPU Cost (estimate):** Freeverb-like IR ~= 50–100 MAC/sample (modest overhead, acceptable).

---

## 3. Insertion Point and Chain Order

### 3.1 Confirmed Location

Per `WdspDspEngine.cs:2354–2369`:
- **Pre-condition:** Post Leveler (slow auto-gain already running)
- **Post-condition:** Pre-CFC (so operator's final-stage compressor is the authority)
- **Rate:** 48 kHz mono (both P1 and P2 profiles feed mic at 48 kHz; WDSP upsamples downstream)
- **Bypass:** Zero-cost when disabled (volatile-bool short-circuit)

### 3.2 Proposed v1 Order (subject to sign-off)

```
Mic Input (48 kHz)
  ↓
[Panel Gain] — WDSP (operator: -40..+10 dB)
  ↓
[ALC] — WDSP (always on; fast 1 ms attack)
  ↓
[Leveler] — WDSP (operator: 0..+15 dB max-gain slider)
  ↓
**[=== PHASE 1 HAND-AUTHORED BLOCKS START ===]**
  ↓
[EQ] — 8–10 band parametric (REUSE or HAND-AUTHOR TBD)
  ↓
[Compressor] — Single VCA-style (REUSE or HAND-AUTHOR TBD)
  ↓
[Exciter] — Top-end harmonic enhancement (HAND-AUTHOR)
  ↓
[Bass Enhancer] — Psychoacoustic octave synth (HAND-AUTHOR)
  ↓
[Reverb] — Spatial effect (EXTERNAL-DEP or HAND-AUTHOR)
  ↓
**[=== PHASE 1 HAND-AUTHORED BLOCKS END ===]**
  ↓
[CFC] — WDSP multiband (operator-tuned; always post-chain)
  ↓
[Output] → fexchange2 modulation → TX IQ
```

**Rationale:**
- **EQ early:** Shape tone before compression so compressor sees the intended spectrum.
- **Compressor mid-chain:** Narrow dynamic range before final limiting effects.
- **Exciter + Bass:** Harmonic enhancement after dynamic control (so compression doesn't mute the synthesis).
- **Reverb last (in chain):** Adds space to the final compressed/enhanced signal so reflections don't trigger CFC re-compression.
- **CFC final (WDSP):** Operator's preferred multiband limiter; always the ultimate gain authority.

### 3.3 Latency Budget

- **Per-block latency:** ~10 ms (one 1024-sample block @ 48 kHz).
- **Cumulative:** Panel Gain (0) + ALC (0) + Leveler (0) + EQ (5 ms filter state) + Compressor (0) + Exciter (0) + Bass (5 ms filter state) + Reverb (50 ms tail) + CFC (0) + CFIR (0) **≈ 60 ms total**.
- **Acceptable?** Yes, typical for radio audio chains. Report to operator in UI (Thetis does this).

---

## 4. Reusability Scoring Summary

| Block | WDSP Available? | Classification | Estimated Effort | Notes |
|---|---|---|---|---|
| **Parametric EQ** | Toggle only; tuning opaque | REUSE (if audit succeeds) OR HAND-AUTHOR | Moderate (if REUSE) / Significant (HAND-AUTHOR) | Requires WDSP library audit for band setters |
| **Compressor** | Toggle only; tuning opaque | REUSE (if audit succeeds) OR HAND-AUTHOR | Moderate (if REUSE) / Significant (HAND-AUTHOR) | Requires WDSP library audit for ratio/threshold |
| **Exciter** | None | HAND-AUTHOR | Moderate (150–250 lines) | Simple waveshaper + high-pass; no dependencies |
| **Bass Enhancer** | None | HAND-AUTHOR | Significant (250–400 lines) | Requires careful psychoacoustic algorithm; most complex block |
| **Reverb** | None | EXTERNAL-DEP preferred / HAND-AUTHOR fallback | Moderate (EXTERNAL-DEP) / Significant (HAND-AUTHOR) | Freeverb integration recommended; defer licensing to sibling doc |

---

## 5. Open Questions for Sign-Off

> **Q1: Are EQ and Compressor tuning functions available in the WDSP library?**
>
> **Action:** Audit `wdsp/eqp.c` and `wdsp/compressor.c` (or equivalent) for band/ratio/threshold setters. If found, extend NativeMethods + RadioService to expose them. If not, mark HAND-AUTHOR and allocate implementation time.

> **Q2: For Bass Enhancer, which psychoacoustic algorithm should Phase 1 target?**
>
> Options:
> - Simplified octave synth (attenuation + mixing, ~30 MAC/sample) — fast to implement, less authentic.
> - Time-domain heterodyne (modulation-based frequency doubling, ~100–150 MAC/sample) — more authentic harmonics.
> - FFT-based pitch shift (heavy but highest quality, ~100+ MAC/sample) — overkill for low-freq synth.
>
> **Recommendation:** Start with simplified octave synth for Phase 1; prototype full heterodyne in Phase 1.1 if operator feedback warrants. Coordinate with psychoacoustics SME if available.

> **Q3: Should Reverb be sourced from an external library (Freeverb GPL) or hand-authored?**
>
> **Action:** Defer licensing audit to sibling doc `01-aethersdr-and-external-deps.md`. If GPL-2.0-or-later is acceptable, recommend Freeverb integration. If custom required, plan HAND-AUTHOR (300–400 lines Schroeder reverberator).

> **Q4: What is the approved v1 chain order and bypass strategy?**
>
> **Current proposal:** EQ → Compressor → Exciter → Bass → Reverb → CFC. Each block individually bypassable? Master bypass?
>
> **Action:** Confirm order and UI model (per-block toggles vs. master chain enable).

> **Q5: Should hand-authored blocks run at 48 kHz or WDSP's DSP rate (96 kHz on P2)?**
>
> **48 kHz (simpler):** Matches VST seam rate; less CPU; ~5% precision loss on P2. **96 kHz (better):** Matches internal WDSP DSP rate; more CPU; full precision.
>
> **Recommendation:** Prototype at 48 kHz for Phase 1. Optimize to DSP rate in Phase 1.1 if CPU budget allows.

> **Q6: Does Phase 1 wire each v1 block behind an IZeusAudioPlugin abstraction (contract), or directly into DspPipelineService?**
>
> **Action:** Gated on Brian's `feature/plugins-foundation` landing. Phase 1 may proceed with direct wiring; Phase 1.1 refactors behind plugin contract once available.

---

## 6. File References

| File | Line Range | Content |
|---|---|---|---|
| `WdspDspEngine.cs` | 2354–2369 | VST seam insertion point; insertion rationale |
| `WdspDspEngine.cs` | 1140–1350 | TXA channel initialization; ALC/Leveler/CFC setup |
| `WdspDspEngine.cs` | 2432–2510 | TX meter snapshot (LVLR_PK/AV/GAIN, CFC_PK/AV/GAIN, etc.) |
| `NativeMethods.cs` | 542–752 | All TXA P/Invoke bindings (SetTXA*, GetTXA*) |
| `IDspEngine.cs` | 145–189 | TX public interface (SetTxPanelGain, SetTxLevelerMaxGain, SetTxMode, ProcessTxBlock, etc.) |
| `RadioService.cs` | 1286–1307 | SetCfc public method; radio-layer CFC wiring |
| `ZeusEndpoints.cs` | 386–407 | `/api/mic-gain` and `/api/tx/leveler-max-gain` REST endpoints |
| `tx-store.ts` | 50–290 | Frontend TX state (micGainDb, levelerMaxGainDb, cfcConfig, metering) |
| `vst-host-phase2-wire.md` | — | VST IPC seam placement and contract |

---

**Generated:** 2026-05-17
**Analyst:** Claude Code (Phase 0 research)
