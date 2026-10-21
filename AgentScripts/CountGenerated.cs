using UnityEngine;

public static class CountGenerated
{
    public static string Main()
    {
        int batchedMeshes = 0, firefly = 0, runtimeMaterials = 0, worlds = 0;
        foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
        {
            if (m.name.StartsWith("Batched ")) batchedMeshes++;
            if (m.name == "Fireflies") firefly++;
        }
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m.name.StartsWith("Mat_") || m.name.StartsWith("Emissive_") || m.name.StartsWith("Unlit_") || m.name == "Fireflies")
                runtimeMaterials++;
        }
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go.name == "World") worlds++;
        return $"batchedMeshes={batchedMeshes} fireflyMesh={firefly} runtimeMaterials={runtimeMaterials} worldObjects={worlds}";
    }
}
