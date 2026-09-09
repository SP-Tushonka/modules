# Modules

BepInEx 6 plugins that make Escape From Tarkov 1.1 run against the SPT server.

**Project**              | **Function**
------------------------ | --------------------------------------------
SPTushonka.Build         | Aggregates the plugins into `Build/BepInEx`
SPTushonka.Common        | HTTP client and utilities shared across projects
SPTushonka.Core          | Required patches to start the game
SPTushonka.Custom        | SPTushonka enhancements to EFT
SPTushonka.Debugging     | Console commands and test tooling, not needed to play
SPTushonka.PrePatch      | Preloader patcher: export gate restore and startup checks
SPTushonka.Reflection    | Patching utilities used across the project
SPTushonka.SinglePlayer  | Simulating the online game while offline

## Building

The projects reference BepInEx and the interop assemblies straight out of the game install named
by `GameDir` in `Directory.Build.props`. Build `Modules.slnx` in Release, then copy
`Build/BepInEx` over the install. Plugins live in `plugins/sptushonka/`, not loose in `plugins/`.

## Privacy
SPT is an open source project. Your commit credentials as author of a commit will be visible by anyone. Please make sure you understand this before submitting a PR.
Feel free to use a "fake" username and email on your commits by using the following commands:
```bash
git config user.name "USERNAME"
git config user.email "USERNAME@SOMETHING.com"
```
