# How this project is built with an AI agent

This file is about the *process*, not the system. The system is described in `Docs/architecture.md` and the two
`Docs/` trees; this is the record of how a human and an agent produce and check the code in this
repository, what that arrangement got wrong along the way, and what caught it. It exists because the
repository is meant to be shown as a reference for AI-assisted development, and a reference that
shows only the result hides the part worth learning from.

Everything below is grounded in artifacts that are in the tree — a document that records a failure, a
test that pins it, a tool that exists because of it — except the review reports, which are kept
outside the repository in the git-ignored `Claude outputs/`. Where the record is incomplete, the last
section says so.

## 1. Division of labour

The human (project owner) decides *what*: the brief, the scope cuts, which review findings to act on,
what the UI should look like, when something is done. The agent does *how*: it writes the code, the
tests, the editor tooling and the documentation, runs the suites and the renders, and reviews its own
and earlier work adversarially. Nothing lands without the human having asked for it, and the agent's
verdicts (a review, a "tests are green") are checked by running the same tools the agent ran.

Concretely, over the life of this project the agent has:

- written every layer of the engine and the six windows, the queue, the inventory and the fakes;
- written both test suites and re-run them after each change through the editor bridge;
- built the UI kit tooling (`Tools/ui-atlas/generate_sprites.py`, the `Tools/UI Kit` menu) and drawn
  the sprites in code from the design canvas the human chose;
- produced the documentation trees and kept them in step with the code — the rule in `Docs/architecture.md` §8
  is that reasoning lives in `Docs/`, not in comments, precisely so an agent can find it again;
- run structured reviews of the whole project and then implemented the agreed items.

The human has, in the same period, chosen the palette and the mockups, cut scope (`Docs/architecture.md` §10 is
the list), rejected or re-ordered review items, and pressed Play.

## 2. The contract the agent works under

An agent is only as good as what it can read at the start of a session, so the project front-loads
its constraints into things the agent cannot miss and, where possible, into things the compiler
checks instead of the reviewer.

- **[AGENTS.md](../AGENTS.md)** is the shared instruction source for every agent. Read it first.
  **[Docs/architecture.md](architecture.md)** holds the stack, layers, coding conventions, extension
  workflow and known gaps. **[CLAUDE.md](../CLAUDE.md)** is Claude's entry point and points to those shared documents.
  The README remains the human-facing summary. Put future changes in the document that owns them,
  following the "Where future changes belong" section of AGENTS.md.
- **Assembly definitions enforce the layering.** `Game` cannot reference `UI` because the compiler
  says so, not because a comment asks nicely. This was made a hard rule *after* the agent had quietly
  broken the soft version of it in seven files (`Docs/architecture.md` §3).
- **Conventions are chosen to be greppable.** "No ambient clock", "no `DiContainer` outside the
  composition root", "no `async void`", "everything `sealed`", "`IFactory<T>` not a locator" — each
  is a one-line search, and a review runs that search rather than trusting the prose.
- **Reasoning lives in `Docs/`, not in code.** `Docs/knowledge-base/` for what was measured about the
  engine and libraries, `Docs/feature-maps/` for how each module hangs together. A change that alters
  behaviour updates the relevant file in the same change; a comment longer than a line is a sign the
  note belongs there instead.
- **A repository skill** (`.claude/skills/ui-kit/SKILL.md`) turns the UI kit into a checklist the agent
  acts from: palette, sprite table, prefab rules, the three menu items, the failures that are silent.
  `ui-atlas.md` is the reasoning; the skill is the procedure.
- **Scratch is fenced off.** `Claude outputs/` is the agent's working folder (renders, test result
  files, downloads) and is git-ignored; nothing there is a deliverable.

## 3. The verification loop

The agent does not get to declare something done; it has to close the loop with a tool whose output
the human can read too.

1. **Tests, run from a menu, results in a file.** `Tools/Tests/Run EditMode|PlayMode tests (write
   results)` starts a suite through `TestRunnerApi`; `PlayModeResultProbe` writes
   `passed=… failed=…` plus one line per failing test to `Claude outputs/TestResults/<mode>.txt`. It
   exists because the agent drives the editor through a bridge (`com.unity.ai.assistant`, and Unity
   MCP tools in later sessions) and has no Test Runner window to look at. The file starting as
   `running` and staying there is itself a signal (a hung run).
2. **Renders, not reasoning, close layout claims.** `Tools/UI Kit/Render prefab` writes a 1080x1920
   PNG of a prefab from a throwaway additive scene. `ui-atlas.md` records why: "every alignment claim
   in this kit that was reasoned about turned out wrong at least once". The agent looks at the PNG
   before saying a screen is right.
3. **Play the scene.** After engine changes the agent presses Play through the bridge and reads the
   console; the expected state is `MainGame → ModalBackdrop → DailyReward` with zero console entries.
