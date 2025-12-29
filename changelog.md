# Changelog

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
