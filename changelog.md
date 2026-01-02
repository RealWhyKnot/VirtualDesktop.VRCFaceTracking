# Changelog

## [v2026.01.01.1] - 2026-01-01
### Added
- Added a 1.5x multiplier to expression weights (smiling, frowning, etc.) to make them easier to trigger.
### Changed
- Updated `build.ps1` to deploy files into a `VirtualDesktop` subfolder within `CustomLibs` for better organization.

## [v2026.01.01.0] - 2026-01-01
### Added
- Wink detection: Eyes are now forced to be perfectly in sync unless the difference in openness is large enough to be an intentional wink.
- Gaze synchronization: Both eyes now look at the same averaged point to prevent cross-eyed or wandering eye issues caused by tracking jitter.

## [v2025.12.30.0] - 2025-12-30
### Changed
- Increased eye closure sensitivity by 1.5x.

## [v2025.12.29.7] - 2025-12-29
### Added
- Tracking refresh rate (Hz) and signal interval monitoring.
### Changed
- Periodic debug log now includes the average signal interval and frequency (Hz) of incoming tracking data.

## [v2025.12.29.6] - 2025-12-29
### Added
- Comprehensive logging for initialization, including thresholds and MemoryMappedFile details.
- Expanded periodic debug logs to include eye openness, mouth closure, and jaw state.
- Explicit teardown logging.
### Changed
- Improved error reporting in `Initialize` to include exception messages.

## [v2025.12.29.5] - 2025-12-29
### Added
- `build.ps1` script for automatic building and deployment to VRCFT directory.
- Detailed periodic debug logging including tongue and eyebrow states.

## [v2025.12.29.4] - 2025-12-29
### Added
- Link between tongue vertical movement and eyebrows.
- Automatic jaw opening when the tongue is extended.
### Changed
- `JawOpen` is now forced to be at least as high as `TongueOut` to prevent lip clipping.
- Eyebrows will now raise slightly when the tongue curls up.
- Tongue will now curl up slightly when eyebrows are raised.

## [v2025.12.29.3] - 2025-12-29
### Added
- Deadzones for eye openness and mouth closure.
### Changed
- Eyes will now stay 100% open if openness is above 95%.
- Mouth will now stay 100% closed if closure weight is above 95%.

## [v2025.12.29.2] - 2025-12-29
### Added
- Benchmarking with `Stopwatch` to track update performance.
- Periodic logging of average update latency (every 1000 frames).
### Changed
- Optimized hot path math by using `Math.Clamp` and pre-calculated constants.
- Added `AggressiveInlining` hints to tracking update methods for reduced call overhead.
- Enhanced initialization logging with capability status.
- Refactored expression mapping to reduce redundant array lookups.

## [v2025.12.29.1] - 2025-12-29
### Added
- GitHub workflow for automated Windows builds and releases on tag push.
### Changed
- Fixed `VRCFaceTracking.Core.dll` reference path in project file.

## [v2025.12.29.0] - 2025-12-29
### Changed
- Updated README.md with detailed program description, features, requirements, and setup instructions.