using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MagicCave.Prototype.Editor
{
    public static class AttributePrototypeBuild
    {
        [MenuItem("Magic Cave/Build Windows Attribute Prototype")]
        public static void BuildWindows()
        {
            CharacterAttributes.ValidateRules(PrototypeAttributeSet.LoadRules());
            string destination = Path.GetFullPath("Builds/WindowsAttributes/MagicCaveGame.exe");
            var arguments = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(arguments, "-attributeBuildPath");
            if (option >= 0 && option + 1 < arguments.Length) destination = Path.GetFullPath(arguments[option + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { WorldMapSceneBuilder.ScenePath }, locationPathName = destination,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(destination), "build-summary.txt"),
                $"{report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings; {report.summary.totalSize} bytes.\n");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("角色属性原型构建失败，请查看Unity日志。");
            Debug.Log("[MagicCave] Attribute prototype ready: " + destination);
        }
    }
}
