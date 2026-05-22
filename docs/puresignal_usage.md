# PURESIGNAL panel — plain-English tour

PureSignal corrects your PA's distortion. It listens to what comes out of the PA, compares it to what you sent in, and pre-distorts the outgoing signal so the result is clean.

For that to work, the engine needs **feedback samples that span the full drive range** without clipping the ADC. Most of the controls are about making that feedback path healthy.

---

## The dial — "148/256, 58% locked"

How "hot" the feedback signal looks to the engine.

- **Below 128** → too quiet (the engine can't see enough to fit a curve)
- **128–181** → green zone (what you want)
- **Above 181** → too hot (ADC near clipping)
- **Above 256** → ADC clipping; corrections will be garbage

The "% locked" is just the dial position as a percentage. "correcting" means the curve is loaded and being applied to live RF.

## Signal flow — TX → PA → coupler → feedback

A diagram of what's happening, not a control. Lights up when keyed.

- **TX `-0.5 dBFS`** = how close your TX envelope is to the engine's ceiling (HW peak). 0 dBFS = right at the ceiling. Mildly negative = good headroom. Positive = clipping the engine's bin scale.

## Internal vs External coupler

Where the feedback comes from.

- **Internal** = the directional coupler built into the radio (HL2 has one; some boards don't).
- **External (bypass)** = you've tapped the antenna line yourself and fed it back into the RX2 jack. Used when you have a Helmut DC6NY sampler or similar.

For an external sampler, aim for ~-15 to -18 dBm at the RX input at full TX.

---

## Right-hand peaks column

### Observed peak
The largest TX envelope sample the engine has seen recently. Live number.

### HW peak
The **ceiling** you're telling the engine to expect. The engine divides every sample by this number to bin it.

**The rule: HW peak should sit just slightly above observed peak (~5% above).**

- If HW peak is **way above** observed → the top bin never fills → engine can't fit the top of the curve → stuck in COLLECT, no correction.
- If HW peak is **below** observed → samples get dropped → engine fits only the bottom of the curve → bad correction.

Example: observed 0.237, HW peak 0.25 → 5.5% above → ideal.

The little `*` next to it means "you've moved this off the per-board default" (HL2 default is 0.233). **Default** button puts it back.

### Correction (±x.xx dB)
How much the engine is currently massaging the outgoing I/Q to cancel the PA's distortion. The sparkline is the last ~60 samples. **Flat = stable = good.** Movement means conditions are changing.

---

## Auto / Single / Run now / Reset

- **Auto** = keep recalibrating continuously while you transmit. Recommended.
- **Single** = run one calibration, then freeze the curve. Use for "I'm done dialling, lock it in."
- **Run now** = trigger one Single pass. Convenient for forcing a fresh measurement.
- **Reset** = throw away everything and start over.

---

## TIMING card

Things measured in time. Defaults are right for HL2.

- **MOX delay (0.2 s)** — how long after you key down before the engine starts sampling. Lets PA bias settle, relays close, etc.
- **Cal delay (0 s)** — gap between calibration passes. Zero = back-to-back as fast as samples arrive.
- **Amp delay (150 ns)** — compensation for the propagation delay through your PA + filters. Don't touch unless you actually know your PA chain group delay.

---

## HARDWARE card

The "calibration constants" — most users never touch these.

- **HW peak** — see above. The one knob that actually matters here.
- **Ints / Spi (16/256)** — resolution of the correction curve. 16/256 = "0.5 dB resolution," the standard. The other presets trade resolution for compute.
- **Auto-attenuate (Enabled)** — when feedback drifts out of the green zone, automatically adjust the **TX step attenuator** (–28 to +31 dB on HL2) to push feedback back into the window. Leave it on.
- **Relax phase tolerance (Disabled)** — loosen the engine's quality bar. Only turn on if the engine refuses to produce a curve on a noisy/weak PA. Increases the risk of accepting a bad curve.

---

## Status row

- **CALIBRATION CONVERGED · -x.xx dB** = engine has a stable curve loaded and is correcting by that many dB. That's the bottom line — if you see this and stay in the green dial, PureSignal is working.

---

## Two-tone test signal

A built-in test source — two pure sine tones at the carrier offsets you pick (e.g. 700 Hz + 1900 Hz). Used to check on-air IMD with a spectrum analyser or another receiver. Magnitude 0.49 per tone keeps the peak under 1.0 if they momentarily align.

- **2-Tone ON** drops two tones into the TX chain instead of mic audio.
- Use it to make a clean, repeatable IMD pattern for PS to chew on.

---

## TL;DR — the only two things that really matter for day-to-day

1. **HW peak sits ~5% above observed peak.** If it's not, fix HW peak (not auto-attenuate).
2. **Dial number is 128–181.** If it isn't, leave auto-attenuate on and it'll handle it.

Everything else has a sensible default.
