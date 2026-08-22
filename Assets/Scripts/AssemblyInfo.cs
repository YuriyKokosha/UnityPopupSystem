using System.Runtime.CompilerServices;

// Grants the EditMode tests access to internal members they need to build deterministic fakes -
// specifically WindowHandle.MarkClosed()/SetState() (see Assets/Scripts/UI/Runtime/WindowHandle.cs),
// which a fake IWindowsManager must be able to drive without exposing those as public API on the
// production type just for testing's sake.
//
// The target is "Assembly-CSharp-Editor", not a custom test-assembly name: a custom .asmdef
// cannot reference the default "Assembly-CSharp" assembly (Assembly-CSharp implicitly depends on
// every custom assembly, so the reverse reference is circular and Unity silently drops it - no
// error, just CS0234 "namespace does not exist" everywhere). The reliable way to unit-test code
// that still lives directly in Assembly-CSharp is the long-standing "magic Editor folder"
// convention: any script under a folder literally named "Editor" (see Assets/Tests/Editor/)
// compiles into the predefined Assembly-CSharp-Editor assembly, which - being an editor-default
// assembly - *is* allowed to (and by default does) reference Assembly-CSharp, plus already gets
// nunit.framework and UnityEngine/UnityEditor.TestRunner for free.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
