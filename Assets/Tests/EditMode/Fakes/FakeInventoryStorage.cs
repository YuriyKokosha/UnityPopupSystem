using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>In-memory storage that completes synchronously, so one mutation is exactly one save - unless
    /// <see cref="HoldSaves"/> is set, in which case every write stays in flight until the test releases it.</summary>
    public sealed class FakeInventoryStorage : IInventoryStorage
    {
        private readonly Dictionary<string, InventorySnapshot> _saved = new();
        private readonly Queue<UniTaskCompletionSource> _heldSaves = new();

        public int SaveCount { get; private set; }

        public bool ThrowOnLoad { get; set; }

        public bool ThrowOnSave { get; set; }

        public InventorySnapshot LastSaved { get; private set; }

        /// <summary>The snapshot each SaveAsync call was given, in call order - written or still held.</summary>
        public List<InventorySnapshot> SaveRequests { get; } = new();

        /// <summary>Makes SaveAsync return a task that only completes on <see cref="ReleaseNextSave"/>.</summary>
        public bool HoldSaves { get; set; }

        public int HeldSaveCount => _heldSaves.Count;

        /// <summary>Completes the oldest held write. Its continuation - the manager's save loop - runs
        /// synchronously inside this call.</summary>
        public void ReleaseNextSave()
        {
            _heldSaves.Dequeue().TrySetResult();
        }

        public void Seed(string playerId, InventorySnapshot snapshot)
        {
            _saved[playerId] = snapshot;
        }

        public UniTask<InventorySnapshot> LoadAsync(string playerId, CancellationToken cancellationToken)
        {
            if (ThrowOnLoad)
            {
                throw new InvalidOperationException("[Expected] Corrupt inventory file.");
            }

            _saved.TryGetValue(playerId, out var snapshot);
            return UniTask.FromResult(snapshot);
        }

        public UniTask SaveAsync(string playerId, InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            SaveCount++;
            SaveRequests.Add(snapshot);

            if (ThrowOnSave)
            {
                throw new InvalidOperationException("[Expected] Disk full.");
            }

            _saved[playerId] = snapshot;
            LastSaved = snapshot;

            if (!HoldSaves)
            {
                return UniTask.CompletedTask;
            }

            var held = new UniTaskCompletionSource();
            _heldSaves.Enqueue(held);
            return held.Task;
        }

        public UniTask ClearAsync(string playerId, CancellationToken cancellationToken)
        {
            _saved.Remove(playerId);
            return UniTask.CompletedTask;
        }
    }
}
