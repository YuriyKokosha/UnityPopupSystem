using System;
using System.Collections.Generic;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Inventory
{
    public sealed class PlayerInventoryManager
    {
        private readonly Dictionary<string, int> _balances = new();

        public event Action BalancesChanged;

        public void SetInventory(InventorySnapshot snapshot)
        {
            _balances.Clear();

            if (snapshot?.Resources != null)
            {
                for (var i = 0; i < snapshot.Resources.Count; i++)
                {
                    var resource = snapshot.Resources[i];
                    _balances[resource.ResourceId] = resource.Amount;
                }
            }

            BalancesChanged?.Invoke();
        }

        public void AddResources(IReadOnlyList<InventoryResource> resources)
        {
            if (resources == null)
            {
                return;
            }

            for (var i = 0; i < resources.Count; i++)
            {
                var resource = resources[i];
                _balances.TryGetValue(resource.ResourceId, out var currentAmount);
                _balances[resource.ResourceId] = currentAmount + resource.Amount;
            }

            BalancesChanged?.Invoke();
        }

        public IReadOnlyList<InventoryResource> GetSnapshot()
        {
            var resources = new List<InventoryResource>(_balances.Count);

            foreach (var pair in _balances)
            {
                resources.Add(new InventoryResource(pair.Key, pair.Value));
            }

            return resources;
        }
    }
}
