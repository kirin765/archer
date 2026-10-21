using UnityEditor;
using Archer.EditorTools;

/// <summary>Runs the goblin LOD build and reports the result.</summary>
public static class GoblinOptimizeRun
{
    public static string Main()
    {
        return GoblinMeshOptimizer.Run();
    }
}
