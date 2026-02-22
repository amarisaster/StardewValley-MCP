using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace StardewMCPBridge
{
    public class BotManager
    {
        private readonly IMonitor monitor;
        private readonly IModHelper helper;
        private readonly Dictionary<string, CompanionAI> companions = new Dictionary<string, CompanionAI>();

        public BotManager(IMonitor monitor, IModHelper helper)
        {
            this.monitor = monitor;
            this.helper = helper;
        }

        public void SpawnBot(string name, string type)
        {
            if (!Context.IsWorldReady) return;
            if (this.companions.ContainsKey(name)) return;

            this.monitor.Log($"Spawning {name} with shadow farmer", LogLevel.Info);

            try
            {
                var portrait = this.helper.ModContent.Load<Microsoft.Xna.Framework.Graphics.Texture2D>($"assets/{name}_portrait.png");

                var spawnPos = Game1.player.Position;
                if (name == "Companion2") spawnPos += new Vector2(64, 0);
                else if (name == "Companion1") spawnPos += new Vector2(-64, 0);

                NPC botNpc = new NPC(
                    new AnimatedSprite($"Characters\\{name}", 0, 16, 32),
                    spawnPos,
                    Game1.player.currentLocation.Name,
                    2, name, portrait, false
                );
                botNpc.displayName = name;
                Game1.player.currentLocation.addCharacter(botNpc);

                var companionFarmer = new CompanionFarmer(botNpc, name, this.monitor, this.helper);
                var ai = new CompanionAI(companionFarmer, this.monitor);
                ai.Mode = CompanionMode.Follow;

                this.companions.Add(name, ai);
                this.monitor.Log($"{name} spawned with shadow farmer at {Game1.player.currentLocation.Name}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Failed to spawn {name}: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        public void Update()
        {
            foreach (var ai in this.companions.Values)
                ai.Tick();
        }

        public object GetBotStatus()
        {
            var statuses = new List<object>();
            foreach (var kvp in this.companions)
            {
                var ai = kvp.Value;
                var pos = ai.Companion.Visual.Position;
                statuses.Add(new
                {
                    name = kvp.Key,
                    position = new { x = pos.X, y = pos.Y },
                    status = ai.GetStatusDescription(),
                    mode = ai.Mode.ToString().ToLower(),
                    stamina = ai.Companion.GetStaminaPercent()
                });
            }
            return statuses;
        }

        public void ProcessAction(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("actionType", out var actionType)) return;

            switch (actionType.GetString())
            {
                case "spawn":
                    this.SpawnBot("Companion1", "Guard");
                    this.SpawnBot("Companion2", "Anchor");
                    this.SetAllMode(CompanionMode.Follow);
                    this.monitor.Log("Spawned and following", LogLevel.Info);
                    break;

                case "follow":
                    this.SetAllMode(CompanionMode.Follow);
                    break;

                case "stay":
                    this.SetAllMode(CompanionMode.Idle);
                    break;

                case "farm":
                    this.SetAllMode(CompanionMode.Farm);
                    break;

                case "mine":
                    this.SetAllMode(CompanionMode.Mine);
                    break;

                case "fish":
                    this.SetAllMode(CompanionMode.Fish);
                    break;

                case "water_all":
                    this.WaterAll();
                    break;

                case "harvest_all":
                    this.HarvestAll();
                    break;

                // Set individual companion mode
                case "set_mode":
                    if (root.TryGetProperty("target", out var target) &&
                        root.TryGetProperty("mode", out var modeStr))
                    {
                        string targetName = target.GetString();
                        if (this.companions.TryGetValue(targetName, out var ai) &&
                            Enum.TryParse<CompanionMode>(modeStr.GetString(), true, out var mode))
                        {
                            ai.Mode = mode;
                            this.monitor.Log($"Set {targetName} to {mode}", LogLevel.Info);
                        }
                    }
                    break;

                // Warp companions to a specific location
                case "warp":
                    if (root.TryGetProperty("location", out var loc) &&
                        root.TryGetProperty("x", out var wx) &&
                        root.TryGetProperty("y", out var wy))
                    {
                        foreach (var kvp in this.companions)
                            kvp.Value.Companion.WarpTo(loc.GetString(), wx.GetInt32(), wy.GetInt32());
                    }
                    break;
            }
        }

        private void SetAllMode(CompanionMode mode)
        {
            foreach (var ai in this.companions.Values)
                ai.Mode = mode;
            this.monitor.Log($"All companions set to {mode}", LogLevel.Info);
        }

        private void WaterAll()
        {
            var location = Game1.player.currentLocation;
            if (location == null) return;
            int count = 0;
            foreach (var pair in location.terrainFeatures.Pairs)
            {
                if (pair.Value is StardewValley.TerrainFeatures.HoeDirt dirt
                    && dirt.crop != null && dirt.state.Value != 1)
                {
                    if (CompanionActions.WaterTile(location, pair.Key, this.monitor))
                        count++;
                }
            }
            this.monitor.Log($"Watered {count} tiles", LogLevel.Info);
        }

        private void HarvestAll()
        {
            var location = Game1.player.currentLocation;
            if (location == null) return;
            int count = 0;
            foreach (var pair in location.terrainFeatures.Pairs.ToList())
            {
                if (pair.Value is StardewValley.TerrainFeatures.HoeDirt dirt
                    && dirt.crop != null && dirt.readyForHarvest())
                {
                    if (CompanionActions.HarvestTile(location, pair.Key, this.monitor))
                        count++;
                }
            }
            this.monitor.Log($"Harvested {count} crops", LogLevel.Info);
        }
    }
}
