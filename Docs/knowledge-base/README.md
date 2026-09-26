# Knowledge base

Cross-cutting knowledge that is too long for a code comment. These are the *engine- and
library-level* facts and decisions — things that are true regardless of which class you are
looking at, and things that were measured rather than assumed. Per-module documentation lives in
[`../feature-maps/`](../feature-maps/); layering and coding conventions are described in
[`../architecture.md`](../architecture.md).

The code itself carries almost no comments by design (see [`../architecture.md`](../architecture.md) §8). What is left in the
code is a short XML-doc on the ports and contracts, plus a one-line warning at the handful of call
sites where the surrounding lines look removable and are not. Everything else — the reasoning and the
measurements — is here; the history of what went wrong lives in [`../process.md`](../process.md).

| File | What it covers |
|---|---|
| [`unity-ui-canvas-and-input.md`](unity-ui-canvas-and-input.md) | Split canvases, `sortingOrder` vs hierarchy order, raycasters, `overrideSorting`, layer bands |
| [`unitask-and-cancellation.md`](unitask-and-cancellation.md) | `Share()` vs `Preserve()`, which token belongs where, transactions, `async void`/`.Forget()` |
| [`addressables-and-content.md`](addressables-and-content.md) | Handle lifetime, dedup, cached failures, the one blocking load, no code-built fallback |
| [`zenject-composition.md`](zenject-composition.md) | Concrete-first binding, typed factories vs `DiContainer`, multi-binding, disposal, assemblies |
| [`time-and-cooldowns.md`](time-and-cooldowns.md) | Server-anchored time, monotonic clocks, suspension, what cooldown semantics actually mean |
| [`pooling-and-ownership.md`](pooling-and-ownership.md) | View pooling traps, who owns a downloaded texture, close-ordering constraints |
| [`resilience.md`](resilience.md) | Degrade-don't-crash rules: which failures are contained where, and which are hard |
| [`testing-in-unity.md`](testing-in-unity.md) | Harness mechanics: async test bodies, NUnit deadlocks, fake/production drift, the result-file runner |
| [`persistence.md`](persistence.md) | The local inventory file as a cache of the server's truth: why JSON over PlayerPrefs, atomic writes, one save at a time, what is not persisted |
