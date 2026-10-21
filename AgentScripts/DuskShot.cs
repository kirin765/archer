using System.Reflection;
using UnityEngine;

public static class DuskShot
{
    public static string Main()
    {
        var gm = Object.FindAnyObjectByType<Archer.GameManager>();
        if (gm == null) return "GameManager not found";
        gm.Restart();
        var tod = Object.FindAnyObjectByType<Archer.TimeOfDay>();
        if (tod != null)
        {
            var f = typeof(Archer.TimeOfDay).GetField("_time01", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(tod, 0.657f);
        }
        if (gm.State == Archer.GameState.Intermission) gm.StartRaid();
        return $"State={gm.State} Raid={gm.RaidNumber} Time01={(tod != null ? tod.Time01 : -1):0.000}";
    }
}
