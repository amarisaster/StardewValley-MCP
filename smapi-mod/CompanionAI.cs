using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewMCPBridge
{
    public enum CompanionMode
    {
        Follow,         // Follow the player around
        Farm,           // Autonomous farming (water, harvest, clear)
        Mine,           // Go to mines, fight, break rocks
        Fish,           // Find water, fish
        Idle            // Stay put
    }

    /// <summary>
    /// AI behavior system for autonomous companion play.
    /// Decides what to do each tick based on current mode and environment.
    /// </summary>
    public class CompanionAI
    {
        public readonly CompanionFarmer Companion;
        private readonly NPC npc;
        private readonly IMonitor monitor;

        public CompanionMode Mode { get; set; } = CompanionMode.Follow;

        private int actionCooldown = 0;
        private int pathCooldown = 0;
        private Vector2? currentTarget = null;
        private bool isFishing = false;

        public CompanionAI(CompanionFarmer companionFarmer, IMonitor monitor)
        {
            this.Companion = companionFarmer;
            this.npc = this.Companion.Visual;
            this.monitor = monitor;
        }

        /// <summary>Called every tick. Decides and executes behavior based on mode.</summary>
        public void Tick()
        {
            if (!Context.IsWorldReady) return;
            if (this.actionCooldown > 0) { this.actionCooldown--; return; }

            switch (this.Mode)
            {
                case CompanionMode.Follow:
                    this.DoFollow();
                    break;
                case CompanionMode.Farm:
                    this.DoFarm();
                    break;
                case CompanionMode.Mine:
                    this.DoMine();
                    break;
                case CompanionMode.Fish:
                    this.DoFish();
                    break;
                case CompanionMode.Idle:
                    break;
            }
        }

        // ====================
        // FOLLOW MODE
        // ====================

        private void DoFollow()
        {
            this.WarpToPlayerIfNeeded();

            var playerPos = Game1.player.Tile;
            var botPos = this.npc.Tile;
            float distance = Vector2.Distance(playerPos, botPos);

            if (distance > 3 && this.pathCooldown <= 0)
            {
                try
                {
                    var offset = this.Companion.Name == "Companion2" ? 1 : -1;
                    this.npc.controller = new PathFindController(
                        this.npc, Game1.currentLocation,
                        new Point((int)playerPos.X + offset, (int)playerPos.Y), 2);
                    this.pathCooldown = 30;
                }
                catch { }
            }

            if (this.pathCooldown > 0) this.pathCooldown--;

            // In combat areas, fight while following
            if (this.IsInCombatArea())
                this.Companion.AttackNearbyMonsters();
        }

        // ====================
        // FARM MODE
        // ====================

        private void DoFarm()
        {
            this.WarpToPlayerIfNeeded();
            var location = this.npc.currentLocation ?? Game1.player.currentLocation;

            // If we have a target, walk to it
            if (this.currentTarget.HasValue)
            {
                float dist = Vector2.Distance(this.npc.Tile, this.currentTarget.Value);
                if (dist <= 1.5f)
                {
                    // Execute the task at this tile
                    this.ExecuteFarmAction(location, this.currentTarget.Value);
                    this.currentTarget = null;
                    this.actionCooldown = 15; // brief pause between actions
                }
                return;
            }

            // Find next task
            var tasks = CompanionActions.ScanForTasks(location, this.monitor);
            if (tasks.Count == 0) return;

            // Pick nearest task
            var myTile = this.npc.Tile;
            var nearest = tasks.OrderBy(t => Vector2.Distance(myTile, t.Tile)).First();
            this.currentTarget = nearest.Tile;

            // Path to it
            try
            {
                this.npc.controller = new PathFindController(
                    this.npc, location,
                    new Point((int)nearest.Tile.X, (int)nearest.Tile.Y), 2);
            }
            catch
            {
                // If pathfinding fails, teleport near
                this.npc.Position = nearest.Tile * 64f;
            }
        }

        private void ExecuteFarmAction(GameLocation location, Vector2 tile)
        {
            // Try harvest first (highest value), then water, then clear
            if (location.terrainFeatures.TryGetValue(tile, out var feature) && feature is HoeDirt dirt)
            {
                if (dirt.crop != null && dirt.readyForHarvest())
                {
                    CompanionActions.HarvestTile(location, tile, this.monitor);
                    return;
                }
                if (dirt.crop != null && dirt.state.Value != 1)
                {
                    // Use shadow farmer's watering can for proper mechanics
                    this.Companion.UseToolAt(tile, typeof(WateringCan));
                    return;
                }
            }

            CompanionActions.ClearDebris(location, tile, this.monitor);
        }

        // ====================
        // MINE MODE
        // ====================

        private void DoMine()
        {
            this.Companion.SyncFromNpc();
            var location = this.npc.currentLocation ?? Game1.player.currentLocation;

            // Priority 1: Fight nearby monsters
            var monster = this.Companion.FindNearestMonster(192f);
            if (monster != null)
            {
                // Move toward monster
                Vector2 monsterTile = monster.Tile;
                float dist = Vector2.Distance(this.npc.Tile, monsterTile);

                if (dist <= 2f)
                {
                    this.Companion.AttackNearbyMonsters();
                    this.actionCooldown = 10;
                }
                else
                {
                    this.PathTo(new Point((int)monsterTile.X, (int)monsterTile.Y));
                }
                return;
            }

            // Priority 2: Break rocks
            var rock = this.Companion.FindNearestRock();
            if (rock.HasValue)
            {
                float dist = Vector2.Distance(this.npc.Tile, rock.Value);
                if (dist <= 1.5f)
                {
                    this.Companion.MineRock(rock.Value);
                    this.actionCooldown = 20;
                }
                else
                {
                    this.PathTo(new Point((int)rock.Value.X, (int)rock.Value.Y));
                }
                return;
            }

            // Priority 3: Find ladder/shaft to go deeper
            // Look for ladder tiles
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value.Name != null &&
                    (pair.Value.Name.Contains("Ladder") || pair.Value.Name.Contains("Shaft")))
                {
                    float dist = Vector2.Distance(this.npc.Tile, pair.Key);
                    if (dist <= 1.5f)
                    {
                        // Touch the ladder to go down
                        this.monitor.Log($"{this.Companion.Name}: Found ladder at ({pair.Key.X},{pair.Key.Y})", LogLevel.Info);
                    }
                    else
                    {
                        this.PathTo(new Point((int)pair.Key.X, (int)pair.Key.Y));
                    }
                    return;
                }
            }

            // Nothing to do — follow player
            this.DoFollow();
        }

        // ====================
        // FISH MODE
        // ====================

        private void DoFish()
        {
            this.WarpToPlayerIfNeeded();

            // If currently fishing, check for nibble
            if (this.isFishing)
            {
                if (this.Companion.CheckAndHookFish())
                {
                    this.isFishing = false;
                    this.actionCooldown = 60; // wait a bit after catching
                }
                return;
            }

            // Find a water tile nearby
            var waterTile = this.FindNearestWaterTile();
            if (waterTile.HasValue)
            {
                float dist = Vector2.Distance(this.npc.Tile, waterTile.Value);
                if (dist <= 2f)
                {
                    // Face the water and cast
                    if (this.Companion.CastFishingRod())
                    {
                        this.isFishing = true;
                        this.actionCooldown = 30;
                    }
                }
                else
                {
                    // Walk to the water
                    this.PathTo(new Point((int)waterTile.Value.X, (int)waterTile.Value.Y));
                }
            }
        }

        private Vector2? FindNearestWaterTile()
        {
            var location = this.npc.currentLocation ?? Game1.player.currentLocation;
            var myTile = this.npc.Tile;
            float nearestDist = float.MaxValue;
            Vector2? nearest = null;

            // Search in a reasonable radius
            int searchRadius = 20;
            for (int x = (int)myTile.X - searchRadius; x <= (int)myTile.X + searchRadius; x++)
            {
                for (int y = (int)myTile.Y - searchRadius; y <= (int)myTile.Y + searchRadius; y++)
                {
                    if (location.isWaterTile(x, y))
                    {
                        var tile = new Vector2(x, y);
                        float dist = Vector2.Distance(myTile, tile);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearest = tile;
                        }
                    }
                }
            }
            return nearest;
        }

        // ====================
        // HELPERS
        // ====================

        private void WarpToPlayerIfNeeded()
        {
            var playerLocation = Game1.player.currentLocation;
            if (playerLocation == null) return;

            if (this.npc.currentLocation?.Name != playerLocation.Name)
            {
                this.npc.currentLocation?.characters.Remove(this.npc);
                this.npc.controller = null;
                this.currentTarget = null;

                var offset = this.Companion.Name == "Companion2" ? new Vector2(64, 0) : new Vector2(-64, 0);
                this.npc.Position = Game1.player.Position + offset;
                this.npc.currentLocation = playerLocation;
                playerLocation.addCharacter(this.npc);

                this.Companion.SyncFromNpc();
                this.monitor.Log($"Warped {this.Companion.Name} to {playerLocation.Name}", LogLevel.Info);
            }
        }

        private void PathTo(Point target)
        {
            if (this.pathCooldown > 0) { this.pathCooldown--; return; }

            try
            {
                var location = this.npc.currentLocation ?? Game1.currentLocation;
                this.npc.controller = new PathFindController(this.npc, location, target, 2);
                this.pathCooldown = 15;
            }
            catch { }
        }

        private bool IsInCombatArea()
        {
            var loc = this.npc.currentLocation;
            if (loc == null) return false;
            return loc is MineShaft || loc.Name == "VolcanoDungeon"
                || loc.characters.Any(c => c is Monster);
        }

        public string GetStatusDescription()
        {
            string mode = this.Mode.ToString().ToLower();
            string task = this.currentTarget.HasValue
                ? $"heading to ({this.currentTarget.Value.X},{this.currentTarget.Value.Y})"
                : this.isFishing ? "fishing" : "scanning";
            float stamina = this.Companion.GetStaminaPercent();
            return $"{mode}: {task} (stamina: {stamina:F0}%)";
        }
    }
}
