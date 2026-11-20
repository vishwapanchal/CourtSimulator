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
5. **Never commit secrets.** API keys come from `ANTHROPIC_API_KEY` or a file in the user's persistent
   data folder (`ApiKeyLocator`), never from anything under `Assets/`.

## Adding a case

Read `docs/CASE_FORMAT.md` first.

- [ ] Copy a shipped case to `Assets/Resources/Cases/<id>.json`. Use a unique kebab-case `id` that matches
      the file name.
- [ ] Keep it fictional, and include the standard `jurisdictionNote`.
- [ ] Give each decision point one `sound` option, `feedback` on every option, factors on both sides, one
      sound advice entry and at least one flawed variant (two of different kinds where you can).
- [ ] Make every flaw detectable from the record, and point to it (`(F4)`) in `flawExplanation`.
- [ ] Check field spelling (`defence`, `flawExplanation`): the reader ignores unknown fields.
- [ ] Add the id to `TestCases.ShippedCaseIds` (`Assets/Tests/EditMode/TestCases.cs`) and update the
      shipped-case count in `CaseFileReaderTests`.
- [ ] The tests pass, and Play shows no `is invalid and was skipped` error in the Console.
- [ ] Play the case through with **Flawed Advice Count** 3 and a few seeds, and read each debrief.
- [ ] Commit the `.json` and its `.meta`, and note the case in `CHANGELOG.md`.

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

### In Unity, headless (preferred)

```powershell
pwsh tools/unity.ps1 -Coverage                 # compile, regenerate scene, EditMode + PlayMode, coverage ≥ 80%
pwsh tools/unity.ps1 -Task compile             # just import + compile; fails on any error CS
pwsh tools/unity.ps1 -Task scene               # regenerate Assets/Scenes/CourtRoom.unity
pwsh tools/unity.ps1 -Task playmode            # one suite (editmode | playmode | test)
```

The script finds the Editor version from `ProjectSettings/ProjectVersion.txt` (override with `-UnityExe`
or `UNITY_EXE`). It writes logs, NUnit XML and the HTML coverage report to `Logs/batch/`. Batch mode
refuses to open a project that an interactive Editor already has open, including one left open for
MCP for Unity.

### In Unity, interactively

1. **Window → General → Test Runner.** Run **EditMode** (core and editor tests) and **PlayMode**.
2. Smoke test: press Play in `CourtRoom`, enter the courtroom, open and close each screen, then make all
   three rulings.
3. Runtime code with logic worth testing should move to Core.

### Builds

**File → Build Settings**, then select *CourtRoom* (added automatically by the generator) and build for
the desktop target. VR targets arrive with the XR phase.

## Pull request checklist

- [ ] `tools/verify-core.ps1` passes
- [ ] `tools/unity.ps1 -Coverage` passes (compile, scene, EditMode, PlayMode, coverage)
- [ ] Regenerated `CourtRoom.unity` committed if the layout or builder changed
- [ ] New logic in Core has tests; coverage ≥ 80%
- [ ] No new warnings in the Unity console
- [ ] `CHANGELOG.md` updated; `NOTICES.md` updated if third-party material was added
- [ ] UI changes use `UIFactory`/`UITheme` only, with screenshots attached
- [ ] New or changed cases follow `docs/CASE_FORMAT.md` and load without errors
- [ ] No API key or other secret in the diff
