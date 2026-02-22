using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace StardewMCPBridge
{
    /// <summary>
    /// Direct game-object manipulation for farm actions.
    /// No Farmer instance needed — we interact with tiles directly.
    /// </summary>
    public static class CompanionActions
    {
        /// <summary>Water a crop at the given tile.</summary>
        public static bool WaterTile(GameLocation location, Vector2 tile, IMonitor monitor)
        {
            if (location.terrainFeatures.TryGetValue(tile, out var feature) && feature is HoeDirt dirt)
            {
                if (dirt.state.Value != 1) // not already watered
                {
                    dirt.state.Value = 1;
                    location.temporarySprites.Add(new TemporaryAnimatedSprite(
                        "TileSheets\\animations", new Rectangle(0, 0, 64, 64),
                        50f, 9, 1, tile * 64f, false, false, 0.01f, 0.01f,
                        Color.White, 1f, 0f, 0f, 0f
                    ));
                    monitor.Log($"Watered tile at ({tile.X}, {tile.Y})", LogLevel.Trace);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Harvest a ready crop at the given tile.</summary>
        public static bool HarvestTile(GameLocation location, Vector2 tile, IMonitor monitor)
        {
            if (location.terrainFeatures.TryGetValue(tile, out var feature) && feature is HoeDirt dirt)
            {
                if (dirt.crop != null && dirt.readyForHarvest())
                {
                    bool success = dirt.crop.harvest((int)tile.X, (int)tile.Y, dirt, null);
                    if (success)
                    {
                        monitor.Log($"Harvested crop at ({tile.X}, {tile.Y})", LogLevel.Trace);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Clear a debris object (stone, weed, twig) at the given tile.</summary>
        public static bool ClearDebris(GameLocation location, Vector2 tile, IMonitor monitor)
        {
            if (location.objects.TryGetValue(tile, out var obj))
            {
                string name = obj.Name ?? "";
                // Stone, Weeds, Twigs
                if (name.Contains("Stone") || name.Contains("Weed") || name.Contains("Twig")
                    || obj.ParentSheetIndex == 294 || obj.ParentSheetIndex == 295
                    || obj.ParentSheetIndex == 343 || obj.ParentSheetIndex == 450)
                {
                    location.objects.Remove(tile);
                    monitor.Log($"Cleared debris at ({tile.X}, {tile.Y}): {name}", LogLevel.Trace);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Hoe the ground at the given tile to create farmable dirt.</summary>
        public static bool HoeTile(GameLocation location, Vector2 tile, IMonitor monitor)
        {
            if (!location.terrainFeatures.ContainsKey(tile)
                && !location.objects.ContainsKey(tile)
                && location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Diggable", "Back") != null)
            {
                location.terrainFeatures.Add(tile, new HoeDirt(0, location));
                monitor.Log($"Hoed tile at ({tile.X}, {tile.Y})", LogLevel.Trace);
                return true;
            }
            return false;
        }

        /// <summary>Scan a location for tiles that need work and return a prioritized task list.</summary>
        public static List<FarmTask> ScanForTasks(GameLocation location, IMonitor monitor)
        {
            var tasks = new List<FarmTask>();

            foreach (var pair in location.terrainFeatures.Pairs)
            {
                if (pair.Value is HoeDirt dirt)
                {
                    // Harvest-ready crops (highest priority)
                    if (dirt.crop != null && dirt.readyForHarvest())
                    {
                        tasks.Add(new FarmTask
                        {
                            Type = FarmTaskType.Harvest,
                            Tile = pair.Key,
                            Priority = 10
                        });
                    }
                    // Unwatered crops
                    else if (dirt.crop != null && dirt.state.Value != 1 && !Game1.isRaining)
                    {
                        tasks.Add(new FarmTask
                        {
                            Type = FarmTaskType.Water,
                            Tile = pair.Key,
                            Priority = 8
                        });
                    }
                }
            }

            // Debris on the farm
            foreach (var pair in location.objects.Pairs)
            {
                var obj = pair.Value;
                string name = obj.Name ?? "";
                if (name.Contains("Stone") || name.Contains("Weed") || name.Contains("Twig")
                    || obj.ParentSheetIndex == 294 || obj.ParentSheetIndex == 295
                    || obj.ParentSheetIndex == 343 || obj.ParentSheetIndex == 450)
                {
                    tasks.Add(new FarmTask
                    {
                        Type = FarmTaskType.ClearDebris,
                        Tile = pair.Key,
                        Priority = 3
                    });
                }
            }

            // Sort by priority descending, then distance to center
            tasks.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return tasks;
        }

        /// <summary>Execute a task at a tile.</summary>
        public static bool ExecuteTask(FarmTask task, GameLocation location, IMonitor monitor)
        {
            switch (task.Type)
            {
                case FarmTaskType.Water:
                    return WaterTile(location, task.Tile, monitor);
                case FarmTaskType.Harvest:
                    return HarvestTile(location, task.Tile, monitor);
                case FarmTaskType.ClearDebris:
                    return ClearDebris(location, task.Tile, monitor);
                case FarmTaskType.Hoe:
                    return HoeTile(location, task.Tile, monitor);
                default:
                    return false;
            }
        }
    }

    public enum FarmTaskType
    {
        Water,
        Harvest,
        Hoe,
        ClearDebris,
        Plant
    }

    public class FarmTask
    {
        public FarmTaskType Type { get; set; }
        public Vector2 Tile { get; set; }
        public int Priority { get; set; }
    }
}
