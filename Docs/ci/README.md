# CI

`tests.yml` is the GitHub Actions workflow that runs both test suites (EditMode and PlayMode) via
[GameCI](https://game-ci.github.io/) on every push and pull request.

**It is not active where it sits.** GitHub only runs workflows found in `.github/workflows/`, so
this file has to be moved there once:

```bash
mkdir -p .github/workflows
mv Docs/ci/tests.yml .github/workflows/tests.yml
```

It was left here rather than placed directly because the tooling used to write it refuses to write
into `.github/workflows/` — a sensible guard, since anything landing there executes on push.

## Before the first green run

The workflow needs a Unity licence in the repository's secrets
(**Settings → Secrets and variables → Actions**):

| Secret | What it is |
|---|---|
| `UNITY_LICENSE` | Contents of the `.ulf` licence file, for a Personal licence. See [GameCI's activation guide](https://game-ci.github.io/docs/github/activation). |
| `UNITY_EMAIL` / `UNITY_PASSWORD` | The Unity account, used for Pro/Plus serial activation. |

Without them the job fails at the activation step, before Unity ever opens the project — the run
log says so explicitly, so a first-time failure here is not a mystery.

## What it does

- Runs `editmode` and `playmode` as a matrix, with `fail-fast: false`, so a break in one suite does
  not hide the state of the other.
- Caches `Library/`, keyed on the contents of `Assets/`, `Packages/` and `ProjectSettings/`. A cold
  run reimports every asset in the project, which is the slow part of Unity CI.
- Pins `unityVersion: 6000.3.12f1`, the version this project is on.
- Uploads the NUnit result XML as an artifact on both success and failure.
