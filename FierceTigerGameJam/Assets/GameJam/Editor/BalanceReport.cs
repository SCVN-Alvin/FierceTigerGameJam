#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GameJam.Config;
using GameJam.Gameplay.Combat;
using GameJam.Gameplay.Wall;
using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Prints, for every map the game ships, how many shots it would take to reach that map's
    /// clear requirement on damage alone, against the ammunition budget the map allows.
    ///
    /// This is the balance pass's acceptance gate and the thing to re-run after any map, bullet
    /// or progression edit. It reads the live assets - block hit points off the block prefabs,
    /// damage off the bullet definitions, budget and requirement off the progression config,
    /// composition out of the map JSON - so it cannot drift from what the game actually loads.
    /// Nothing is written, which is what makes it idempotent: running it twice logs the same
    /// report and leaves the project untouched.
    ///
    /// The accounting is deliberately the worst case a player could face:
    ///
    /// - one shot damages one block, cheapest blocks first (ceil(hit points / damage) each);
    /// - no credit for toppling, for collapse, or for blocks breaking on landing;
    /// - splash is credited only against glass, and only where a level's splash actually clears
    ///   glass's hit point, as one extra pane per shot;
    /// - a material the ammunition cannot hurt is unreachable, not slow.
    ///
    /// Read the result as a ceiling on difficulty rather than a prediction. What the model leaves
    /// out is not a rounding error - see <see cref="BlocksPerShotNote"/>.
    /// </summary>
    public static class BalanceReport
    {
        /// <summary>
        /// Over this share of the budget is flagged. The head-room stands in for everything the
        /// model refuses to credit.
        /// </summary>
        private const float BudgetWarnShare = 0.7f;

        /// <summary>
        /// The gap the report cannot close, stated once so nobody reads a red row as a bug.
        ///
        /// Clearing is measured in blocks, not hit points: LevelProgressTracker
        /// counts what is still standing under the generated root and divides by what was
        /// placed. A shot damages one block directly, so damage-only accounting can never remove
        /// more than about one block per shot, however hard the ammunition hits. Every map here
        /// asks for a good deal more than one block per shot, which means the shortfall is made
        /// up entirely by collapse - blocks knocked off their supports, and blocks breaking on
        /// landing through BreakableBlock's impact damage. That is real and it is most of the
        /// game; it is simply not something a static table can count.
        /// </summary>
        private const string BlocksPerShotNote =
            "blocks/shot is what the budget demands on average; anything over 1.0 has to come "
            + "from collapse, which this model gives no credit for.";

        /// <summary>
        /// Which ammunition each map is designed to be played with, and at what level. Editor-only
        /// tuning data, so it lives with the tool that checks it rather than in a shipped config:
        /// it describes the intended ladder, and the game never reads it.
        /// </summary>
        private sealed class Expectation
        {
            public string mapId;
            public string gateBulletId;
            public int gateLevel;
            public int rockLevel;
            public int cannonLevel;
        }

        private const string RockId = "rock_type";
        private const string CannonId = "cannon_type";

        private static readonly Expectation[] Ladder =
        {
            new Expectation { mapId = "tutorial",      gateBulletId = RockId,   gateLevel = 1, rockLevel = 1, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map1", gateBulletId = RockId,   gateLevel = 1, rockLevel = 1, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map2", gateBulletId = RockId,   gateLevel = 1, rockLevel = 1, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map3", gateBulletId = RockId,   gateLevel = 1, rockLevel = 1, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map4", gateBulletId = RockId,   gateLevel = 2, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map5", gateBulletId = RockId,   gateLevel = 2, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map6", gateBulletId = RockId,   gateLevel = 2, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map7", gateBulletId = CannonId, gateLevel = 1, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission1_map8", gateBulletId = CannonId, gateLevel = 2, rockLevel = 2, cannonLevel = 2 },
            new Expectation { mapId = "mission1_map9", gateBulletId = CannonId, gateLevel = 1, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission2_map1", gateBulletId = RockId,   gateLevel = 2, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission2_map2", gateBulletId = RockId,   gateLevel = 2, rockLevel = 2, cannonLevel = 1 },
            new Expectation { mapId = "mission2_map3", gateBulletId = RockId,   gateLevel = 2, rockLevel = 2, cannonLevel = 1 },
        };

        /// <summary>What one block type costs to remove with a given loadout.</summary>
        private struct BlockCost
        {
            public string type;
            public int count;
            public int shotsEach;
            public bool splashChains;
        }

        /// <summary>Hit points and material for one block type, read off its prefab.</summary>
        private struct BlockStats
        {
            public string materialId;
            public float maxHitPoints;
        }

        [MenuItem("Tools/Smashdown/Balance Report")]
        public static void Report()
        {
            MapConfig maps = LoadFirst<MapConfig>();
            MapProgressionConfig progression = LoadFirst<MapProgressionConfig>();
            BlockDatabase blocks = LoadFirst<BlockDatabase>();

            if (maps == null || progression == null || blocks == null)
            {
                Debug.LogError(
                    "Balance Report needs a MapConfig, a MapProgressionConfig and a BlockDatabase "
                    + "in the project. One of them is missing.");
                return;
            }

            Dictionary<string, BlockStats> stats = ReadBlockStats(blocks);
            if (stats.Count == 0)
            {
                Debug.LogError("Balance Report found no block prefabs carrying a BreakableBlock.");
                return;
            }

            BulletDefinition rock = FindBullet(RockId);
            BulletDefinition cannon = FindBullet(CannonId);
            if (rock == null || cannon == null)
            {
                Debug.LogError($"Balance Report needs bullet definitions with ids \"{RockId}\" and \"{CannonId}\".");
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("Balance Report - worst-case shots to the clear requirement, damage only.");
            report.AppendLine(AppendBlockLegend(stats));
            report.AppendLine(AppendDamageLegend(rock, cannon, stats));
            report.AppendLine();
            report.AppendLine(
                "map            blocks  need  budget  cap  blk/shot | Rock         Cannon       gate");
            report.AppendLine(new string('-', 96));

            int redRows = 0;
            int rows = 0;

            foreach (MapInfo map in maps.Maps)
            {
                if (map == null || map.MapJson == null)
                {
                    continue;
                }

                if (!KnockdownMapDefinition.TryParse(map.MapJson.text, out KnockdownMapDefinition definition, out string error))
                {
                    report.AppendLine($"{map.Id,-14} unreadable JSON: {error}");
                    continue;
                }

                if (!progression.TryGetMapRules(map.Id, out MapProgressionConfig.Entry rules))
                {
                    report.AppendLine($"{map.Id,-14} no MapProgressionConfig row, so there is nothing to check it against.");
                    continue;
                }

                Dictionary<string, int> composition = CountBlocks(definition);
                int total = composition.Values.Sum();
                if (total == 0)
                {
                    continue;
                }

                // Ceiling, matching the run's own test: the tracker compares a fraction of the
                // count against the requirement, so the pass happens on the block that tips it
                // over, not on the one that reaches it exactly.
                int need = Mathf.CeilToInt(rules.requiredClearPercent * total);
                int budget = rules.bulletPickLimit;
                float cap = budget * BudgetWarnShare;

                Expectation expectation = Ladder.FirstOrDefault(e => string.Equals(e.mapId, map.Id, StringComparison.Ordinal));

                int rockLevel = expectation?.rockLevel ?? 1;
                int cannonLevel = expectation?.cannonLevel ?? 1;

                int rockShots = ShotsToRequirement(composition, stats, rock, rockLevel, need);
                int cannonShots = ShotsToRequirement(composition, stats, cannon, cannonLevel, need);

                bool gateIsRock = expectation == null || string.Equals(expectation.gateBulletId, RockId, StringComparison.Ordinal);
                int gateShots = gateIsRock ? rockShots : cannonShots;
                string gateName = expectation == null
                    ? "(unladdered)"
                    : $"{(gateIsRock ? rock : cannon).DisplayName} {Roman(expectation.gateLevel)}";

                string verdict = Verdict(gateShots, cap, budget);
                if (!verdict.StartsWith("GREEN", StringComparison.Ordinal))
                {
                    redRows++;
                }

                rows++;

                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-14} {1,6} {2,5} {3,7} {4,4:0.0} {5,9:0.00} | {6,-12} {7,-12} {8} {9}",
                    map.Id,
                    total,
                    need,
                    budget,
                    cap,
                    need / (float)Mathf.Max(1, budget),
                    Cell(rockShots, cap, budget, rockLevel, "Rock"),
                    Cell(cannonShots, cap, budget, cannonLevel, "Cannon"),
                    gateName,
                    verdict));

                report.AppendLine($"{string.Empty,15}{Composition(composition, stats)}");

                string gateNote = GearCheckNote(composition, stats, rock, cannon, need, total);
                if (!string.IsNullOrEmpty(gateNote))
                {
                    report.AppendLine($"{string.Empty,15}{gateNote}");
                }
            }

            report.AppendLine(new string('-', 96));
            report.AppendLine($"{rows} maps checked, {redRows} over {BudgetWarnShare:P0} of budget on the gate loadout.");
            report.AppendLine(BlocksPerShotNote);
            report.AppendLine(PlaytestExpectation());
            report.AppendLine(EconomyCheck());

            if (redRows > 0)
            {
                Debug.LogWarning(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }

        /// <summary>
        /// Shots to bring <paramref name="need"/> blocks down with one ammunition at one level,
        /// spending on the cheapest blocks first. Returns <see cref="int.MaxValue"/> when the
        /// requirement cannot be met at all, which is what a material this ammunition does no
        /// damage to produces - the honest answer there is "never", not a large number.
        /// </summary>
        private static int ShotsToRequirement(
            Dictionary<string, int> composition,
            Dictionary<string, BlockStats> stats,
            BulletDefinition bullet,
            int level,
            int need)
        {
            List<BlockCost> costs = new List<BlockCost>();
            float splashShare = bullet.GetLevel(level)?.splashShare ?? 0f;

            foreach (KeyValuePair<string, int> entry in composition)
            {
                if (!stats.TryGetValue(entry.Key, out BlockStats block))
                {
                    continue;
                }

                if (!bullet.TryGetDamage(level, block.materialId, out BulletDefinition.MaterialDamage damage)
                    || damage.blockDamage <= 0f)
                {
                    // Not reachable with this ammunition at all, so it is not a cheap option, it
                    // is no option. Left out of the list rather than priced high.
                    continue;
                }

                costs.Add(new BlockCost
                {
                    type = entry.Key,
                    count = entry.Value,
                    shotsEach = Mathf.CeilToInt(block.maxHitPoints / damage.blockDamage),

                    // Splash is credited only where it actually finishes the material in one
                    // go, and glass is the only material whose hit points are low enough for
                    // that to be true of any level here.
                    splashChains = IsGlass(block.materialId)
                                   && damage.blockDamage * splashShare >= block.maxHitPoints,
                });
            }

            // Cheapest first, on what a block actually costs rather than on its shot count: a
            // pane that dies to a neighbour's splash costs half a shot, which can undercut a
            // brick that costs a whole one. Sorting on the raw count would spend the budget on
            // brick while cheaper glass stood, and report a total nobody would ever pay.
            costs.Sort((a, b) => EffectiveCost(a).CompareTo(EffectiveCost(b)));

            int shots = 0;
            int removed = 0;
            foreach (BlockCost cost in costs)
            {
                if (removed >= need)
                {
                    break;
                }

                int take = Mathf.Min(cost.count, need - removed);

                // A chaining shot takes its target and one neighbour, so a run of glass costs
                // half as many shots as it has panes. One neighbour rather than a blast-radius
                // count: the radius reaches further, but how many panes sit inside it is a
                // property of the map's shape, and guessing it would be the sort of optimism
                // this report exists to avoid.
                shots += cost.splashChains
                    ? Mathf.CeilToInt(take / 2f) * cost.shotsEach
                    : take * cost.shotsEach;
                removed += take;
            }

            return removed >= need ? shots : int.MaxValue;
        }

        /// <summary>
        /// Whether a map that contains Rock-proof material actually gates on it.
        ///
        /// Owning the concrete-capable ammunition is meant to be the price of entry to the late
        /// campaign, but the requirement is a share of the block count and concrete is only a
        /// share of the blocks. Where the rest of the structure is by itself enough to reach the
        /// requirement, a player can pass on Rock alone and never touch a concrete block, and the
        /// gear check is decorative. That is a design question rather than a bug, so it is
        /// reported with the number that decides it - the share the requirement would have to
        /// exceed for the gate to bite - and nothing here changes it.
        /// </summary>
        private static string GearCheckNote(
            Dictionary<string, int> composition,
            Dictionary<string, BlockStats> stats,
            BulletDefinition rock,
            BulletDefinition cannon,
            int need,
            int total)
        {
            int bestRockLevel = Mathf.Max(1, rock.LevelCount);
            int rockProof = 0;
            foreach (KeyValuePair<string, int> entry in composition)
            {
                if (!stats.TryGetValue(entry.Key, out BlockStats block))
                {
                    continue;
                }

                bool rockCan = rock.CanDamage(bestRockLevel, block.materialId);
                if (!rockCan && cannon.CanDamage(Mathf.Max(1, cannon.LevelCount), block.materialId))
                {
                    rockProof += entry.Value;
                }
            }

            if (rockProof == 0)
            {
                return string.Empty;
            }

            int reachable = total - rockProof;
            float gateShare = reachable / (float)total;
            return reachable >= need
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "GEAR CHECK DOES NOT BITE: {0} blocks here need the cannon, but the other {1} "
                    + "already cover the {2} the requirement asks for. Rock alone passes without "
                    + "touching them; the requirement would have to exceed {3:P0} to gate.",
                    rockProof, reachable, need, gateShare)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Gear check bites: only {0} of {1} blocks are reachable without the cannon, "
                    + "short of the {2} required.",
                    reachable, total, need);
        }

        /// <summary>
        /// Shots per block once the splash chain is priced in, used only for ordering. Kept
        /// separate from the running total below, which still counts whole shots.
        /// </summary>
        private static float EffectiveCost(BlockCost cost)
        {
            return cost.splashChains ? cost.shotsEach / 2f : cost.shotsEach;
        }

        private static Dictionary<string, int> CountBlocks(KnockdownMapDefinition definition)
        {
            Dictionary<string, int> composition = new Dictionary<string, int>(StringComparer.Ordinal);
            if (definition?.layers == null)
            {
                return composition;
            }

            foreach (KnockdownMapLayer layer in definition.layers)
            {
                if (layer?.blocks == null)
                {
                    continue;
                }

                foreach (KnockdownMapBlock block in layer.blocks)
                {
                    if (block == null || string.IsNullOrEmpty(block.type))
                    {
                        continue;
                    }

                    composition.TryGetValue(block.type, out int count);
                    composition[block.type] = count + 1;
                }
            }

            return composition;
        }

        /// <summary>
        /// Hit points per block type, taken from the prefab the block database actually spawns
        /// rather than from a table here. A number typed into this tool would be a second place
        /// for hit points to live, and the first thing to go stale when a prefab is retuned.
        /// </summary>
        private static Dictionary<string, BlockStats> ReadBlockStats(BlockDatabase blocks)
        {
            Dictionary<string, BlockStats> stats = new Dictionary<string, BlockStats>(StringComparer.Ordinal);
            foreach (BlockDatabase.Entry entry in blocks.Entries)
            {
                if (string.IsNullOrEmpty(entry.type) || entry.prefab == null)
                {
                    continue;
                }

                if (!entry.prefab.TryGetComponent(out BreakableBlock breakable))
                {
                    continue;
                }

                stats[entry.type] = new BlockStats
                {
                    materialId = breakable.MaterialId,
                    maxHitPoints = breakable.MaxHitPoints,
                };
            }

            return stats;
        }

        private static string Cell(int shots, float cap, int budget, int level, string label)
        {
            if (shots == int.MaxValue)
            {
                return $"{label} {Roman(level)} n/a";
            }

            string mark = shots <= cap ? string.Empty : (shots <= budget ? "!" : "!!");
            return $"{label} {Roman(level)} {shots}{mark}";
        }

        private static string Verdict(int shots, float cap, int budget)
        {
            if (shots == int.MaxValue)
            {
                return "BLOCKED (cannot reach the requirement with this ammunition)";
            }

            if (shots <= cap)
            {
                return "GREEN";
            }

            return shots <= budget ? "RED (over 70% of budget)" : "RED (over budget)";
        }

        private static string Composition(Dictionary<string, int> composition, Dictionary<string, BlockStats> stats)
        {
            return string.Join(
                "  ",
                composition
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => stats.TryGetValue(pair.Key, out BlockStats block)
                        ? $"{pair.Key} x{pair.Value} (hp {block.maxHitPoints:0.#})"
                        : $"{pair.Key} x{pair.Value} (unknown block)"));
        }

        private static string AppendBlockLegend(Dictionary<string, BlockStats> stats)
        {
            return "Blocks: " + string.Join(
                ", ",
                stats
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key} = {pair.Value.materialId} hp {pair.Value.maxHitPoints:0.#}"));
        }

        private static string AppendDamageLegend(
            BulletDefinition rock,
            BulletDefinition cannon,
            Dictionary<string, BlockStats> stats)
        {
            IEnumerable<string> materials = stats.Values
                .Select(block => block.materialId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal);

            List<string> lines = new List<string>();
            foreach (BulletDefinition bullet in new[] { rock, cannon })
            {
                for (int level = 1; level <= bullet.LevelCount; level++)
                {
                    BulletDefinition.Level bulletLevel = bullet.GetLevel(level);
                    string damage = string.Join(
                        " ",
                        materials.Select(id => bullet.TryGetDamage(level, id, out BulletDefinition.MaterialDamage entry)
                            ? $"{id} {entry.blockDamage:0.#}"
                            : $"{id} -"));
                    lines.Add($"{bulletLevel?.displayName ?? bullet.DisplayName}: {damage} splash x{bulletLevel?.splashShare ?? 0f:0.##}");
                }
            }

            return "Damage: " + string.Join(" | ", lines);
        }

        /// <summary>
        /// What a playtester should see before they play, so a run that disagrees with the table
        /// is recognisable as news rather than noise.
        /// </summary>
        private static string PlaytestExpectation()
        {
            return
                "Before playing: concrete is unreachable with Rock at any level, so map 7 onward "
                + "must fail on a Rock-only loadout - that is the gear check working, not a bug. "
                + "Everywhere else the shot count above is a ceiling nobody should approach: the "
                + "brief asks each of maps 1, 4, 7 and 9 to pass with 30% of the budget unspent, "
                + "and reaching that depends on collapse, so aim at supports low in the structure "
                + "rather than at the blocks the requirement names. Record shots used per map.";
        }

        /// <summary>
        /// Whether the campaign pays for its own gear check. Cumulative pass rewards up to the
        /// last map before concrete, against what the concrete-capable ammunition costs - both
        /// read from the live configs, because the point of the check is to catch a price and a
        /// reward table drifting apart.
        /// </summary>
        private static string EconomyCheck()
        {
            RewardConfig rewards = LoadFirst<RewardConfig>();
            PurchaseBulletConfig purchase = LoadFirst<PurchaseBulletConfig>();
            MapProgressionConfig progression = LoadFirst<MapProgressionConfig>();
            UpgradeBulletConfig upgrades = LoadFirst<UpgradeBulletConfig>();

            if (rewards == null || purchase == null || progression == null)
            {
                return "Economy: skipped, a config is missing.";
            }

            if (!purchase.TryGetPrice(CannonId, out int cannonPrice))
            {
                return "Economy: no purchase price listed for the cannon, so it cannot be bought at all.";
            }

            // Maps 1-6, the run-up to the first concrete map. Pass rewards only: clearing a map
            // 100% is a thing to come back for, not something the ladder may assume.
            int earned = 0;
            List<string> counted = new List<string>();
            for (int i = 1; i <= 6; i++)
            {
                string mapId = $"mission1_map{i}";
                if (progression.TryGetMapRules(mapId, out MapProgressionConfig.Entry rules)
                    && rewards.TryGetReward(rules.passMapRewardId, out int gold))
                {
                    earned += gold;
                    counted.Add($"{gold}");
                }
            }

            int rockUpgrade = 0;
            if (upgrades != null && upgrades.TryGetUpgradePrice(RockId, 2, out int rockPrice))
            {
                rockUpgrade = rockPrice;
            }

            string verdict = earned >= cannonPrice ? "covered" : "SHORT";
            string ladderVerdict = earned >= cannonPrice + rockUpgrade ? "covered" : "SHORT";

            return
                $"Economy: pass rewards for maps 1-6 ({string.Join("+", counted)}) = {earned} gold. "
                + $"Cannon Ball unlock costs {cannonPrice}, so the gear check is {verdict} with "
                + $"{earned - cannonPrice} to spare. The ladder also expects Rock II by map 4 "
                + $"({rockUpgrade} gold); buying both leaves {earned - cannonPrice - rockUpgrade} "
                + $"and is {ladderVerdict}. Clear rewards are on top of this and are not counted.";
        }

        private static bool IsGlass(string materialId)
        {
            return string.Equals(materialId, "glass", StringComparison.OrdinalIgnoreCase);
        }

        private static string Roman(int level)
        {
            switch (level)
            {
                case 1: return "I";
                case 2: return "II";
                case 3: return "III";
                default: return level.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static BulletDefinition FindBullet(string id)
        {
            return AssetDatabase.FindAssets($"t:{nameof(BulletDefinition)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<BulletDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(bullet => bullet != null && string.Equals(bullet.Id, id, StringComparison.Ordinal));
        }

        private static T LoadFirst<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(asset => asset != null);
        }
    }
}
#endif
