using UnityEngine;

public static class StartRaidForShot
{
    public static string Main()
    {
        var gm = Object.FindObjectOfType<Archer.GameManager>();
        if (gm == null) return "GameManager not found";
        if (gm.State == Archer.GameState.Intermission && gm.RaidNumber == 0)
            gm.BeginRun();
        if (gm.State == Archer.GameState.Intermission)
            gm.StartRaid();
        return $"State={gm.State} Raid={gm.RaidNumber}";
    }
}
