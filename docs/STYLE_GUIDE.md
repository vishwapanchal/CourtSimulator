# CourtSim C# style guide

The rules below are enforced by `.editorconfig`. Rider, ReSharper and Visual Studio read it
automatically, and `tools/verify-core.ps1` fails the build on violations in `CourtSim.Core`.
This page explains the *why* and covers what a formatter can't check.

## Formatting (enforced)

- 4 spaces, LF line endings, UTF-8, final newline, 140-column soft limit.
- Allman braces. **Always** use braces, even for one-line `if` bodies.
- `using` directives outside the namespace, `System.*` first, unused ones removed.
- Block-scoped namespaces (`namespace X { ... }`), which Unity 2022.3's compiler fully supports.

**Format on save:** in Rider, enable *Settings → Tools → Actions on Save → Reformat and Cleanup
Code*. In Visual Studio, enable *Code Cleanup on Save* with the "Format document" fixer. In VS Code,
set `"editor.formatOnSave": true` with the C# Dev Kit.

## Naming (enforced)

| Symbol | Style | Example |
| --- | --- | --- |
| Types, methods, properties, events, constants | `PascalCase` | `ScreenStack`, `MaxPitchDegrees` |
| Interfaces | `IPascalCase` | `IPlayerInputSource` |
| Private/protected fields, including `[SerializeField]` | `_camelCase` | `_walkSpeed` |
| Parameters, locals | `camelCase` | `deltaTime` |
| Test methods | `Method_Condition_Expectation` | `Pop_OnEmpty_ReturnsFalse` |

Unity's Inspector strips the leading underscore, so `_walkSpeed` displays as "Walk Speed".

## Structure

- **Assemblies:** `CourtSim.Core` holds pure C# with *no* `UnityEngine` reference (`noEngineReferences`).
  Put every rule, calculation and state machine there so it can be unit-tested outside Unity.
  `CourtSim.Runtime` holds MonoBehaviours and Unity glue. `CourtSim.Editor` holds editor tooling.
- **One MonoBehaviour or ScriptableObject per file, file named after the class.** Unity cannot
  serialise a component into a scene otherwise. Plain C# helpers may share a file when they are small
  and closely related.
- **Serialised state:** `[SerializeField] private` fields plus read-only properties. Never public fields.
- **Composition over singletons.** `CourtSimBootstrap` wires dependencies. `GameServices.Events` is the
  only global, and it is reset on play-mode entry.
- **Events are `readonly struct`s** published on the `EventBus`. Subscribe in `OnEnable` and dispose
  the token in `OnDisable`.

## Unity-specific rules

- **Don't use `?.` or `??` on `UnityEngine.Object`s.** They bypass Unity's overloaded null check and
  treat destroyed objects as alive. Use explicit `!= null`. They are fine on plain C# objects.
- No `Find*`, `GetComponent` or allocations (LINQ, string concat, closures) in `Update`. Cache in
  `Awake`. `InteractionProbe` shows the pattern.
- Time-based UI uses `Time.unscaledDeltaTime`, so menus keep animating if gameplay is paused.
- Create runtime `Material`/`Texture` instances with `HideFlags.DontSave` and destroy them in `OnDestroy`
  (`UnityObjectUtil.SafeDestroy`).

## Comments and docs

- XML `<summary>` on every public type and on any member whose name doesn't say everything.
- Comments explain *why*, not *what*. A comment that restates the code gets deleted.
- Link the decision record or issue when code works around a Unity bug.

## UI

- Build UI through `UIFactory` only. Colours, sizes and radii come from `UITheme`; never hard-code a
  colour in a screen.
- Each modal is a `ModalScreen` subclass that fills `BuildModal`. Each screen declares a
  `DefaultSelection` so keyboard and gamepad users land on a control.
- Overline text is written in capitals in the source string. Body copy is sentence case.
- Copy is plain and second-person ("You have ruled on…"). The AI assistant speaks in first person and
  always carries the standing caution.