4. **Adversarial review with a refutation step.** Periodically the agent reviews the whole project
   as a reviewer trying to reject it: inventory first, then every claim in the docs turned into a
   hypothesis and tested against the code, then a pass for what is *absent*, then an explicit attempt
   to refute each finding before it is reported. Findings the agent dropped are listed with the
   reason, so nobody re-raises them. The output is a report file, kept outside the repository in
   `Claude outputs/`, and a proposed order of work; the human picks from it.
5. **Docs move with the code.** The same change that fixes a behaviour updates the feature map and,
   if it taught something about Unity or a library, the knowledge base.

## 4. What went wrong, and what caught it

This is the section a reference project is for. Each entry names the mistake, where it is written up,
and the mechanism that surfaced it. "Review" means an adversarial pass; "tests" means a suite run;
"measured" means the agent ran or rendered something rather than reasoning about it.

| Mistake | Caught by | Written up in |
|---|---|---|
| The `Game` layer imported `UI` in seven files while the docs said it never did | review, then made impossible by asmdefs | `Docs/architecture.md` §3 |
| The engine loaded prefabs with `Resources.Load`; everything under `Resources/` shipped and nothing could move to a remote catalog | review | `knowledge-base/addressables-and-content.md` |
| Startup ran as `async void`, so a boot failure was unobservable | review | `unitask-and-cancellation.md`, `feature-maps/app.md` |
| The view pool was unbounded and never emptied — a leak that grew with every window type | review | `knowledge-base/pooling-and-ownership.md` |
| `OfferWindowView` destroyed a texture the loader still cached | review | `pooling-and-ownership.md`, `feature-maps/windows.md` |
| The offer's active window was recomputed from "now" on every call, so `IsActiveAt` was always true and the queue's unavailability path was never exercised | review | `feature-maps/game-services.md` |
| The runner polled twice a second (500 ms idle, 250 ms interrupt) for events that could wake it | review, then measured after the rewrite | `feature-maps/window-queue.md` |
| A stale waiter cleared the live waiter's wake source, so a wake could sleep out the full heartbeat | tests (`StillWakesPromptly_AfterAnInterruptCycleHasOverlappedTwoWaiters`) | `window-queue.md` |
| `WindowDefinition.ViewType` was written by every module and read by nobody — a declared invariant nothing checked | review | `feature-maps/window-core.md` |
| A window's own close button closed the *popup* above it instead | review | `window-core.md` |
| `WindowHandle.StateChanged` had no subscriber anywhere; the lifecycle was a claim nothing checked | review, then pinned by a PlayMode test | `feature-maps/tests.md` |
| A nested canvas without its own `GraphicRaycaster` renders but ignores every click; `Canvas.overrideSorting` is ignored while the object is inactive | measured in Play | `knowledge-base/unity-ui-canvas-and-input.md` |
| Forcing `spriteMode = Single` renames sprite sub-assets and every `Image` pointing at the old name becomes a white quad, with no error | measured (render) | `feature-maps/ui-atlas.md`, the skill |
| The render harness built its camera in `MainScene` and left objects behind on any exception | measured (unsaved scene changes) | `ui-atlas.md` |
| The daily reward prefab showed a static "Chest x1" that was never the reward | review of a render | `feature-maps/windows.md` |
| `Assert.That(async () => …, Throws…)` deadlocks the editor: NUnit blocks the thread the UniTask player loop runs on. It bit twice | measured (a frozen editor) | `knowledge-base/testing-in-unity.md` |
| `FakeWindowsManager` flipped `IsQueueIdle` synchronously; the real manager flipped it only after the prefab load. The fake was *stricter* than production, so every EditMode test passed while the queue could open a window on top of a user-opened one | review, reading fake and production side by side; pinned by a PlayMode test the same day | `testing-in-unity.md` |
| `MainGameWindowController` set an "open requested" flag and never reset it on failure — one bad open and the button was dead for the session | review | `feature-maps/tests.md` |
| The Offer's Buy button was blank and clickable while the remote copy loaded | noted on the design canvas as a known defect, fixed after the review | `Docs/mockups/README.md`, `windows.md` |
| "Buy" cost nothing: `OfferData` had no price and the wallet never moved | review | `game-services.md` |
| `README.md` said five windows, no persistence and 44/26 tests while the code had six, a file-backed inventory and 92/36 | review, by counting | `README.md` |
| Two `InitTestScene*.unity` leftovers in `Assets/`, two editor scripts outside any asmdef | review (inventory sweep) | `Docs/architecture.md` §3 (asmdef table) |
| A throwing `InventoryManager.Changed` subscriber aborted a grant half-way: items added, save never scheduled, currencies and price never applied — and the caller saw a failure it could retry. Purchase relied on "grant and `Spend` both run synchronously", which a re-entrant subscriber breaks | review, reproduced by a standalone probe against the current sources; fixed with isolated observers and a local grant+price transaction, pinned by EditMode tests | `knowledge-base/resilience.md`, `game-services.md` |
| A throwing queue aggregator could send the idle monitor into a loop with no yield, and a window whose load kept failing was retried on every `QueueBecameIdle`, because the per-burst failure set was cleared each burst and the fake did not raise that wake | review, static reasoning; fixed with per-window isolation and a retry backoff on the injected clock, pinned by EditMode idle-monitor tests against a fake that now raises the wake | `window-queue.md`, `resilience.md` |
| A throwing `StateChanged` subscriber left `WaitForCloseAsync` pending forever; two concurrent base-screen opens created two instances; a throwing controller creation orphaned the acquired view | review — the close waiter reproduced by a standalone probe, the other two static; pinned by PlayMode tests | `window-core.md` |
| `int` overflow in `StackPacker` let an oversized bundle pass the slot check, and two wallet credits could wrap a balance negative | review, reproduced by a standalone probe; fixed with explicit limits (`MaxUnitsPerItem`, `MaxBalance`) and `long` accumulation | `inventory.md`, `game-services.md` |
| The docs promised more than the code: "no caller depends on `Fake*`" (two managers used `FakeRemoteConfigApi` constants), "the server compares hashes" (the fake accepts any non-empty hash), "rebind `IRpcManager` for a real backend" (no purchase/claim RPC exists), "strict lifecycle" (order was not enforced), and there was no boot test of `MainScene` | review, reading docs as hypotheses against the code | `game-services.md`, `architecture.md` §7/§10, `tests.md` |

