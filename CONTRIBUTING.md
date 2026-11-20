# Contributing to CourtSim

## Ground rules

1. **Logic goes in `CourtSim.Core`.** If it can be written without `UnityEngine`, it must be, and it
   needs unit tests. MonoBehaviours stay thin.
2. **Follow `docs/STYLE_GUIDE.md`.** `.editorconfig` enforces formatting and naming; configure your IDE
   to apply it on save.
3. **Third-party code needs a license check first.** Copy only from permissively licensed sources
   (MIT, BSD, Apache-2.0), add an entry to `NOTICES.md` in the same commit, and mark the adapted code
   with a comment. Repositories without a license file, and copyleft or ShareAlike ones
   (e.g. CC BY-SA), are reference-only.
4. **Every new dependency needs a justification** in the PR description. Prefer built-in Unity features.
5. **Never commit secrets.** API keys come from the environment or an untracked file.

## Git workflow

- `main` is always releasable. Work on short-lived branches cut from `main`:
  `feature/<topic>`, `fix/<topic>`, `docs/<topic>`, `chore/<topic>`.
- Keep commits focused, with messages in [Conventional Commits](https://www.conventionalcommits.org/) style:
  `feat(ui): add evidence viewer`, `fix(player): stop sliding after closing a menu`.
- Before opening a PR, rebase on `main` and squash fix-up commits (`git rebase -i main` locally) so each
  commit builds and tells one part of the story.
- Merge with **squash** or **rebase**, never a merge commit from a stale branch. Delete the branch after merge.
- Update `CHANGELOG.md` under *Unreleased* in the same PR.
- Unity: commit every `.meta` file alongside its asset. Configure Smart Merge for scenes (see
  `.gitattributes`). When the project starts importing binary art, move it to Git LFS.

## Building and testing

### Without Unity (core only)

```powershell
pwsh tools/verify-core.ps1                    # format check, build (warnings = errors), tests, coverage ≥ 80%
pwsh tools/verify-core.ps1 -Dotnet <path>     # use a specific dotnet executable
```

CI (`.github/workflows/core.yml`) runs the same script on every push and PR.

### In Unity

1. **Window → General → Test Runner.** Run **EditMode** (core tests) and **PlayMode** (builder and UI flow).
2. Coverage: **Window → Analysis → Code Coverage**, enable *Generate HTML report*, and re-run the tests.
   Core must stay at or above 80% line coverage. Runtime code with logic worth testing should move to Core.
3. Smoke test: generate the scene, press Play, enter the courtroom, open and close each screen, then
   make all three rulings.
4. Headless scene generation (CI or batch):
   `Unity -batchmode -quit -projectPath . -executeMethod CourtSim.EditorTools.CourtroomSceneGenerator.GenerateFromCommandLine`

### Builds

**File → Build Settings**, then select *CourtRoom* (added automatically by the generator) and build for
the desktop target. VR targets arrive with the XR phase.

## Pull request checklist

- [ ] `tools/verify-core.ps1` passes
- [ ] EditMode and PlayMode tests pass in Unity
- [ ] New logic in Core has tests; coverage ≥ 80%
- [ ] No new warnings in the Unity console
- [ ] `CHANGELOG.md` updated; `NOTICES.md` updated if third-party material was added
- [ ] UI changes use `UIFactory`/`UITheme` only, with screenshots attached
