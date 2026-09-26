using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using UnityEngine;

namespace PopupSystem.Game.Services.Inventory
{
    /// <summary>Owns the inventory state and the rules over it. Every mutation is all-or-nothing, is handed to
    /// <see cref="IInventoryStorage"/> and only then raises <see cref="Changed"/> exactly once. Subscribers are
    /// isolated: one that throws is logged and cannot undo, interrupt or fail the mutation that notified it.
    /// Constructible without Unity or a container: the EditMode suite drives it against a fake storage.</summary>
    public sealed class InventoryManager
    {
        private readonly IInventoryStorage _storage;

        private InventoryConfig _config;
        private InventorySnapshot _snapshot = InventorySnapshot.Empty;
        private string _playerId;

        private readonly StateChangeNotifier _changed;

        private bool _isSaving;
        private bool _isSaveDirty;

        public InventoryManager(IInventoryStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _changed = new StateChangeNotifier(() => Changed);
        }

        public event Action Changed;

        public bool IsInitialized => _config != null;

        public InventoryConfig Config => _config;

        public ItemCatalog Catalog => _config?.Catalog ?? ItemCatalog.Empty;

        public InventorySnapshot Snapshot => _snapshot;

        public IReadOnlyList<ItemStack> Stacks => _snapshot.Stacks;

        public int UsedSlots => _snapshot.UsedSlots;

        public int SlotLimit => _config?.SlotLimit ?? InventoryConfig.NoSlotLimit;

        /// <summary>Null when there is no limit. Never negative: a limit lowered below the used count reads as 0.</summary>
        public int? FreeSlots => _config == null || !_config.HasSlotLimit
            ? null
            : Math.Max(0, _config.SlotLimit - _snapshot.UsedSlots);

        public void Initialize(InventoryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>Replaces the state without persisting it — this is what the connect flow calls with whatever
        /// the storage had, so loading never writes the file back.</summary>
        public void Load(string playerId, InventorySnapshot snapshot)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("A player id is required.", nameof(playerId));
            }

            _playerId = playerId;
            _snapshot = snapshot ?? InventorySnapshot.Empty;
            _changed.Raise();
        }

        /// <summary>The server disagreed with the local hash: its snapshot wins and is written down.</summary>
        public void ReplaceFromServer(InventorySnapshot snapshot)
        {
            EnsureLoaded();
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            ScheduleSave();
            _changed.Raise();
        }

        public int GetCount(string itemId)
        {
            return StackPacker.CountOf(_snapshot.Stacks, itemId);
        }

        public InventoryOperationResult CanAdd(IReadOnlyList<ItemAmount> items)
        {
            EnsureInitialized();

            if (items == null || items.Count == 0)
            {
                return InventoryOperationResult.Ok;
            }

            var totals = StackPacker.Aggregate(items);

            foreach (var pair in totals)
            {
                if (!_config.Catalog.Contains(pair.Key))
                {
                    return InventoryOperationResult.UnknownItem(pair.Key);
                }

                // Long arithmetic: both halves are ints, and their sum is exactly what must not wrap.
                if ((long)GetCount(pair.Key) + pair.Value > InventoryConfig.MaxUnitsPerItem)
                {
                    return InventoryOperationResult.QuantityLimitExceeded(pair.Key);
                }
            }

            var required = StackPacker.CountNewStacksRequired(_snapshot.Stacks, _config.Catalog, totals);
            var free = FreeSlots;

            if (free.HasValue && required > free.Value)
            {
                return InventoryOperationResult.NotEnoughSlots(required, free.Value);
            }

            return InventoryOperationResult.Ok;
        }

        public InventoryOperationResult TryAdd(IReadOnlyList<ItemAmount> items)
        {
            EnsureLoaded();

            var check = CanAdd(items);
            if (!check.Success || items == null || items.Count == 0)
            {
                return check;
            }

            var totals = StackPacker.Aggregate(items);
            var nextStackId = _snapshot.NextStackId;
            var stacks = StackPacker.Add(_snapshot.Stacks, _config.Catalog, totals, ref nextStackId);

            Commit(new InventorySnapshot(stacks, _snapshot.Revision + 1, nextStackId));
            return InventoryOperationResult.Ok;
        }