Two patterns are worth pulling out of the table.

**Most of what the agent got wrong, the agent also found** — but only in a *separate* pass with a
deliberately hostile stance, never in the pass that wrote the code. Code that explains itself as you
write it is persuasive; the same code read the next day by a reviewer trying to reject it is not.
That is why the review is a distinct activity with its own procedure and its own output file.

**The most expensive class of mistake is the one where a check exists and quietly checks nothing**:
the `ViewType` nobody read, the `StateChanged` nobody subscribed to, the fake that was stricter than
production, the `IsActiveAt` that was always true. None of these fail. They are found by asking, for
each thing that looks like a safeguard, "what would happen if this were wrong?" — and by writing the
PlayMode test that would notice.

## 5. Rules that came out of it

These are the working rules that exist because of an entry in the table above. They are already in
force; this is where they are gathered.

- A fake that hard-codes an ordering or timing of production gets the PlayMode test for that same
  ordering *the same day*, not "later".
- A claim about layout is closed by a render, a claim about input by a Play session, a claim about
  behaviour by a test. Reasoning alone closes nothing.
- The document that explains a behaviour changes in the same commit as the behaviour. If the change
  taught something about Unity or a library, it goes in the knowledge base with the date.
- A review reports what it dropped and why, so the next review does not re-raise it and the reader
  can see the list was filtered.
- Numbers that change on every commit (test counts) do not go in prose; they live in the file the
  tools write.
- Anything an agent must not be able to do by accident is made a compile error (asmdefs,
  `InternalsVisibleTo` granted by name) rather than a rule in a document.

## 6. Reproducing the loop for the next change

1. Read `AGENTS.md` and `Docs/architecture.md`, then the feature map for the module you are touching, then the knowledge-base
   file it links. For a window, also the `ui-kit` skill.
2. Make the change with its test: EditMode for the queue and the inventory logic, PlayMode for
   anything that touches a prefab, a canvas or a controller flow (`Docs/architecture.md` §9 says which).
3. Run both suites through `Tools/Tests/…` and read `Claude outputs/TestResults/*.txt`. For a
   prefab, render it and look. For engine work, press Play and read the console.
4. Update the feature map (and the knowledge base if something was learned). Do not leave a comment
   longer than a line in the code.
5. Before a milestone, run the adversarial review as a separate pass and act on its order of work.

## 7. What this record does not yet show

Honesty about the gaps is part of the point.

- **The early version history is coarse.** The first commits are few and large, and none carries an
  agent trailer, so the log cannot show which of those changes the agent made, which the human
  corrected, and in what order. The convention from here on is a trailer on every agent-authored
  commit naming the agent and linking the session, so the split is traceable from the log rather
  than from memory.
- **Which model or tool did what is not recorded per change.** The editor bridge changed over time
  (`com.unity.ai.assistant` first, Unity MCP tools later); the design canvas is a separate Claude
  artifact; reviews were run through a review skill. None of that is stamped on the files. The
  commit trailers above are the intended fix; none of the existing commits has one yet.
- **CI has never run.** The workflow in `Docs/ci/` is written and reviewed but has not executed; until
  it does, "both suites green" is a statement about one machine (see
  `Claude outputs/TestResults/`).
- **The human's decisions are recorded only where they left a mark** — a scope cut in `Docs/architecture.md` §10,
  a rejected palette in the mockups README, an ordering change in a review report's status block
  (kept outside the repository, in `Claude outputs/`). There is no decision log as such, and this
  file is the closest thing to one.
