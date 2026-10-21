using UnityEngine;

namespace Archer
{
    /// <summary>What changed when a completed run was compared with local records.</summary>
    public readonly struct RunRecordSummary
    {
        public readonly bool NewScore, NewAccuracy, NewHeadshots, NewRaids;
        public readonly bool AccuracyEligible;
        public readonly int BestScore, BestHeadshots, BestRaids;
        public readonly float BestAccuracy;

        public RunRecordSummary(bool newScore, bool newAccuracy, bool newHeadshots, bool newRaids,
            bool accuracyEligible, int bestScore, float bestAccuracy, int bestHeadshots, int bestRaids)
        {
            NewScore = newScore; NewAccuracy = newAccuracy; NewHeadshots = newHeadshots; NewRaids = newRaids;
            AccuracyEligible = accuracyEligible;
            BestScore = bestScore; BestAccuracy = bestAccuracy; BestHeadshots = bestHeadshots; BestRaids = bestRaids;
        }

        public bool AnyNew => NewScore || NewAccuracy || NewHeadshots || NewRaids;
    }

    /// <summary>
    /// Local personal records. Accuracy only counts when the run had enough
    /// shots to be meaningful; the threshold is shown in the results card.
    /// </summary>
    public static class PlayerRecords
    {
        public const int AccuracyMinShots = 20;

        const string ScoreKey = "LastRanger.Record.Score";
        const string AccuracyKey = "LastRanger.Record.Accuracy";
        const string HeadshotsKey = "LastRanger.Record.Headshots";
        const string RaidsKey = "LastRanger.Record.Raids";

        public static int BestScore => PlayerPrefs.GetInt(ScoreKey, 0);
        public static float BestAccuracy => PlayerPrefs.GetFloat(AccuracyKey, 0f);
        public static int BestHeadshots => PlayerPrefs.GetInt(HeadshotsKey, 0);
        public static int BestRaids => PlayerPrefs.GetInt(RaidsKey, 0);

        public static bool HasRecords => BestScore > 0 || BestRaids > 0 || BestHeadshots > 0;

        public static RunRecordSummary SubmitRun(GameManager gm)
        {
            bool accuracyEligible = gm.ShotsFired >= AccuracyMinShots;
            bool newScore = gm.Score > BestScore;
            bool newHeadshots = gm.Headshots > BestHeadshots;
            bool newRaids = gm.RaidsCleared > BestRaids;
            bool newAccuracy = accuracyEligible && gm.Accuracy > BestAccuracy + 0.0001f;

            if (newScore) PlayerPrefs.SetInt(ScoreKey, gm.Score);
            if (newHeadshots) PlayerPrefs.SetInt(HeadshotsKey, gm.Headshots);
            if (newRaids) PlayerPrefs.SetInt(RaidsKey, gm.RaidsCleared);
            if (newAccuracy) PlayerPrefs.SetFloat(AccuracyKey, gm.Accuracy);
            if (newScore || newHeadshots || newRaids || newAccuracy) PlayerPrefs.Save();

            return new RunRecordSummary(newScore, newAccuracy, newHeadshots, newRaids,
                accuracyEligible, BestScore, BestAccuracy, BestHeadshots, BestRaids);
        }
    }
}
