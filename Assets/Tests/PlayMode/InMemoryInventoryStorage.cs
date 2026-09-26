using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;

namespace PopupSystem.Tests.PlayMode
{
    /// <summary>Storage for the window fixtures: the windows under test never touch a file, and a real
    /// FileInventoryStorage would write under the developer's persistentDataPath.</summary>
    internal sealed class InMemoryInventoryStorage : IInventoryStorage
    {
        private readonly Dictionary<string, InventorySnapshot> _saved = new();

        public UniTask<InventorySnapshot> LoadAsync(string playerId, CancellationToken cancellationToken)
        {
            _saved.TryGetValue(playerId, out var snapshot);
            return UniTask.FromResult(snapshot);
        }

        public UniTask SaveAsync(string playerId, InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            _saved[playerId] = snapshot;
            return UniTask.CompletedTask;
        }

        public UniTask ClearAsync(string playerId, CancellationToken cancellationToken)
        {
            _saved.Remove(playerId);
            return UniTask.CompletedTask;
        }
    }
}
