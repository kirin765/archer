using System.Collections.Generic;

namespace Archer
{
    /// <summary>
    /// One scheduled enemy spawn. Delay is measured in "beats" and scaled by
    /// <see cref="GameManager.SpawnInterval"/> at runtime, so tests can compress
    /// the pacing without rewriting the authored plans.
    /// </summary>
    public readonly struct SpawnOrder
    {
        public readonly MonsterRole Role;
        public readonly float Lane;   // -1..1 across the spawn width; 0 is the centre
        public readonly float Z;      // distance up the path; 0 = random within the raid band
        public readonly float Delay;  // beats after the previous spawn

        public SpawnOrder(MonsterRole role, float lane, float z, float delay)
        {
            Role = role; Lane = lane; Z = z; Delay = delay;
        }
    }

    /// <summary>
    /// Hand-authored raid compositions. Each raid is a small scene rather than a
    /// pile of stats: a staggered ambush, a shield wall with runners in its
    /// shadow, or a boss with a readable interruption window.
    /// </summary>
    public static class RaidPlan
    {
        /// <summary>Fill <paramref name="into"/> with the authored order for <paramref name="raid"/>.</summary>
        public static void Build(int raid, List<SpawnOrder> into)
        {
            into.Clear();
            switch (raid)
            {
                case 1: BuildFirstSigns(into); break;
                case 2: BuildTheRush(into); break;
                case 3: BuildWoodenShields(into); break;
                case 4: BuildTheAmbush(into); break;
                case 5: BuildTheBrute(into); break;
                case 6: BuildSecondWave(into); break;
                case 7: BuildIronRanks(into); break;
                case 8: BuildTheHunt(into); break;
                case 9: BuildBrutesGuard(into); break;
                case 10: BuildTheWarden(into); break;
                default: BuildEndless(raid, into); break;
            }
        }

        static void Add(List<SpawnOrder> into, MonsterRole role, float lane, float z, float delay)
            => into.Add(new SpawnOrder(role, lane, z, delay));

        // 1 · FIRST SIGNS — one at a time down the middle. Room to miss and learn.
        static void BuildFirstSigns(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Raider, 0.0f, 26f, 0.7f);
            Add(into, MonsterRole.Raider, 0.0f, 30f, 1.6f);
            Add(into, MonsterRole.Raider, -0.35f, 24f, 1.7f);
            Add(into, MonsterRole.Raider, 0.35f, 28f, 1.5f);
            Add(into, MonsterRole.Raider, 0.0f, 32f, 1.7f);
        }

