# Python UDP Sender

Minimal control-signal generator for the SAPC bridge.

## What it does

Sends a smooth sine-wave value in `[0.0, 1.0]` to a given IP and UDP port,
at a configurable rate. The receiver on the Unity side uses that value to
drive a visual parameter (e.g. sphere scale).

## Usage

```bash
python udp_sender.py
python udp_sender.py --ip 192.168.1.42 --port 5005 --rate 25
```

Flags:

| Flag     | Default       | Meaning                         |
|----------|---------------|---------------------------------|
| `--ip`   | `127.0.0.1`   | Target IP (Unity machine)       |
| `--port` | `5005`        | UDP port                        |
| `--rate` | `25`          | Send rate in Hz                 |

## Dependencies

None — standard library only.

## Extending this

To drive Unity from real EEG instead of a sine wave, replace the control-value
loop in `udp_sender.py` with your signal processing pipeline. The contract
with the receiver is simply: send a UTF-8 string containing a float in
`[0.0, 1.0]` per packet.
