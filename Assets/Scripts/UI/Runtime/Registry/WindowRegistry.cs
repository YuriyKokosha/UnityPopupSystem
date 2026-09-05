using System;
using System.Collections.Generic;
using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;

namespace PopupSystem.UI.Runtime.Registry
{
    public sealed class WindowRegistry : IWindowRegistry
    {
        private readonly Dictionary<WindowType, WindowDefinition> _definitions;

        public WindowRegistry(List<IWindowModule> modules)
        {
            _definitions = new Dictionary<WindowType, WindowDefinition>();
            foreach (var module in modules)
            {
                _definitions[module.Definition.Type] = module.Definition;
            }
        }

        public WindowDefinition Get(WindowType type)
        {
            if (_definitions.TryGetValue(type, out var definition))
            {
                return definition;
            }

            throw new InvalidOperationException($"Window definition not found for {type}");
        }
    }
}
