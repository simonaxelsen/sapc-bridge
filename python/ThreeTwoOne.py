"""
unicorn_udp_bridge.py
─────────────────────
Reads raw EEG from the Unicorn Hybrid Black via UnicornPy (direct g.tec API),
computes SAPC / CZ / GLOBAL, normalises to 0-1, and sends UDP to Unity.

Requirements:
    pip install numpy
    UnicornPy ships with Unicorn Suite — see path note below.

Unicorn channel layout (17 total per frame):
    EEG 0-7: Fz, C3, Cz, C4, Pz, PO7, Oz, PO8
    8-10: Accel X/Y/Z  |  11-13: Gyro X/Y/Z
    14: Counter  |  15: Battery  |  16: Validation

Computed values (all → 0-1):
    SAPC   = (C3-C4)/(|C3|+|C4|+ε)  mapped from [-1,1]
    CZ     = Cz  via rolling percentile normalisation
    GLOBAL = mean(C3,Cz,C4) via rolling percentile normalisation

UDP format → Unity:  "SAPC=0.500 | CZ=0.342 | GLOBAL=0.471"
"""

import sys, os, socket, time, threading, collections
import numpy as np

# ── UnicornPy location ────────────────────────────────────────────────────────
# UnicornPy.pyd ships with Unicorn Suite. Add its folder to the path:
# Always add the script's own folder first (handles .pyd sitting next to the script)
_script_dir = os.path.dirname(os.path.abspath(__file__))
if _script_dir not in sys.path:
    sys.path.insert(0, _script_dir)

# Also try the default Unicorn Suite install path
UNICORN_PY_PATH = r"C:\Program Files\gtec\Unicorn Suite\Unicorn Apps\Unicorn Python"
if os.path.isdir(UNICORN_PY_PATH) and UNICORN_PY_PATH not in sys.path:
    sys.path.insert(0, UNICORN_PY_PATH)

try:
    import UnicornPy
except ImportError:
    print("ERROR: UnicornPy not found.")
    print(f"  Expected location: {UNICORN_PY_PATH}")
    print("  Or copy UnicornPy.pyd next to this script.")
    sys.exit(1)

# ── Config ────────────────────────────────────────────────────────────────────
UNITY_IP      = "127.0.0.1"
UNITY_PORT    = 1000
SEND_RATE_HZ  = 30
FRAMES_PER_GET = 4          # samples pulled per UnicornPy call
BUFFER_SEC    = 3.0         # rolling window for percentile normalisation

# Channel indices inside the EEG block
IDX_C3, IDX_CZ, IDX_C4 = 1, 2, 3
TOTAL_CHANNELS = UnicornPy.TotalNumberOfChannels  # 17

NORM_LO_PCT = 5
NORM_HI_PCT = 95
EPSILON     = 1e-6

# ── Shared state ──────────────────────────────────────────────────────────────
_lock   = threading.Lock()
_latest = {"sapc": 0.5, "cz": 0.5, "global": 0.5}

# ── Running percentile normaliser ─────────────────────────────────────────────
class RunningNorm:
    def __init__(self, cap):
        self._buf = collections.deque(maxlen=cap)
    def push(self, v):
        self._buf.append(v)
        if len(self._buf) < 20:
            return 0.5
        lo = np.percentile(self._buf, NORM_LO_PCT)
        hi = np.percentile(self._buf, NORM_HI_PCT)
        if hi - lo < EPSILON:
            return 0.5
        return float(np.clip((v - lo) / (hi - lo), 0.0, 1.0))

# ── EEG acquisition thread ────────────────────────────────────────────────────
def eeg_thread(device, srate):
    cap  = int(BUFFER_SEC * srate)
    n_cz = RunningNorm(cap)
    n_gl = RunningNorm(cap)

    frame_bytes = TOTAL_CHANNELS * FRAMES_PER_GET * 4  # float32
    buf = bytearray(frame_bytes)

    print(f"[bridge] Acquiring at {srate} Hz, {TOTAL_CHANNELS} ch")

    while True:
        try:
            device.GetData(FRAMES_PER_GET, buf, frame_bytes)
        except Exception as e:
            print(f"[bridge] GetData error: {e}")
            time.sleep(0.01)
            continue

        data = np.frombuffer(buf, dtype=np.float32).reshape(FRAMES_PER_GET, TOTAL_CHANNELS)

        for frame in data:
            c3, cz, c4 = frame[IDX_C3], frame[IDX_CZ], frame[IDX_C4]

            sapc_raw = (c3 - c4) / (abs(c3) + abs(c4) + EPSILON)
            sapc_01  = (sapc_raw + 1.0) / 2.0

            cz_01  = n_cz.push(float(cz))
            gl_01  = n_gl.push(float((c3 + cz + c4) / 3.0))

            with _lock:
                _latest["sapc"]   = sapc_01
                _latest["cz"]     = cz_01
                _latest["global"] = gl_01

# ── UDP sender thread ─────────────────────────────────────────────────────────
def udp_thread():
    sock     = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    interval = 1.0 / SEND_RATE_HZ
    print(f"[bridge] Sending UDP → {UNITY_IP}:{UNITY_PORT} @ {SEND_RATE_HZ} Hz")
    while True:
        with _lock:
            s, c, g = _latest["sapc"], _latest["cz"], _latest["global"]
        msg = f"SAPC={s:.3f} | CZ={c:.3f} | GLOBAL={g:.3f}\n"
        try:
            sock.sendto(msg.encode(), (UNITY_IP, UNITY_PORT))
        except Exception as e:
            print(f"[bridge] UDP error: {e}")
        time.sleep(interval)

# ── Main ──────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    available = UnicornPy.GetAvailableDevices(True)
    if not available:
        print("ERROR: No Unicorn device found. Is it paired and on?")
        sys.exit(1)

    serial = available[0]
    print(f"[bridge] Connecting to {serial} ...")
    device = UnicornPy.Unicorn(serial)
    srate  = int(UnicornPy.SamplingRate)   # 250 Hz

    device.StartAcquisition(False)         # False = no test signal
    print("[bridge] Acquisition started.")

    t_eeg = threading.Thread(target=eeg_thread, args=(device, srate), daemon=True)
    t_udp = threading.Thread(target=udp_thread, daemon=True)
    t_eeg.start()
    t_udp.start()

    print("[bridge] Running — Ctrl-C to stop.\n")
    try:
        while True:
            time.sleep(1)
            with _lock:
                print(f"  SAPC={_latest['sapc']:.3f}  "
                      f"CZ={_latest['cz']:.3f}  "
                      f"GLOBAL={_latest['global']:.3f}")
    except KeyboardInterrupt:
        pass
    finally:
        print("\n[bridge] Stopping acquisition...")
        device.StopAcquisition()
        del device
        print("[bridge] Done.")