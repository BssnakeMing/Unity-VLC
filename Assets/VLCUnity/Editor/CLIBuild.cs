using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class CLIBuild
{
    private const string AndroidBuildDirectory = "Builds/Android";
    private const string AndroidApkPath = AndroidBuildDirectory + "/vlcdemottt.apk";

    public static void BuildLinux()
    {
        var scenes = new string[EditorBuildSettings.scenes.Length];
        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            scenes[i] = EditorBuildSettings.scenes[i].path;

        if (scenes.Length == 0)
        {
            Debug.LogError("No scenes in build settings!");
            EditorApplication.Exit(1);
            return;
        }

        var report = BuildPipeline.BuildPlayer(scenes, "build/app.x86_64",
            BuildTarget.StandaloneLinux64, BuildOptions.None);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("Build failed: " + report.summary.result);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/VLC Unity/Build Android Development APK")]
    public static void BuildAndroidDevelopment()
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                scenes.Add(scene.path);
        }

        if (scenes.Count == 0)
            throw new BuildFailedException("No enabled scenes in Build Settings.");

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new BuildFailedException("Failed to switch active build target to Android.");

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;

        Directory.CreateDirectory(AndroidBuildDirectory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = AndroidApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Android APK build failed: " + report.summary.result);

        Debug.Log("Android development APK built: " + Path.GetFullPath(AndroidApkPath));
    }
}