        public InventoryOperationResult CanRemove(IReadOnlyList<ItemAmount> items)
        {
            EnsureInitialized();

            if (items == null || items.Count == 0)
            {
                return InventoryOperationResult.Ok;
            }

            var totals = StackPacker.Aggregate(items);

            foreach (var pair in totals)
            {
                if (StackPacker.CountOf(_snapshot.Stacks, pair.Key) < pair.Value)
                {
                    return InventoryOperationResult.NotEnoughItems(pair.Key);
                }
            }

            return InventoryOperationResult.Ok;
        }

        public InventoryOperationResult TryRemove(IReadOnlyList<ItemAmount> items)
        {
            EnsureLoaded();

            var check = CanRemove(items);
            if (!check.Success || items == null || items.Count == 0)
            {
                return check;
            }

            var totals = StackPacker.Aggregate(items);
            var stacks = StackPacker.Remove(_snapshot.Stacks, totals);

            Commit(new InventorySnapshot(stacks, _snapshot.Revision + 1, _snapshot.NextStackId));
            return InventoryOperationResult.Ok;
        }

        public InventoryOperationResult TryRemove(string itemId, int count)
        {
            return TryRemove(new[] { new ItemAmount(itemId, count) });
        }

        /// <summary>Drops one whole stack by identity — the "discard" action in the window.</summary>
        public bool TryRemoveStack(long stackId)
        {
            return TryRemoveFromStack(stackId, int.MaxValue);
        }

        /// <summary>Takes up to <paramref name="count"/> units from one specific stack — the "use" action, which
        /// must spend from the cell the player tapped, not from whichever stack is smallest.</summary>
        public bool TryRemoveFromStack(long stackId, int count)
        {
            EnsureLoaded();

            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Remove at least 1 unit.");
            }

            var stacks = new List<ItemStack>(_snapshot.Stacks.Count);
            var found = false;

            for (var i = 0; i < _snapshot.Stacks.Count; i++)
            {
                var stack = _snapshot.Stacks[i];
                if (stack.StackId == stackId)
                {
                    found = true;

                    if (stack.Count > count)
                    {
                        stacks.Add(stack.WithCount(stack.Count - count));
                    }

                    continue;
                }

                stacks.Add(stack);
            }

            if (!found)
            {
                return false;
            }

            Commit(new InventorySnapshot(stacks, _snapshot.Revision + 1, _snapshot.NextStackId));
            return true;
        }

        /// <summary>Holds <see cref="Changed"/> back until the scope ends, so a transaction spanning several
        /// services (see <c>RewardGrantService</c>) notifies only after its last step.</summary>
        internal StateChangeNotifier.Scope DeferNotifications()
        {
            return _changed.Defer();
        }

        // State first, then the save, then observers: by the time any subscriber runs, the mutation is complete
        // and already queued for storage, so nothing a subscriber does (or throws) can lose it.
        private void Commit(InventorySnapshot snapshot)
        {
            _snapshot = snapshot;
            ScheduleSave();
            _changed.Raise();
        }

        // Saves are serialised through one loop: a second mutation while a write is in flight marks it dirty, and
        // the loop writes the latest snapshot once more when the current write ends. Two writes never race, and
        // the file always ends up holding the newest state.
        private void ScheduleSave()
        {
            if (_isSaving)
            {
                _isSaveDirty = true;
                return;
            }

            SaveLoopAsync().Forget();
        }

        private async UniTaskVoid SaveLoopAsync()
        {
            _isSaving = true;

            try
            {
                do
                {
                    _isSaveDirty = false;
                    var snapshot = _snapshot;
                    var playerId = _playerId;

                    try
                    {
                        await _storage.SaveAsync(playerId, snapshot, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
                while (_isSaveDirty);
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void EnsureInitialized()
        {
            if (_config == null)
            {
                throw new InvalidOperationException("InventoryManager.Initialize(config) has not been called.");
            }
        }

        private void EnsureLoaded()
        {
            EnsureInitialized();

            if (_playerId == null)
            {
                throw new InvalidOperationException("InventoryManager.Load(playerId, snapshot) has not been called.");
            }
        }
    }
}
