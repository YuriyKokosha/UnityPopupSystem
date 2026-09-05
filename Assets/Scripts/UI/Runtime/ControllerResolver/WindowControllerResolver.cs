using System;
using System.Collections.Generic;
using PopupSystem.Contracts;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Runtime.ControllerResolver
{
    public sealed class WindowControllerResolver : IWindowControllerResolver
    {
        private readonly Dictionary<WindowType, IWindowModule> _modulesByType;

        public WindowControllerResolver(List<IWindowModule> modules)
        {
            _modulesByType = new Dictionary<WindowType, IWindowModule>();
            foreach (var module in modules)
            {
                _modulesByType[module.Definition.Type] = module;
            }
        }

        public IWindowController Create(WindowType type)
        {
            if (_modulesByType.TryGetValue(type, out var module))
            {
                return module.CreateController();
            }

            throw new InvalidOperationException($"No IWindowModule registered for {type}");
        }
    }
}
