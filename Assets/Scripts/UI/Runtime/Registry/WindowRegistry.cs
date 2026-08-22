using System;
using System.Collections.Generic;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;

namespace PopupSystem.UI.Runtime.Registry
{
    /// <summary>
    /// Fully generic: it knows nothing about any specific window type. Every IWindowModule bound
    /// in the installer (see AppInstaller) contributes its own WindowDefinition - adding a new
    /// window type means writing one new XxxWindowModule next to that window and binding it,
    /// never touching this class (mirrors the IWindowQueueAggregator multi-binding pattern
    /// already used by WindowQueueRunner).
    /// </summary>
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
