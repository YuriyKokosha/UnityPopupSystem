using System;
using System.Collections.Generic;

namespace PopupSystem.Game.Domain.Inventory
{
    public sealed class ItemCatalog
    {
        public static readonly ItemCatalog Empty = new(Array.Empty<ItemDefinition>());

        private readonly Dictionary<string, ItemDefinition> _byId;

        public ItemCatalog(IReadOnlyList<ItemDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            _byId = new Dictionary<string, ItemDefinition>(definitions.Count);

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];

                if (_byId.ContainsKey(definition.ItemId))
                {
                    throw new ArgumentException($"Item '{definition.ItemId}' is defined twice.", nameof(definitions));
                }

                _byId.Add(definition.ItemId, definition);
            }

            Definitions = definitions;
        }

        public IReadOnlyList<ItemDefinition> Definitions { get; }

        public int Count => _byId.Count;

        public bool Contains(string itemId)
        {
            return itemId != null && _byId.ContainsKey(itemId);
        }

        public bool TryGet(string itemId, out ItemDefinition definition)
        {
            if (itemId == null)
            {
                definition = null;
                return false;
            }

            return _byId.TryGetValue(itemId, out definition);
        }

        /// <summary>Throws on an unknown id: an item that is not in the catalog cannot be stacked or drawn, and
        /// silently treating it as MaxStack = 1 would hide a config error.</summary>
        public ItemDefinition Get(string itemId)
        {
            if (TryGet(itemId, out var definition))
            {
                return definition;
            }

            throw new InvalidOperationException($"Item '{itemId}' is not in the item catalog.");
        }
    }
}
