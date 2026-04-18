# Unity UDP Receiver

Unity MonoBehaviour that listens for a continuous control value over UDP and
maps it to the scale of the GameObject it is attached to.

## Setup

1. Open your Unity project (tested on **Unity 6**, should work on 2021 LTS+).
2. Copy `SAPCReceiver.cs` into your `Assets/` folder.
3. Create or pick a GameObject (e.g. a default `Sphere`).
4. Drag `SAPCReceiver` onto the GameObject in the Inspector.
5. Press **Play**. The script starts a background thread listening on
   UDP port 5005.

## Inspector fields

| Field             | Default | Meaning                                         |
|-------------------|---------|-------------------------------------------------|
| `Port`            | `5005`  | UDP port to listen on                           |
| `Min Scale`       | `0.5`   | Sphere scale when received value is `0.0`       |
| `Max Scale`       | `3.0`   | Sphere scale when received value is `1.0`       |
| `Smoothing Speed` | `8.0`   | Higher = more reactive, lower = smoother        |

## Behaviour

- Receives UTF-8 strings containing a float in `[0.0, 1.0]`.
- Values outside that range, `NaN`, or malformed packets are silently ignored.
- Uses a background thread with a 500 ms receive timeout so the editor can
  always shut down cleanly.
- `Vector3.Lerp` is used per-frame for visual smoothing, hiding any mismatch
  between send rate and render rate.

## Tip

Enable `Edit → Project Settings → Player → Run In Background` so the sphere
keeps responding even when the Unity window loses focus.

## Pairing

The Python counterpart lives in `../python/udp_sender.py`. Start it **after**
pressing Play in Unity.
