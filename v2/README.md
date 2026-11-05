# SteadyCues 2 — clean-sheet rebuild

SteadyCues 2 is a **separate Windows executable**, not the original SteadyCues patched in-place.

## Architecture and design choices

- `Core.cs` — deterministic, hardware-independent acceleration processing with gravity estimation, gyro tilt suppression, direction correction and **separate** sensitivity from visibility.
- `SensorService.cs` — the only device driver adapter; reads local Windows `Accelerometer` and optional `Gyrometer`, never connects to a network, phone or GPS.
- `Overlay.cs` — a click-through, nonactivating, transparent overlay with smooth spring response, without changing the desktop input behavior.
- `Dashboard.cs` — entirely new native Hebrew, right-to-left, responsive WinForms UI with live samples/s, signal values, live preview, orientation and sensitivity controls.
- `Program.cs` — isolated application lifecycle, system-tray control and error logging.
- `tests/EngineTests.cs` — standalone deterministic core tests.

Only this folder's application source files are compiled into the new EXE. The legacy code is **not** linked. The existing icon and Windows application manifest are reused as non-executable assets.

### Why the earlier dots barely reacted

The original used (a) heavily damped activity recognition to enter Automatic mode, (b) relatively small displacement gain, and (c) a slow spring that combined with (a) hid brief acceleration. In V2 the default mode is *Always visible for diagnosis*, movement threshold is immediate, spring response is faster, user gain is exposed directly, and visible sensor rate and processed vector distinguish real sensor problems from animation problems. This is an engineering hypothesis, not hardware validation.

### Caveats

Windows tablets and 2-in-1 laptops may expose the sensor in the screen, base or another axis frame. The direction correction is adjustable, but V2 does not automatically infer which physical component contains the sensor. The laptop must be steady relative to the vehicle; sensor-only motion inference cannot derive absolute speed or reliably predict turns in advance. Automatic mode reacts to changes of acceleration, not speed itself.

### Build

From the repository root, on Windows:

```powershell
./v2/build.ps1
```

Result: `dist-v2/SteadyCues2.exe`. Automated compilation, unit tests and artifact creation run in GitHub Actions.

### Safety and coexistence

- No phone HTTP/TCP server, no GPS.
- No installation, forced persistence, firewall changes or administrator privileges.
- Windows may show a warning for unsigned EXEs.
- Can run alongside the original during inspection, but overlapping cue overlays may be confusing; quit the older app first.
- Diagnostics are local and do not send data outside the computer.
