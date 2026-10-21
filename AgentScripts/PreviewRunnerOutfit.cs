using System.Threading.Tasks;
using UnityEngine;
using Archer;

/// <summary>
/// Editor-only preview helper (run via the pipeline, never shipped):
/// spawns a runner goblin in front of the main camera so the outfit
/// can be screenshotted, then parks it in the run pose.
/// </summary>
public static class PreviewRunnerOutfit
{
    static Monster _preview;

    public static async Task<string> Spawn()
    {
        // Leave the intro/settings overlay and get the arena running.
        var gm = GameManager.Instance;
        if (gm != null && gm.State != GameState.Tutorial && gm.State != GameState.Playing)
            gm.StartTutorial();
        Time.timeScale = 1f;
        await Task.Delay(1500);

        var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam == null) return $"no camera; state={gm?.State}";
        cam.tag = "MainCamera";
        var camT = cam.transform;

        if (_preview != null) Object.Destroy(_preview.gameObject);
        Vector3 pos = camT.position + Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized * 5.5f;
        pos.y = 0.02f;

        // Run toward the camera at tutorial speed so the walk cycle plays;
        // practice=true so it can never hurt anything.
        _preview = Monster.Create(null, pos, camT.root, MonsterRole.Runner,
            health: 999f, speed: 2.4f, damage: 0f, attackInterval: 99f,
            color: new Color(0.35f, 0.55f, 0.30f), practice: true);

        // Let it take a few strides into frame before the screenshot.
        await Task.Delay(1100);
        return $"spawned runner at {pos} -> camera at {camT.position}";
    }
}
