"""
lsl_receiver.py
===============
Reads EEG data from Lab Streaming Layer (LSL) and sends a normalized
control value to Unity via UDP.

The Unicorn Hybrid Black streams EEG over LSL. This script:
1. Connects to the LSL EEG stream
2. Reads the latest sample
3. Computes a simple signal amplitude metric
4. Sends it as a float [0.0, 1.0] to Unity

Usage:
    python lsl_receiver.py
    python lsl_receiver.py --stream "Unicorn" --ip 127.0.0.1 --port 5005
"""

import argparse
import socket
import time
import sys

def find_lsl_stream(stream_name=None, timeout=10):
    try:
        from pylsl import resolve_byprop, StreamNotFoundError
    except ImportError:
        print("ERROR: pylsl not installed. Run: pip install pylsl")
        sys.exit(1)

    print(f"Searching for LSL stream{(': ' + stream_name) if stream_name else ''}...")
    streams = resolve_byprop("type", "EEG", timeout=timeout)

    if not streams:
        print("No EEG streams found. Is the Unicorn connected and streaming?")
        sys.exit(1)

    if stream_name:
        for s in streams:
            if stream_name.lower() in s.name().lower():
                return s
        print(f"Stream '{stream_name}' not found. Available: {[s.name() for s in streams]}")
        sys.exit(1)

    return streams[0]

def compute_control_value(sample, sampling_rate=250):
    """
    Simple metric: compute RMS (root mean square) of the signal
    as a proxy for signal strength/engagement.

    This is a placeholder — the real SAPC engine will use
    adaptive spectral asymmetry. For now, we just normalize
    the RMS to [0.0, 1.0] based on typical EEG ranges.
    """
    import numpy as np
    if len(sample) < 1:
        return 0.5

    rms = np.sqrt(np.mean(np.array(sample) ** 2))

    eeg_min = 0.1
    eeg_max = 50.0

    normalized = (rms - eeg_min) / (eeg_max - eeg_min)
    normalized = max(0.0, min(1.0, normalized))

    return normalized

def run(stream_name=None, ip="127.0.0.1", port=5005, rate_hz=25.0):
    from pylsl import StreamInlet

    stream = find_lsl_stream(stream_name)
    print(f"Connected to: {stream.name()}")
    print(f"  Channels: {stream.channel_count()}")
    print(f"  Sampling rate: {stream.nominal_srate()} Hz")

    inlet = StreamInlet(stream)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    period = 1.0 / rate_hz

    print(f"\nSending to {ip}:{port} @ {rate_hz} Hz")
    print("Ctrl+C to stop.\n")

    try:
        while True:
            sample, timestamp = inlet.pull_sample(timeout=1.0)

            if sample is not None:
                value = compute_control_value(sample, stream.nominal_srate())
                message = f"{value:.4f}".encode("utf-8")
                sock.sendto(message, (ip, port))
                print(f"EEG signal → {value:.4f}   \r", end="")
            else:
                print("Waiting for data...                          \r", end="")

            time.sleep(period)

    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        sock.close()
        inlet.close_stream()

def main():
    parser = argparse.ArgumentParser(description="SAPC LSL → Unity bridge")
    parser.add_argument("--stream", default=None,
                        help="LSL stream name to connect to (default: first EEG stream)")
    parser.add_argument("--ip", default="127.0.0.1",
                        help="Unity IP address (default: localhost)")
    parser.add_argument("--port", type=int, default=5005,
                        help="UDP port (default: 5005)")
    parser.add_argument("--rate", type=float, default=25.0,
                        help="Send rate in Hz (default: 25)")
    args = parser.parse_args()
    run(args.stream, args.ip, args.port, args.rate)

if __name__ == "__main__":
    main()