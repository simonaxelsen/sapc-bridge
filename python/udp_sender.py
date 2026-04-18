"""
udp_sender.py
==============
Minimal Python → Unity bridge over UDP, using a continuous control signal
in the range [0.0, 1.0].

This script sends a smooth sine-wave signal to Unity on port 5005.
Pair it with the `SAPCReceiver.cs` script attached to a sphere in Unity
to see the sphere "breathe" in real time.

Part of the SAPC (Stroke Adaptive Playback Control) project
  → Real-time biofeedback for early-stage stroke rehabilitation.
  → The core signal processing engine (adaptive spectral asymmetry
    with graceful degradation) will be published after the
    BR41N.io Spring School hackathon (May 2026).

Usage:
    python udp_sender.py
    python udp_sender.py --ip 192.168.1.42 --port 5005 --rate 25
"""

import argparse
import math
import socket
import time


def run(ip: str = "127.0.0.1", port: int = 5005, rate_hz: float = 25.0):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    period = 1.0 / rate_hz
    t = 0.0
    dt = period

    print(f"SAPC UDP bridge → {ip}:{port} @ {rate_hz} Hz")
    print("Ctrl+C to stop.\n")

    try:
        while True:
            # Smooth control signal in [0.0, 1.0]
            value = (math.sin(t) + 1.0) / 2.0
            message = f"{value:.4f}".encode("utf-8")
            sock.sendto(message, (ip, port))

            print(f"→ {value:.4f}", end="\r")
            time.sleep(period)
            t += dt
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        sock.close()


def main():
    parser = argparse.ArgumentParser(description="SAPC minimal UDP sender")
    parser.add_argument("--ip", default="127.0.0.1",
                        help="Target IP (default: localhost)")
    parser.add_argument("--port", type=int, default=5005,
                        help="UDP port (default: 5005)")
    parser.add_argument("--rate", type=float, default=25.0,
                        help="Send rate in Hz (default: 25)")
    args = parser.parse_args()
    run(args.ip, args.port, args.rate)


if __name__ == "__main__":
    main()
