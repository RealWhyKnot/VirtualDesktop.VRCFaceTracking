# VirtualDesktop.VRCFaceTracking
VRCFaceTracking module for Virtual Desktop

This is my fork of [guygodin/VirtualDesktop.VRCFaceTracking](https://github.com/guygodin/VirtualDesktop.VRCFaceTracking). It keeps the eyelids moving when the headset loses gaze tracking, fixes eye gaze angles that came out short, and eases the face back to neutral when tracking stops.

Upstream reads eye openness only while the eye-following blendshapes are valid. When a blink lost the pupils, the lids froze until gaze came back. In this fork openness comes from the face model whenever the face is tracked, and each gaze dropout writes a debug log line.

Upstream also sent gaze angles in radians, and VRCFaceTracking reads gaze as the tangent of the angle. The eyes fell short the further you looked: 30° to the side showed as about 27.6°, and 40° as about 35°.

When Virtual Desktop stops tracking the eyes or the face, upstream leaves the last values in place and the avatar stays stuck mid-expression. This fork holds them for 250 ms, then eases that part of the face to neutral over the next 750 ms.

Builds are on the Releases page. MIT licensed, like upstream.
