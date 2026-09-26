# AGENTS.md — shared project instructions

These instructions apply to every coding agent working in this repository. Keep shared agent rules
here; `CLAUDE.md` is only the Claude entry point. User task instructions take precedence.

## Before working

- Read [the architecture guide](Docs/architecture.md) for the stack, assembly boundaries,
  coding conventions, extension workflow and prototype limitations. Read the relevant
  `Docs/feature-maps/` document and its linked knowledge-base notes before changing a subsystem.
- Read [the process](Docs/process.md) before a review or work spanning multiple modules.
- For UI changes, read [the UI kit](.claude/skills/ui-kit/SKILL.md), the
  [UI feature map](Docs/feature-maps/ui-atlas.md) and relevant `Docs/mockups/` references.
- Inspect the working tree first. Preserve existing changes, including staged work by another
  agent; do not reset, overwrite or commit unrelated changes.
- This is a prototype / take-home assessment. Respect the documented scope; distinguish an
  intentional limitation from a defect in the supported behaviour.

## Coding conventions

Follow the coding conventions in
[Docs/architecture.md §8](Docs/architecture.md#8-coding-conventions). They are project rules that
apply to humans and agents alike, so the technical guide owns them; change them there, not here.

## Unity assets and validation

- Preserve asset GUIDs and move or rename an asset together with its `.meta` file. New assets
  need their Unity-generated metadata. Do not hand-edit generated `Library/`, `Temp/` or `obj/` files.
- Keep the assembly dependency direction described in the architecture guide. Game must not
  reference UI; window payload implementations live beside their windows in UI.
- Extend EditMode tests for queue and game-service behaviour, PlayMode tests for the window engine,
  and `WindowFlowPlayModeTests` for controller flows. See
  [test coverage](Docs/feature-maps/tests.md) and [test mechanics](Docs/knowledge-base/testing-in-unity.md).
- After behaviour changes, run both EditMode and PlayMode suites — how to run them and where the
  results land is in [test coverage](Docs/feature-maps/tests.md#extending-this). Report what
  actually ran; old result files are not evidence for current changes. If execution is
  unavailable, say what remains unverified.
- Follow the test-body rules in [test mechanics](Docs/knowledge-base/testing-in-unity.md)
  (plain `async Task`, NUnit constraints, no async delegate inside a blocking constraint).
- After prefab/visual changes, render and inspect the result using the UI kit workflow. After
  engine changes, run the demo scene and inspect the console when editor access is available.
- Keep temporary reports, renders and exports in the git-ignored `Claude outputs/` folder.
  Keep test counts in generated result files rather than documentation prose.

## Where future changes belong

- Shared agent instructions: this file.
- Agent-specific integration instructions: that agent's entry point (`CLAUDE.md` for Claude).
- Architecture, stack, coding conventions, extension workflow and scope: [Docs/architecture.md](Docs/architecture.md).
- Module behaviour: `Docs/feature-maps/`; cross-cutting technical reasoning: `Docs/knowledge-base/`.
- Development process and verification history: [Docs/process.md](Docs/process.md).
- Human-facing description, setup and running instructions: [README.md](README.md).
- Update the relevant documentation with behaviour changes. Link to the canonical rule instead
  of copying it into several files. A review should identify discarded findings and why they
  were discarded, and distinguish static reasoning from scenarios reproduced by tests.
