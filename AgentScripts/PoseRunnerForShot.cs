using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Editor-only preview helper (run via the pipeline, never shipped):
/// parks the preview runner at a fixed distance from the camera facing
/// it, optionally showing its back, and pauses it there.
/// </summary>
public static class PoseRunnerForShot
{
    public static async Task<string> Front()
    {
        return await Pose(false);
    }

    public static async Task<string> Back()
    {
        return await Pose(true);
    }

    public static async Task<string> Far()
    {
        return await Pose(false, 12f);
    }

    static async Task<string> Pose(bool back, float dist = 4.2f)
    {
        var cam = Camera.main;
        GameObject runner = null;
        foreach (var go in Object.FindObjectsOfType<GameObject>())
            if (go.name == "Monster_Runner") { runner = go; break; }
        if (cam == null || runner == null) return "missing camera or runner";

        runner.GetComponent<Archer.Monster>().enabled = false;   // hold the pose

        var camT = cam.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
        Vector3 pos = camT.position + fwd * dist;
        pos.y = 0.02f;
        runner.transform.position = pos;
        runner.transform.rotation = Quaternion.LookRotation(back ? -fwd : fwd);

        await Task.Delay(250);
        return $"posed {(back ? "back" : "front")} at {pos}";
    }
}