        // 2 · THE RUSH — runners cut ahead of the line. Learn to lead a moving target.
        static void BuildTheRush(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Raider, -0.4f, 27f, 0.7f);
            Add(into, MonsterRole.Runner, 0.5f, 33f, 1.1f);    // breaks ahead of the pack
            Add(into, MonsterRole.Raider, 0.3f, 25f, 1.0f);
            Add(into, MonsterRole.Runner, -0.5f, 34f, 1.4f);
            Add(into, MonsterRole.Raider, 0.0f, 29f, 1.1f);
            Add(into, MonsterRole.Raider, -0.2f, 31f, 1.0f);
        }

        // 3 · WOODEN SHIELDS — a shield leads and a body follows in its shadow.
        static void BuildWoodenShields(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Shield, 0.0f, 24f, 0.7f);
            Add(into, MonsterRole.Raider, -0.5f, 31f, 1.1f);
            Add(into, MonsterRole.Raider, 0.5f, 31f, 0.8f);
            Add(into, MonsterRole.Shield, -0.3f, 27f, 1.6f);
            Add(into, MonsterRole.Raider, 0.3f, 26f, 0.8f);
            Add(into, MonsterRole.Runner, 0.4f, 35f, 1.2f);
        }

        // 4 · THE AMBUSH — two staggered lines; each shield has a runner behind it.
        static void BuildTheAmbush(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Raider, -0.6f, 26f, 0.5f);   // left probe
            Add(into, MonsterRole.Shield, -0.6f, 30f, 0.6f);   // guard steps up
            Add(into, MonsterRole.Runner, -0.6f, 35f, 0.5f);   // runner arrives behind the shield
            Add(into, MonsterRole.Raider, 0.6f, 26f, 1.5f);    // right line is staggered late
            Add(into, MonsterRole.Shield, 0.6f, 30f, 0.6f);
            Add(into, MonsterRole.Runner, 0.6f, 35f, 0.5f);
            Add(into, MonsterRole.Raider, 0.0f, 28f, 1.5f);    // centre follow-up
            Add(into, MonsterRole.Runner, 0.0f, 34f, 1.0f);
        }

        // 5 · THE BRUTE — the boss headlines; shields buy him time. Headshots interrupt.
        static void BuildTheBrute(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Ogre, 0.0f, 34f, 1.0f);      // headline
            Add(into, MonsterRole.Shield, -0.5f, 24f, 1.2f);   // cover for the brute
            Add(into, MonsterRole.Shield, 0.5f, 24f, 0.4f);
            Add(into, MonsterRole.Runner, -0.5f, 33f, 1.6f);
            Add(into, MonsterRole.Raider, 0.0f, 28f, 1.0f);
        }

        // 6 · THE SECOND WAVE — chapter two opens with a wider, faster push.
        static void BuildSecondWave(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Raider, -0.45f, 27f, 0.5f);
            Add(into, MonsterRole.Runner, 0.55f, 35f, 0.8f);
            Add(into, MonsterRole.Raider, 0.20f, 26f, 0.6f);
            Add(into, MonsterRole.Shield, -0.70f, 24f, 0.8f);
            Add(into, MonsterRole.Runner, -0.20f, 36f, 0.7f);
            Add(into, MonsterRole.Raider, 0.65f, 30f, 0.6f);
            Add(into, MonsterRole.Raider, 0.0f, 28f, 0.7f);
            Add(into, MonsterRole.Runner, 0.35f, 34f, 0.7f);
            Add(into, MonsterRole.Shield, 0.70f, 25f, 0.9f);
        }

        // 7 · IRON RANKS — a shield wall with runners using the gaps.
        static void BuildIronRanks(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Shield, -0.6f, 24f, 0.45f);
            Add(into, MonsterRole.Shield, 0.0f, 26f, 0.55f);
            Add(into, MonsterRole.Shield, 0.6f, 24f, 0.45f);
            Add(into, MonsterRole.Raider, -0.6f, 31f, 0.7f);
            Add(into, MonsterRole.Raider, 0.6f, 31f, 0.6f);
            Add(into, MonsterRole.Runner, -0.55f, 36f, 0.8f);
            Add(into, MonsterRole.Runner, 0.55f, 36f, 0.7f);
            Add(into, MonsterRole.Shield, 0.0f, 28f, 0.9f);
            Add(into, MonsterRole.Raider, 0.0f, 29f, 0.8f);
            Add(into, MonsterRole.Runner, 0.0f, 37f, 0.9f);
        }

        // 8 · THE HUNT — runners dominate; they must be led before the shields close.
        static void BuildTheHunt(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Runner, -0.65f, 36f, 0.4f);
            Add(into, MonsterRole.Runner, 0.65f, 36f, 0.45f);
            Add(into, MonsterRole.Raider, 0.0f, 28f, 0.55f);
            Add(into, MonsterRole.Runner, -0.25f, 35f, 0.65f);
            Add(into, MonsterRole.Shield, 0.45f, 25f, 0.75f);
            Add(into, MonsterRole.Runner, 0.25f, 37f, 0.55f);
            Add(into, MonsterRole.Raider, -0.55f, 30f, 0.65f);
            Add(into, MonsterRole.Shield, -0.45f, 26f, 0.75f);
            Add(into, MonsterRole.Runner, 0.0f, 36f, 0.55f);
            Add(into, MonsterRole.Raider, 0.55f, 29f, 0.7f);
        }

        // 9 · THE BRUTE'S GUARD — shields arrive first; the brute follows the wall.
        static void BuildBrutesGuard(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Shield, -0.55f, 24f, 0.45f);
            Add(into, MonsterRole.Shield, 0.55f, 24f, 0.45f);
            Add(into, MonsterRole.Raider, 0.0f, 28f, 0.6f);
            Add(into, MonsterRole.Shield, 0.0f, 26f, 0.55f);
            Add(into, MonsterRole.Runner, -0.55f, 35f, 0.7f);
            Add(into, MonsterRole.Runner, 0.55f, 35f, 0.6f);
            Add(into, MonsterRole.Raider, -0.3f, 29f, 0.7f);
            Add(into, MonsterRole.Raider, 0.3f, 31f, 0.6f);
            Add(into, MonsterRole.Ogre, 0.0f, 34f, 1.0f);
        }

        // 10 · THE WARDEN — the brute leads the last wave; the guard follows.
        static void BuildTheWarden(List<SpawnOrder> into)
        {
            Add(into, MonsterRole.Ogre, 0.0f, 34f, 0.8f);
            Add(into, MonsterRole.Shield, -0.65f, 24f, 1.0f);
            Add(into, MonsterRole.Shield, 0.0f, 25f, 0.4f);
            Add(into, MonsterRole.Shield, 0.65f, 24f, 0.4f);
            Add(into, MonsterRole.Runner, -0.65f, 36f, 0.6f);
            Add(into, MonsterRole.Runner, 0.65f, 36f, 0.5f);
            Add(into, MonsterRole.Raider, -0.35f, 29f, 0.6f);
            Add(into, MonsterRole.Raider, 0.35f, 29f, 0.55f);
            Add(into, MonsterRole.Shield, 0.0f, 27f, 0.7f);
            Add(into, MonsterRole.Runner, 0.0f, 37f, 0.8f);
            Add(into, MonsterRole.Raider, 0.0f, 30f, 0.7f);
        }

        // Not yet authored — a gently scaling horde so the loop can extend past chapter two.
        static void BuildEndless(int raid, List<SpawnOrder> into)
        {
            int extra = raid - 6;
            int raiders = 5 + extra / 4;
            int runners = 2 + extra / 4;
            int shields = 2 + extra / 5;

            Add(into, MonsterRole.Ogre, 0.0f, 34f, 1.0f);
            for (int i = 0; i < shields; i++)
                Add(into, MonsterRole.Shield, i % 2 == 0 ? -0.55f : 0.55f, 24f + (i % 3) * 2f, i == 0 ? 1.2f : 0.9f);
            for (int i = 0; i < raiders; i++)
                Add(into, MonsterRole.Raider, ((i % 5) - 2) * 0.3f, 26f + (i % 4) * 2f, 0.7f);
            for (int i = 0; i < runners; i++)
                Add(into, MonsterRole.Runner, i % 2 == 0 ? -0.45f : 0.45f, 34f, 1.0f);
        }

        /// <summary>Count the roles in an authored raid (for the supply screen summary).</summary>
        public static void Tally(int raid, out int raiders, out int runners, out int shields, out int ogres)
        {
            var list = new List<SpawnOrder>();
            Build(raid, list);
            raiders = runners = shields = ogres = 0;
            foreach (var order in list)
            {
                switch (order.Role)
                {
                    case MonsterRole.Runner: runners++; break;
                    case MonsterRole.Shield: shields++; break;
                    case MonsterRole.Ogre: ogres++; break;
                    default: raiders++; break;
                }
            }
        }
    }
}
