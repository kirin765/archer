using System.Text;
using UnityEngine;

public static class MeasureScenery
{
    public static string Main()
    {
        var world = GameObject.Find("World");
        if (world == null) return "World not found (not in play mode?)";
        var renderers = world.GetComponentsInChildren<Renderer>(true);
        int meshRenderers = 0, skinned = 0, other = 0;
        long tris = 0;
        var materials = new System.Collections.Generic.HashSet<Material>();
        var meshes = new System.Collections.Generic.HashSet<Mesh>();
        foreach (var r in renderers)
        {
            if (r is MeshRenderer) meshRenderers++;
            else if (r is SkinnedMeshRenderer) skinned++;
            else { other++; continue; }
            foreach (var m in r.sharedMaterials) if (m != null) materials.Add(m);
            Mesh mesh = null;
            if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh == null) continue;
            meshes.Add(mesh);
            for (int s = 0; s < mesh.subMeshCount; s++) tris += mesh.GetIndexCount(s) / 3;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Renderers: {renderers.Length} (mesh {meshRenderers}, skinned {skinned}, other {other})");
        sb.AppendLine($"Unique materials: {materials.Count}, unique meshes: {meshes.Count}, triangles (scene sum): {tris}");
        var lights = world.GetComponentsInChildren<Light>(true);
        sb.AppendLine($"Lights: {lights.Length}");
        return sb.ToString();
    }
}
