using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;

/// <summary>Survives the domain reload a PlayMode run triggers (hence [InitializeOnLoad]) and records the
/// outcome of any test run in EditorPrefs and in <c>Claude outputs/TestResults/&lt;mode&gt;.txt</c>, so an
/// external agent without a Test Runner window can read it. <see cref="TestRunMenu"/> starts the runs.</summary>
[InitializeOnLoad]
public static class PlayModeResultProbe
{
    private const string OutputDirectory = "Claude outputs/TestResults";

    static PlayModeResultProbe()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Reporter());
    }

    private sealed class Reporter : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            EditorPrefs.SetString("ps_pm_status", "running");
            EditorPrefs.SetString("ps_pm_failures", "");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var summary = string.Format(
                "passed={0} failed={1} skipped={2} inconclusive={3} duration={4:0.0}s",
                result.PassCount, result.FailCount, result.SkipCount, result.InconclusiveCount, result.Duration);
            var failures = Collect(result, "");

            EditorPrefs.SetString("ps_pm_status", summary);
            EditorPrefs.SetString("ps_pm_failures", failures);

            var mode = result.Test.TestMode;
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, mode + ".txt"), summary + "\n" + failures);
            Debug.Log($"[PlayModeResultProbe] {mode}: {summary}");
        }

        private static string Collect(ITestResultAdaptor r, string acc)
        {
            if (r.Test.IsSuite)
            {
                foreach (var child in r.Children)
                {
                    acc = Collect(child, acc);
                }

                return acc;
            }

            if (r.TestStatus != TestStatus.Passed)
            {
                var msg = (r.Message ?? "").Replace("\n", " / ");
                if (msg.Length > 600)
                {
                    msg = msg.Substring(0, 600);
                }

                acc += r.TestStatus + " " + r.Test.FullName + " :: " + msg + "\n";
            }

            return acc;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) { }
    }
}
