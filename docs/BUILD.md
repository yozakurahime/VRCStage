# Build and validate

## Unity player and exporter

Use **Unity 2022.3.22f1**, Windows x64 build support, and PowerShell. Clone the repository and open its root in Unity. Packages resolve from `Packages/manifest.json`; Spout is vendored. Run:

```powershell
./Tools/Build.ps1
# Or pass the installed editor explicitly:
./Tools/Build.ps1 -UnityPath 'C:\Path\To\Unity.exe'
```

The build entry point `AvatarLab.Editor.LabBuild.Build` calls `Prepare`: it generates the scene, motion library and original demo assets in `Assets/AvatarLabGenerated`, compiles the Windows player, and exports the Unity editor package. Output is under `Build/AvatarLabOffline`; legacy identifiers intentionally preserve local data and existing integrations.

The player does not require the VRChat SDK to compile. The **avatar export project** needs the SDK/shaders/build tools that its avatar uses. Runtime VRChat support is an independent implementation; no VRChat client assemblies are bundled here.

## Optional webcam and RGB conversion bridges

Install Python 3.10+ on Windows, then:

```powershell
./Tools/BuildWebcam.ps1
./Tools/BuildRGBMotion.ps1
```

The webcam build installs pinned MediaPipe/OpenCV/PyInstaller dependencies into `Tools/HandTracking/.venv`. Hand/pose task models are included under `Tools/HandTracking/models`; provenance and SHA256 are in `MODEL-SOURCE.txt`. Bridges are separate processes and carry dependency license notices. The RGB converter needs NumPy; video reconstruction itself is performed by an optional external [toyxyz_RGBMocap](https://github.com/toyxyz/toyxyz_RGBMocap) installation. Registered body models, AI weights and videos are not distributed by VRCStage.

## Package

```powershell
./Tools/Package.ps1
./Tools/PackageSource.ps1
```

The runtime packager copies an explicit allowlist, renames the public player to `VRCStage.exe`, includes the exporter and generated examples, and carries third-party notices. It excludes logs, profiles, imported avatars/worlds, private pose packs and test results. Optional bridges are included only if built. The source packager uses `git archive HEAD`; commit reviewed source changes before running it.

## Checks

```powershell
python Tools/check_public.py
python -m unittest discover -s Tools/HandTracking -p test_bridge.py
```

`check_public.py` checks the Git index/HEAD, including private-package extensions, credential patterns, local Windows user paths and files too large for normal GitHub commits. It is a publication guard, not a complete security audit. GitHub Actions runs these lightweight checks without Unity credentials.

Player validation uses command-line flags with an avatar you own. For example:

```powershell
& '.\Build\AvatarLabOffline\AvatarLab Offline.exe' --avatar 'C:\MyTests\Avatar.alavatar' --game-camera-test -batchmode -nographics -logFile 'C:\MyTests\camera.log'
```

Available harnesses include `--smoke-test`, `--hand-test`, `--desk-test`, `--game-camera-test`, `--locomotion-test`, `--ifacial-test`, `--photo-test` and `--hair-video-test`; inspect each test's source for prerequisites and result locations. Some require a Humanoid avatar, a locally owned GoGo pack or a real GPU; headless graphics tests are not substitutes for GPU rendering tests. Test harnesses may change live application state: run them separately from a live stream. The public repo does not include private regression avatars or user validation outputs.

## Internal compatibility

Namespace `AvatarLab`, package magic values, file extensions, Unity company/product names, AppData location, OBS collection names and Spout sender remain stable. Public branding/window title is VRCStage. Output viewer launch resolves the current executable, so both the legacy installation and renamed public package work. Do not rename these internal interfaces without a migration and regression checks.
