# VirtualDesktop.VRCFaceTracking
VRCFaceTracking module for Virtual Desktop

This is my fork of [guygodin/VirtualDesktop.VRCFaceTracking](https://github.com/guygodin/VirtualDesktop.VRCFaceTracking). It keeps the eyelids moving when the headset loses gaze tracking and fixes eye gaze angles that came out short.

Upstream reads eye openness only while the eye-following blendshapes are valid. When a blink lost the pupils, the lids froze until gaze came back. In this fork openness comes from the face model whenever the face is tracked, and each gaze dropout writes a debug log line.

Upstream also sent gaze angles in radians, and VRCFaceTracking reads gaze as the tangent of the angle. The eyes fell short the further you looked: 30° to the side showed as about 27.6°, and 40° as about 35°.

## Install

1. Install the Virtual Desktop Streamer, version 1.30 or newer, and turn on face and eye tracking for your headset.
2. Download `VirtualDesktop.FaceTracking-<version>.zip` from the [Releases page](https://github.com/RealWhyKnot/VirtualDesktop.VRCFaceTracking/releases).
3. Close VRCFaceTracking, then unzip the archive into its own folder inside `%APPDATA%\VRCFaceTracking\CustomLibs`. The folder should hold `VirtualDesktop.FaceTracking.dll` and `module.json`.
4. Start VRCFaceTracking, turn on the Virtual Desktop module and connect to your PC with Virtual Desktop.

If you installed the stock Virtual Desktop module from the module registry, remove it first so the two don't fight over the headset.

## Compatibility

- Windows only, built for .NET 7.
- Needs Virtual Desktop Streamer 1.30 or newer.
- Built against the VRCFaceTracking core library bundled in `3rdParty`.

## Bug reports and contributing

Bugs go in [issues](https://github.com/RealWhyKnot/VirtualDesktop.VRCFaceTracking/issues). This fork is solo-maintained, so read [CONTRIBUTING.md](CONTRIBUTING.md) before sending a PR.

## License

MIT, like upstream.
