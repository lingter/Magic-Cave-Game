using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MagicCave.ArtValidation.Editor
{
    public static class ArtValidationTestRunner
    {
        const string RequestPath = "Temp/ArtValidation10x10.tests.request";
        const string RunningKey = "MagicCave.ArtValidation.TestsRunning";

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            TestRunnerApi.RegisterTestCallback(new Results());
            if (File.Exists(RequestPath)) EditorApplication.update += RunRequested;
        }

        static void RunRequested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                File.Exists("Temp/ArtValidation10x10.rebuild.request") ||
                !File.Exists("Assets/Scenes/ArtValidation10x10.unity")) return;
            EditorApplication.update -= RunRequested;
            File.Delete(RequestPath); Run();
        }

        [MenuItem("Magic Cave/Art Validation/Run PlayMode Checks")]
        public static void Run()
        {
            SessionState.SetBool(RunningKey, true);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, assemblyNames = new[] { "MagicCave.ArtValidation.PlayTests" } }));
        }

        sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(RunningKey, false)) return;
                SessionState.SetBool(RunningKey, false);
                Directory.CreateDirectory("Captures/ArtValidation10x10");
                TestRunnerApi.SaveResultToFile(result, "Captures/ArtValidation10x10/playmode-results.xml");
                string status = result.PassCount + result.FailCount + result.SkipCount == 0 ? "No tests executed" : result.ResultState;
                File.WriteAllText("Captures/ArtValidation10x10/playmode-summary.txt", $"{status}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.\n{result.Message}\n");
                Debug.Log($"[ArtValidation] PlayMode checks: {result.ResultState}, {result.PassCount} passed, {result.FailCount} failed.");
            }
        }
    }
}
