using UnityEngine;

/// <summary>Keeps the editor ticking while unfocused so play-mode captures work.</summary>
public static class FocusBackground
{
    public static string Main()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        return $"runInBackground={Application.runInBackground} frame={Time.frameCount} playing={Application.isPlaying}";
    }
}
