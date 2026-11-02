# Contributing

VRCStage welcomes bug reports, documentation, translation, compatibility fixtures and code contributions. Start with a small reproducible issue or discuss a larger change before implementing it.

Use Unity **2022.3.22f1**. See [build instructions](docs/BUILD.md). Keep avatar export code isolated from runtime code and preserve existing serialized formats and profiles. For tracking/animation changes, state which source owns each bone and how ownership is released. For rendering changes, test actual GPU output, alpha and concurrent streaming where relevant.

Run checks appropriate to your change and include results in the PR. Never claim a hardware test from synthetic input. Hardware-free CI covers publication checks and bridge unit tests; it does not validate physical tracking, all avatar rigs or a full Unity build.

Do not commit Unity caches, generated assets, binaries from private projects, access tokens, local profiles, user paths, commercial worlds, GoGo packs, AI weights or registered body models. Use the publication guard before pushing. Remove personal details from diagnostics manually too.

Original contributions are submitted under the repository's MIT license. Preserve third-party notices and identify any reused code/assets with their source and license. Unity Companion assets retain their license. No contributor license agreement is required.

Please be respectful, focus feedback on behavior and code, and avoid posting other people's personal information. Maintainers may remove abusive or private content.
