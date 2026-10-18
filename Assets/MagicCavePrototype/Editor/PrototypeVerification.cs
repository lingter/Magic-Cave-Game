using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MagicCave.Prototype.Editor
{
    public static class PrototypeVerification
    {
        public const string RequestPath = "Temp/PrototypePixel.tests.request";
        const string PhaseKey = "MagicCave.PixelVerification.Phase";
        const string OutputPath = "Captures/MagicCavePrototype/PixelArt/";
        public static bool IsRunning => SessionState.GetInt(PhaseKey, 0) != 0;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            TestRunnerApi.RegisterTestCallback(new Results());
            if (File.Exists(RequestPath)) EditorApplication.update += StartRequested;
        }

        static void StartRequested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                File.Exists("Temp/PrototypePixel.upgrade.request") || Resources.Load<PrototypeArtSet>("PrototypeArt") == null) return;
            EditorApplication.update -= StartRequested; File.Delete(RequestPath); Run();
        }

        [MenuItem("Magic Cave/Verify Pixel Prototype")]
        public static void Run()
        {
            Directory.CreateDirectory(OutputPath); SessionState.SetInt(PhaseKey, 1);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "MagicCave.Prototype.Tests" } }));
        }

        static void RunPlayMode()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += RunPlayMode; return; }
            SessionState.SetInt(PhaseKey, 2);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                assemblyNames = new[] { "MagicCave.Prototype.PlayTests", "MagicCave.ArtValidation.PlayTests" }
            }));
        }

        sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                int phase = SessionState.GetInt(PhaseKey, 0); if (phase == 0) return;
                string name = phase == 1 ? "editmode" : "playmode";
                TestRunnerApi.SaveResultToFile(result, OutputPath + name + "-results.xml");
                File.WriteAllText(OutputPath + name + "-summary.txt", $"{result.ResultState}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.\n{result.Message}\n");
                Debug.Log($"[MagicCave] Pixel {name}: {result.PassCount} passed, {result.FailCount} failed.");
                if (phase == 1 && result.FailCount == 0 && result.PassCount > 0) EditorApplication.delayCall += RunPlayMode;
                else
                {
                    SessionState.SetInt(PhaseKey, 0);
                    if (result.FailCount > 0 || result.PassCount == 0)
                    {
                        // A failed verification must not publish a new local build.
                        if (File.Exists("Temp/PrototypePixel.build.request")) File.Delete("Temp/PrototypePixel.build.request");
                    }
                }
            }
        }
    }
}
