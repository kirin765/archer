using UnityEngine;

namespace Archer
{
    /// <summary>Two local languages; an explicit selection overrides the device language.</summary>
    public static class GameLanguage
    {
        const string Key = "LastRanger.Language";
        public static bool Korean => PlayerPrefs.GetInt(Key, Application.systemLanguage == SystemLanguage.Korean ? 1 : 0) == 1;
        public static string Text(string english, string korean) => Korean ? korean : english;
        public static void Toggle()
        {
            PlayerPrefs.SetInt(Key, Korean ? 0 : 1);
            PlayerPrefs.Save();
        }
    }
}
