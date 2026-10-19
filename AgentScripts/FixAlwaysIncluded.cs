using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AgentScripts
{
    /// <summary>
    /// Ensure the URP shaders that Prim.cs resolves with Shader.Find at runtime are
    /// actually present in player builds. Without this Unity strips them (nothing in
    /// the scenes references them) and new Material(null) throws
    /// "ArgumentNullException: Parameter name: shader" in WebGL/Android builds.
    /// </summary>
    public static class FixAlwaysIncluded
    {
        static void Add(SerializedProperty arr, Shader shader, List<string> log)
        {
            if (shader == null)
            {
                log.Add("MISSING(Shader.Find returned null)");
                return;
            }

            for (int i = 0; i < arr.arraySize; i++)
            {
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    log.Add("already:" + shader.name);
                    return;
                }
            }

            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            log.Add("added:" + shader.name);
        }

        public static string Run()
        {
            Object settings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (settings == null) settings = GraphicsSettings.GetGraphicsSettings();

            var so = new SerializedObject(settings);
            so.Update();
            SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return "ERROR: m_AlwaysIncludedShaders not found";

            var log = new List<string>();
            Add(arr, Shader.Find("Universal Render Pipeline/Lit"), log);
            Add(arr, Shader.Find("Universal Render Pipeline/Unlit"), log);

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            var names = new List<string>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var s = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                names.Add(s != null ? s.name : "null");
            }

            return "count=" + arr.arraySize + " | " + string.Join(", ", log) + " | list=[" + string.Join(", ", names) + "]";
        }
    }
}
