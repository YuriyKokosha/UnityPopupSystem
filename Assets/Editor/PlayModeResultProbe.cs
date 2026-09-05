using UnityEditor;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;

[InitializeOnLoad]
public static class PlayModeResultProbe
{
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
            EditorPrefs.SetString("ps_pm_status", string.Format(
                "passed={0} failed={1} skipped={2} inconclusive={3} duration={4:0.0}s",
                result.PassCount, result.FailCount, result.SkipCount, result.InconclusiveCount, result.Duration));
            EditorPrefs.SetString("ps_pm_failures", Collect(result, ""));
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
                if (msg.Length > 400)
                {
                    msg = msg.Substring(0, 400);
                }

                acc += r.Test.Name + " [" + r.TestStatus + "] " + msg + "\n";
            }

            return acc;
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) { }
    }
}
