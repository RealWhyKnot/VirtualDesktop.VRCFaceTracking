# Changelog

All notable user-visible changes to VirtualDesktop.VRCFaceTracking are tracked here.

## Unreleased

_No notable changes since the last release._

## v2026.05.12.1

- Removed the 1-euro filter pre-pass added in v2026.05.12.0. The headset's tracking signal is already smooth, and the extra low-pass step added perceptible latency without solving a real problem. Calibration persistence, soft-max curve, asymmetric floor learning, and antagonist arbitration from v2026.05.12.0 are unchanged.

## v2026.05.12.0

### Calibration

- Calibration now persists across sessions and across brief tracking interruptions. The learned per-expression range is saved to `%APPDATA%\VRCFaceTracking\VirtualDesktop.FaceTracking\calibration-v2.json` and reloaded on startup, so there is no 10-30 second re-learning window after a brief tracking drop.
- Moderate expressions no longer snap to full max while the calibrator is still figuring out the personal range. The output curve has a soft shoulder near the top; the avatar only fully reaches 1.0 when the raw signal clearly exceeds the learned ceiling.
- Resting neutral is steadier. The floor no longer slowly drifts toward zero while the channel is active; it only rises when the channel actually looks at rest.
- A 1-euro filter on the raw weights reduces neutral-state jitter without delaying intentional expressions.

### Mouth and face

- Antagonist expression pairs (smile and frown, cheek puff and cheek suck, pucker and stretch, tongue out and tongue retreat) are actively suppressed when both fire at once, so the avatar no longer shows physically impossible combinations.

### License

- The project is now distributed under the GNU General Public License v3. The original MIT notice from the upstream work is preserved in `LICENSE`.