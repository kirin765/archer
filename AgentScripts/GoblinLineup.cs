using System.Threading.Tasks;
using UnityEngine;
using Archer;

/// <summary>
/// Editor-only preview helper (run via the pipeline, never shipped):
/// spawns one goblin of each role in a line in front of the main camera so
/// silhouettes, materials and animation can be inspected in a single shot.
/// </summary>
public static class GoblinLineup
{
    static readonly MonsterRole[] Roles =
    {
        MonsterRole.Runner, MonsterRole.Raider, MonsterRole.Shield, MonsterRole.Ogre
    };

    public static async Task<string> Spawn()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.State != GameState.Tutorial && gm.State != GameState.Playing)
            gm.StartTutorial();
        Time.timeScale = 1f;
        await Task.Delay(1200);

        var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam == null) return $"no camera; state={gm?.State}";
        cam.tag = "MainCamera";
        var camT = cam.transform;

        foreach (var m in Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
            Object.Destroy(m.gameObject);
        await Task.Delay(100);

        Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float spacing = 1.15f;
        int i = 0;
        string log = "";
        foreach (var role in Roles)
        {
            float offset = (i - (Roles.Length - 1) * 0.5f) * spacing;
            Vector3 pos = camT.position + forward * 4.2f + right * offset;
            pos.y = 0.02f;
            var monster = Monster.Create(null, pos, camT.root, role,
                health: 999f, speed: 0f, damage: 0f, attackInterval: 999f,
                color: RoleColor(role), practice: true);
            // Face the camera so the front of the silhouette is visible.
            var look = camT.position - pos; look.y = 0f;
            if (look.sqrMagnitude > 0.001f) monster.transform.rotation = Quaternion.LookRotation(look);
            log += $"{role}:{pos} ";
            i++;
        }

        await Task.Delay(900);
        return $"lineup spawned {log}";
    }

    static Color RoleColor(MonsterRole role) => role switch
    {
        MonsterRole.Runner => new Color(0.72f, 0.86f, 0.40f),
        MonsterRole.Shield => new Color(0.54f, 0.52f, 0.32f),
        MonsterRole.Ogre => new Color(0.54f, 0.42f, 0.46f),
        _ => new Color(0.46f, 0.64f, 0.36f)
    };
}
