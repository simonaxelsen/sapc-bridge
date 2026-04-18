# SAPC Bridge

**Real-time Python → Unity UDP bridge for brain-computer interface biofeedback.**

Part of the **SAPC (Stroke Adaptive Playback Control)** project — a closed-loop
neurorehabilitation system being developed for the
[g.tec BR41N.io Spring School Hackathon 2026](https://www.br41n.io/).

---

## What this repo contains

A minimal, production-ready communication layer between a Python signal
processing pipeline and a Unity visualization layer, using UDP on port 5005.

- A Python sender that emits a continuous control signal (0.0 to 1.0) at 25 Hz.
- A Unity C# receiver that listens on UDP and maps the value to the scale of
  a GameObject (e.g. a sphere that "breathes" in real time).
- Thread-safe, timeout-aware, and validated — ready for live demos.

This is the **transport layer**. It is hardware-agnostic and can be driven by
any signal source: simulated sine waves, EEG-derived metrics, or any other
continuous value you want to visualize in real time.

> The core signal processing engine — adaptive spectral asymmetry with
> graceful 3-tier degradation — will be published after the BR41N.io Spring
> School hackathon (May 2026).

---

## Why this project exists

In early-stage stroke rehabilitation, patients need biofeedback that is:

- **Always responsive** — even when signal quality is degraded.
- **Low-threshold** — no complex binary decisions the patient must get "right".
- **Continuous** — rewarding effort, not just success.
- **Interpretable** — the therapist always knows what the system is doing.

SAPC is designed around those constraints. The bridge in this repo is the
lightweight glue that connects the brain signal to the visual feedback loop.

---

## Quick start

### 1. Python side

```bash
cd python
pip install -r requirements.txt
python udp_sender.py
```

You should see a stream of values being sent:

```
SAPC UDP bridge → 127.0.0.1:5005 @ 25 Hz
Ctrl+C to stop.

→ 0.8415
```

### 2. Unity side

1. In your Unity project, create a new scene with a `Sphere` GameObject.
2. Copy `unity/SAPCReceiver.cs` into your `Assets/` folder.
3. Drag the script onto the `Sphere` in the Hierarchy.
4. Press Play.

The sphere will expand and contract smoothly, driven by the Python sender.

> **Tip:** enable `Edit → Project Settings → Player → Run In Background`
> so the sphere keeps breathing even when Unity loses focus.

---

## Repo structure

```
sapc-bridge/
├── python/
│   ├── udp_sender.py        # Minimal sine-wave UDP sender
│   └── requirements.txt
├── unity/
│   └── SAPCReceiver.cs      # Unity MonoBehaviour UDP receiver
├── docs/
│   └── architecture.md      # High-level system overview
├── LICENSE
└── README.md
```

---

## Protocol

Each UDP packet contains a single UTF-8 string: a float in the range
`[0.0, 1.0]` with 4 decimal places.

Examples: `0.0000`, `0.5000`, `0.8415`, `1.0000`.

Values outside that range are silently rejected by the receiver.

---

## Roadmap

- ✅ Python ↔ Unity UDP bridge
- ✅ Simulated control signal
- ✅ Robust Unity receiver with cleanup and validation
- 🔄 LSL integration with the Unicorn Hybrid Black (April 2026)
- 🔄 SAPC engine: adaptive spectral asymmetry (April 2026)
- 📅 BR41N.io Spring School Hackathon (May 2026)
- 📅 Open-source release of the full SAPC engine (post-hackathon)

---

## License

MIT — see [LICENSE](LICENSE).

---

## Author

Rafael Castañeda — MD, transitioning into neuroengineering.
Project lead, SAPC (2026 →).
