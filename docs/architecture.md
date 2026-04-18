# SAPC — System Architecture (high-level)

This document describes the architecture at a high level. It intentionally
omits the core signal processing details, which will be released after the
BR41N.io hackathon (May 2026).

---

## Three-layer design

SAPC is organized in three decoupled layers that communicate over UDP:

```
┌─────────────────────────────┐
│  Intelligence layer         │
│  Obsidian · Plaud · Gemini  │     ← research, transcripts, documentation
└─────────────────────────────┘

┌─────────────────────────────┐
│  Signal processing layer    │
│  Python · MNE · NumPy       │     ← EEG → control signal [0.0, 1.0]
│  Unicorn Hybrid Black (LSL) │
└─────────────┬───────────────┘
              │ UDP :5005 (continuous float)
              ▼
┌─────────────────────────────┐
│  Visualization layer        │
│  Unity 3D · C#              │     ← sphere responds in real time
└─────────────────────────────┘
```

This repo contains the **UDP bridge** between layers 2 and 3.

---

## Why UDP

UDP was chosen over TCP and WebSockets for three reasons:

1. **Latency** — no handshake, no retransmission overhead.
2. **Loss tolerance** — for continuous biofeedback, a dropped packet is
   instantly replaced by the next one ~40 ms later. Retransmission would
   only add jitter.
3. **Simplicity** — every platform, every language, every firewall-free
   local network supports it trivially.

The bridge runs on `127.0.0.1:5005` by default. Both endpoints live on the
same machine to minimize latency — the clinician's laptop runs Python and
Unity side by side.

---

## Signal contract

Between Python and Unity, the only agreement is:

- **Value range**: `[0.0, 1.0]`, inclusive.
- **Encoding**: UTF-8 string of a float with 4 decimal places (e.g. `0.7531`).
- **Rate**: nominally 25 Hz, but the receiver is rate-agnostic.
- **Semantics**: `0.0` = minimum control, `1.0` = maximum control. The
  mapping from this value to visual parameters (sphere scale, color, etc.)
  is entirely the responsibility of the visualization layer.

This clean contract is what allows the signal processing engine to evolve
(different metrics, fallback modes, machine learning additions) without
touching Unity at all.

---

## Latency budget

Target end-to-end latency: **< 150 ms** from muscle-intent EEG onset to
visible sphere response.

| Stage                                | Typical latency |
|--------------------------------------|-----------------|
| EEG acquisition (Unicorn → LSL)      | ~20 ms          |
| Python signal processing             | ~50 ms          |
| UDP transport (localhost)            | < 1 ms          |
| Unity receive + render frame         | ~16 ms @ 60 fps |
| **Total**                            | **~90–100 ms**  |

Well within the perceptual threshold for "direct control" feeling.

---

## What's next

- Integrate LSL input from the Unicorn Hybrid Black (April 2026).
- Open-source the SAPC engine after the hackathon (May 2026).
- Extend to clinical pilot studies during PhD work (ETH Zürich, from
  September 2026).
