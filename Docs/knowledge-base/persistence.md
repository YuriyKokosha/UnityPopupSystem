# Persistence — local files as a cache of the server's truth

Applies to: `FileInventoryStorage`, `InventoryManager.ScheduleSave`, `InventorySyncService`. Feature detail
in [`../feature-maps/inventory.md`](../feature-maps/inventory.md).

## The server is the source of truth; the file is a cache

The client stores the inventory so the player sees the last known state instantly and offline, and so the
connect can send a hash instead of downloading the whole inventory every launch. Nothing about a locally
stored snapshot makes it *true*: when the server's hash disagrees, the server's snapshot replaces the local
one wholesale, with no merge. A merge would need to know which side is right about each stack, and only a
server that validated the mutations could know that — and this build has no such server (`Docs/architecture.md` §10).

The consequence for code: `Load` never writes (loading a cache must not re-persist it), `ReplaceFromServer`
does, and a missing or unreadable file is "no hash to offer", which forces the server to answer with a
snapshot. A corrupt file is therefore a logged event, not a blocked connect.

## Why a JSON file and not PlayerPrefs

`PlayerPrefs` is a string registry with platform-specific size limits (1 MB on some), no atomic write, and
no way to swap in a whole record. A file per player under `persistentDataPath` has none of those problems,
is trivially inspectable when a bug report arrives, and lets the root directory be injected — which is what
keeps the PlayMode storage tests out of a developer's real save.

## Atomic write

A crash between "truncate" and "flush" of a plain `File.WriteAllText` leaves an empty or half-written file,
which then reads as corrupt on the next launch. `FileInventoryStorage` writes to `<file>.tmp` and swaps it
in with `File.Replace` (or `File.Move` when there is no previous file), so the previous complete file
survives until the new one is complete. The file also carries `schemaVersion` and its own `hash`; a
mismatch on either throws rather than returning a snapshot the code would then trust.

## One save at a time, newest wins

Two mutations in the same frame must not race two writes: the slower one could finish last and leave the
file holding the older state. `InventoryManager` funnels saves through one loop — while a write is in
flight a new mutation only sets a dirty flag, and the loop writes the *current* snapshot once more when the
write ends. Intermediate states are skipped on purpose; only the latest has to reach disk. The save is
scheduled *before* `Changed` is raised, so an observer that throws cannot skip it
([`resilience.md`](resilience.md)). `InventoryManagerTests` pins the loop against a write held in flight
(`FakeInventoryStorage.HoldSaves`).

## What is not persisted

The wallet, the profile, cooldowns and the offer window are still in-memory only; the seam
(`IInventoryStorage`) is inventory-specific by design rather than a generic "save system", because the
inventory is the one piece of state with a server-side hash check designed to anchor it (the fake server
accepts any non-empty hash; see [`../feature-maps/inventory.md`](../feature-maps/inventory.md)). Generalising is a
deliberate later step, once a second consumer exists.
