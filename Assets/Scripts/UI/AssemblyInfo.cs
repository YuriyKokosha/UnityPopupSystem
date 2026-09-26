using System.Runtime.CompilerServices;

// The PlayMode suite drives the real views and needs their buttons and pool hooks; nothing else does.
[assembly: InternalsVisibleTo("PopupSystem.Tests.PlayMode")]
