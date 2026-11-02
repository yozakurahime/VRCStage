# Changelog

## 0.10.1 — smoother game locomotion

- Added acceleration/braking, smoother direction reversals, speed/proportion-aware animation cadence and an adjustable step rate.
- Separated jump ascent from falling, added an official Unity running-jump pose, and shortened landing recovery while moving.
- Added a short jump buffer and ledge grace period; brief ground contact gaps no longer immediately trigger falling animation.
- Matched the authored crouch-walk cycle/stride to its movement speed and added restrained opposing arm motion. Crouch remains a locally authored animation.
- Corrected movement facing for avatars with a rotated prefab root; the C crouch key no longer also requests tracking calibration during game control.
- Expanded locomotion validation for start/stop, custom speeds, reversal, moving jump/landing, imported yaw, ledge jumps and buffered landing jumps.

## 0.10.0 — VRCStage public alpha

- Adopted the VRCStage name and a VTuber-first introduction: VRChat avatar Unity project → local package → studio → OBS, without VRM conversion.
- Added **Começar**, a first-stream hub linking avatar import, face/voice, optional hands/VR, camera framing and output settings.
- Added VRCStage export menu aliases while retaining legacy menus, serialized formats, settings paths and OBS/Spout identifiers.
- Updated clean-output launch to use the current executable; the public Windows distribution uses `VRCStage.exe` and includes a renamed exporter package.
- Prepared public documentation in English and Brazilian Portuguese, contribution/issue templates, roadmap, original-code MIT license and third-party notices.
- Added tracked-source publication checks, lightweight CI, source archives from reviewed Git commits and runtime packaging from an explicit allowlist.

## Previous AvatarLab Offline releases

The local development series included avatar export, expressions/controllers, custom physics/colliders, VR/body/face/hand inputs, pose/motion libraries, world/object import, studio lighting, cameras, photos, post effects and video output. The 0.9.6 update added third-person game camera controls and tabletop Leap height following for seated/crouched poses. These implementations form the public alpha; earlier private validation content is not redistributed.
