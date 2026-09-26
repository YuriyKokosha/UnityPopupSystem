using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>Starts a test suite from a menu item, so a run can be driven by an external agent that has no Test
/// Runner window. The outcome lands in <c>Claude outputs/TestResults/&lt;mode&gt;.txt</c>, written by
/// <see cref="PlayModeResultProbe"/> — the reporter has to live in an [InitializeOnLoad] type because a
/// PlayMode run reloads the domain and would drop a callback registered here.</summary>
public static class TestRunMenu
{
    private const string OutputDirectory = "Claude outputs/TestResults";

    [MenuItem("Tools/Tests/Run EditMode tests (write results)")]
    public static void RunEditMode() => Run(TestMode.EditMode);

    [MenuItem("Tools/Tests/Run PlayMode tests (write results)")]
    public static void RunPlayMode() => Run(TestMode.PlayMode);

    private static void Run(TestMode mode)
    {
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, mode + ".txt"), "running\n");

        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = mode }));
    }
}
