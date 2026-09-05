using System.Runtime.CompilerServices;

// WindowHandle.SetState/MarkClosed are internal: only these two assemblies may drive a lifecycle.
[assembly: InternalsVisibleTo("PopupSystem.UI")]
[assembly: InternalsVisibleTo("PopupSystem.Tests.EditMode")]
