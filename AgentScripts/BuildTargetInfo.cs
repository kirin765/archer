using UnityEditor;
using UnityEngine;

/// <summary>Reports the editor's active build target.</summary>
public static class BuildTargetInfo
{
    public static string Main()
    {
        return $"activeBuildTarget={EditorUserBuildSettings.activeBuildTarget} " +
               $"group={EditorUserBuildSettings.selectedBuildTargetGroup} " +
               $"isPlaying={EditorApplication.isPlaying} " +
               $"androidModule={ModuleManager.IsPlatformSupportLoaded("Android")}";
    }
}
