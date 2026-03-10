using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.Tools;

namespace StardewMCPBridge
{
    public class BotManager
    {
        private readonly IMonitor monitor;
        private readonly IModHelper helper;
        private readonly Dictionary<string, CompanionAI> companions = new Dictionary<string, CompanionAI>();

        /// <summary>Per-companion last command results, written to bridge data.</summary>
        private readonly Dictionary<string, object> commandResults = new Dictionary<string, object>();

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
                // Remove any game-auto-created NPCs with this name from ALL locations
                // (Data/Characters injection causes the game to auto-spawn them)
                foreach (var loc in Game1.locations)
                {
                    var dupes = loc.characters.Where(c => c.Name == name).ToList();
                    foreach (var dupe in dupes)
                    {
                        loc.characters.Remove(dupe);
                        this.monitor.Log($"Removed duplicate {name} from {loc.Name}", LogLevel.Debug);
                    }
                }

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
                this.monitor.Log($"{name} placed at pixel ({spawnPos.X},{spawnPos.Y}), tile ({spawnPos.X / 64f:F1},{spawnPos.Y / 64f:F1})", LogLevel.Info);

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
            foreach (var kvp in this.companions)
            {
                try
                {
                    kvp.Value.Tick();
                }
                catch (Exception ex)
                {
                    this.monitor.Log($"{kvp.Key}: AI tick error (recovering): {ex.Message}", LogLevel.Error);
                }
            }
        }

        /// <summary>Get expanded per-companion status for bridge data (includes surroundings in Player mode).</summary>
        public object GetBotStatus()
        {
            var statuses = new List<object>();
            foreach (var kvp in this.companions)
            {
                var ai = kvp.Value;
                var companion = ai.Companion;
                var pos = companion.Visual.Position;
                var tile = companion.Visual.Tile;

                var status = new Dictionary<string, object>
                {
                    ["name"] = kvp.Key,
                    ["position"] = new { x = pos.X, y = pos.Y },
                    ["tile"] = new { x = (int)tile.X, y = (int)tile.Y },
                    ["location"] = companion.Visual.currentLocation?.Name ?? "unknown",
                    ["status"] = ai.GetStatusDescription(),
                    ["mode"] = ai.Mode.ToString().ToLower(),
                    ["stamina"] = companion.GetStaminaPercent(),
                    ["health"] = companion.Shadow.health,
                    ["maxHealth"] = companion.Shadow.maxHealth,
                    ["autoCombat"] = ai.AutoCombat
                };

                // Include surroundings scan in Player mode (the companion's "eyes")
                if (ai.Mode == CompanionMode.Player)
                {
                    var scan = companion.GetSurroundings(8);
                    if (scan != null)
                    {
                        status["surroundings"] = new
                        {
                            tiles = scan.Tiles.Select(t => new
                            {
                                x = t.X, y = t.Y,
                                passable = t.Passable,
                                water = t.IsWater,
                                terrain = t.Terrain,
                                crop = t.CropName,
                                cropReady = t.CropReady,
                                waterState = t.WaterState,
                                obj = t.ObjectName,
                                objType = t.ObjectType,
                                breakable = t.Breakable,
                                interactable = t.Interactable
                            }),
                            monsters = scan.Monsters.Select(m => new
                            {
                                name = m.Name,
                                x = m.X, y = m.Y,
                                health = m.Health,
                                maxHealth = m.MaxHealth
                            }),
                            npcs = scan.Npcs.Select(n => new
                            {
                                name = n.Name,
                                x = n.X, y = n.Y
                            })
                        };
                    }

                    status["inventory"] = companion.GetInventoryData();

                    // Include last command result if any
                    if (this.commandResults.TryGetValue(kvp.Key, out var result))
                        status["lastCommandResult"] = result;
                }

                statuses.Add(status);
            }
            return statuses;
        }

        // ======================
        // SLEEP SIGNAL
        // ======================

        /// <summary>Signal all bot farmers as sleep-ready. Call when host goes to bed.</summary>
        public void SignalAllSleepReady()
        {
            foreach (var kvp in this.companions)
            {
                kvp.Value.Companion.SignalSleepReady();
                this.monitor.Log($"{kvp.Key}: Signaled sleep ready", LogLevel.Debug);
            }
        }

        // ======================
        // DAY TRANSITION
        // ======================

        /// <summary>Restore companion stamina on new day; reset state.</summary>
        public void OnDayStarted()
        {
            foreach (var kvp in this.companions)
            {
                var ai = kvp.Value;
                var shadow = ai.Companion.Shadow;
                shadow.WakeUp();

                // Reset AI state for fresh day (keep Player mode if set)
                if (ai.Mode != CompanionMode.Player)
                    ai.Mode = CompanionMode.Follow;

                // If companion was in a mine/dungeon, warp to farm
                var loc = ai.Companion.Visual.currentLocation;
                if (loc is StardewValley.Locations.MineShaft || loc?.Name == "VolcanoDungeon")
                {
                    ai.Companion.WarpTo("Farm", 64, 15);
                    this.monitor.Log($"{kvp.Key}: Was in {loc.Name} at day end — warped to Farm", LogLevel.Info);
                }

                this.monitor.Log($"{kvp.Key}: New day — stamina restored, mode: {ai.Mode}", LogLevel.Info);
            }
        }

        /// <summary>Clean up companions on return to title.</summary>
        public void Cleanup()
        {
            foreach (var kvp in this.companions)
            {
                kvp.Value.Companion.Unregister();
                var npc = kvp.Value.Companion.Visual;
                npc.currentLocation?.characters.Remove(npc);
            }
            this.companions.Clear();
            this.commandResults.Clear();
            this.monitor.Log("All companions cleaned up", LogLevel.Info);
        }

        // ======================
        // ACTION PROCESSING
        // ======================

        public void ProcessAction(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("actionType", out var actionType)) return;

            string action = actionType.GetString();

            // Check for companion-targeted commands (Player mode direct control)
            if (root.TryGetProperty("companion", out var companionProp))
            {
                string companionName = companionProp.GetString();
                if (this.companions.TryGetValue(companionName, out var targetAi))
                {
                    this.ProcessCompanionCommand(action, root, companionName, targetAi);
                    return;
                }
                else
                {
                    this.monitor.Log($"Unknown companion: {companionName}", LogLevel.Warn);
                    return;
                }
            }

            // Global commands (existing behavior)
            switch (action)
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

                // Single-tile actions (from stardew_action tool)
                case "water":
                case "harvest":
                case "clear":
                case "hoe":
                    if (root.TryGetProperty("x", out var ax) && root.TryGetProperty("y", out var ay))
                    {
                        var location = Game1.player.currentLocation;
                        if (location != null)
                        {
                            var tile = new Vector2(ax.GetInt32(), ay.GetInt32());
                            bool ok = action switch
                            {
                                "water" => CompanionActions.WaterTile(location, tile, this.monitor),
                                "harvest" => CompanionActions.HarvestTile(location, tile, this.monitor),
                                "clear" => CompanionActions.ClearDebris(location, tile, this.monitor),
                                "hoe" => CompanionActions.HoeTile(location, tile, this.monitor),
                                _ => false
                            };
                            this.monitor.Log($"Action {action} at ({ax.GetInt32()},{ay.GetInt32()}): {(ok ? "success" : "failed")}", LogLevel.Info);
                        }
                    }
                    break;
            }
        }

        // ======================
        // DIRECT COMPANION COMMANDS (Player mode)
        // ======================

        private void ProcessCompanionCommand(string action, System.Text.Json.JsonElement root, string companionName, CompanionAI ai)
        {
            var companion = ai.Companion;
            bool success = false;
            string detail = "";

            try
            {
                switch (action)
                {
                    // --- Movement ---
                    case "move_to":
                    {
                        int tx = root.GetProperty("x").GetInt32();
                        int ty = root.GetProperty("y").GetInt32();
                        var npc = companion.Visual;
                        var location = npc.currentLocation ?? Game1.player.currentLocation;
                        try
                        {
                            npc.controller = new PathFindController(
                                npc, location, new Microsoft.Xna.Framework.Point(tx, ty), 2);
                            companion.SyncFromNpc();
                            success = true;
                            detail = $"Pathing to ({tx},{ty})";
                        }
                        catch
                        {
                            // Pathfinding failed — teleport
                            npc.Position = new Vector2(tx * 64f, ty * 64f);
                            npc.controller = null;
                            companion.SyncFromNpc();
                            success = true;
                            detail = $"Teleported to ({tx},{ty}) (path failed)";
                        }
                        break;
                    }

                    case "warp_to":
                    {
                        string loc = root.GetProperty("location").GetString();
                        int wx = root.GetProperty("x").GetInt32();
                        int wy = root.GetProperty("y").GetInt32();
                        companion.WarpTo(loc, wx, wy);
                        success = true;
                        detail = $"Warped to {loc} ({wx},{wy})";
                        break;
                    }

                    case "face_direction":
                    {
                        int dir = root.GetProperty("direction").GetInt32(); // 0=up,1=right,2=down,3=left
                        if (dir < 0 || dir > 3)
                        {
                            detail = $"Invalid direction: {dir} (must be 0-3)";
                            break;
                        }
                        companion.Visual.FacingDirection = dir;
                        companion.Shadow.FacingDirection = dir;
                        success = true;
                        detail = $"Facing {new[] { "up", "right", "down", "left" }[dir]}";
                        break;
                    }

                    // --- Tools ---
                    case "use_tool":
                    {
                        string toolName = root.GetProperty("tool").GetString();
                        int tx = root.GetProperty("x").GetInt32();
                        int ty = root.GetProperty("y").GetInt32();
                        var tile = new Vector2(tx, ty);

                        Type toolType = toolName.ToLower() switch
                        {
                            "pickaxe" => typeof(Pickaxe),
                            "axe" => typeof(Axe),
                            "hoe" => typeof(Hoe),
                            "wateringcan" or "watering_can" => typeof(WateringCan),
                            "sword" or "weapon" => typeof(MeleeWeapon),
                            _ => null
                        };

                        if (toolType != null)
                        {
                            success = companion.UseToolAt(tile, toolType);
                            detail = success ? $"Used {toolName} at ({tx},{ty})" : $"Failed to use {toolName}";
                        }
                        else
                        {
                            detail = $"Unknown tool: {toolName}";
                        }
                        break;
                    }

                    case "attack":
                    {
                        success = companion.AttackNearbyMonsters();
                        detail = success ? "Attacked nearby monster" : "No monsters in range";
                        break;
                    }

                    // --- Interact ---
                    case "interact":
                    {
                        int tx = root.GetProperty("x").GetInt32();
                        int ty = root.GetProperty("y").GetInt32();
                        var tile = new Vector2(tx, ty);
                        var location = companion.Visual.currentLocation ?? Game1.player.currentLocation;

                        // Try harvest
                        if (location.terrainFeatures.TryGetValue(tile, out var feature)
                            && feature is StardewValley.TerrainFeatures.HoeDirt dirt
                            && dirt.crop != null && dirt.readyForHarvest())
                        {
                            success = CompanionActions.HarvestTile(location, tile, this.monitor);
                            detail = success ? $"Harvested at ({tx},{ty})" : "Harvest failed";
                        }
                        // Try pick up item
                        else if (location.objects.TryGetValue(tile, out var obj))
                        {
                            if (obj.Name != null && (obj.Name.Contains("Ladder") || obj.Name.Contains("Shaft")))
                            {
                                // Descend mine
                                if (location is StardewValley.Locations.MineShaft shaft)
                                {
                                    int nextLevel = shaft.mineLevel + 1;
                                    string nextName = "UndergroundMine" + nextLevel;
                                    var nextLoc = Game1.getLocationFromName(nextName)
                                        ?? StardewValley.Locations.MineShaft.GetMine(nextName);
                                    if (nextLoc != null)
                                    {
                                        companion.WarpTo(nextLoc.Name, 6, 6);
                                        success = true;
                                        detail = $"Descended to mine level {nextLevel}";
                                    }
                                    else
                                    {
                                        detail = $"Can't find mine level {nextLevel}";
                                    }
                                }
                            }
                            else if (obj is StardewValley.Objects.Chest chest)
                            {
                                // Return chest contents as the command result
                                var items = chest.Items.Where(i => i != null).Select(i => new
                                {
                                    name = i.DisplayName ?? i.Name,
                                    stack = i.Stack,
                                    qualifiedId = i.QualifiedItemId
                                }).ToList();
                                success = true;
                                detail = $"Chest at ({tx},{ty}): {items.Count} items";
                                this.commandResults[companionName] = new { action, success, detail, items };
                                this.monitor.Log($"{companionName}: {detail}", LogLevel.Info);
                                return; // early return — custom result
                            }
                            else
                            {
                                // Try to check/interact with the object using companion's shadow farmer
                                success = obj.checkForAction(companion.Shadow);
                                detail = success ? $"Interacted with {obj.DisplayName}" : $"Can't interact with {obj.DisplayName}";
                            }
                        }
                        else
                        {
                            detail = $"Nothing to interact with at ({tx},{ty})";
                        }
                        break;
                    }

                    // --- Fishing ---
                    case "cast_fishing_rod":
                    {
                        success = ai.StartFishing();
                        detail = success ? "Cast fishing rod" : "Failed to cast";
                        break;
                    }

                    // --- Combat toggle ---
                    case "set_auto_combat":
                    {
                        bool enabled = root.GetProperty("enabled").GetBoolean();
                        ai.AutoCombat = enabled;
                        success = true;
                        detail = $"Auto-combat {(enabled ? "enabled" : "disabled")}";
                        break;
                    }

                    // --- Inventory ---
                    case "eat_item":
                    {
                        int slot = root.TryGetProperty("slot", out var slotProp) ? slotProp.GetInt32() : -1;
                        var items = companion.Shadow.Items;
                        StardewValley.Object food = null;

                        if (slot >= 0 && slot < items.Count && items[slot] is StardewValley.Object o1 && o1.Edibility > 0)
                            food = o1;
                        else
                            food = items.FirstOrDefault(i => i is StardewValley.Object o && o.Edibility > 0) as StardewValley.Object;

                        if (food != null)
                        {
                            try
                            {
                                companion.Shadow.eatObject(food);
                                success = true;
                                detail = $"Ate {food.DisplayName}";
                            }
                            catch (Exception eatEx)
                            {
                                // eatObject may access UI elements on headless farmer
                                // Fall back to direct stamina/health restoration
                                companion.Shadow.Stamina = Math.Min(companion.Shadow.MaxStamina, companion.Shadow.Stamina + food.Edibility);
                                companion.Shadow.health = Math.Min(companion.Shadow.maxHealth, companion.Shadow.health + (int)(food.Edibility * 0.4f));

                                // Consume the food item (eatObject normally handles this)
                                food.Stack--;
                                if (food.Stack <= 0)
                                {
                                    int idx = items.IndexOf(food);
                                    if (idx >= 0) items[idx] = null;
                                }

                                success = true;
                                detail = $"Ate {food.DisplayName} (manual restore, eatObject failed: {eatEx.Message})";
                            }
                        }
                        else
                        {
                            detail = "No edible items in inventory";
                        }
                        break;
                    }

                    default:
                        detail = $"Unknown companion command: {action}";
                        this.monitor.Log($"{companionName}: {detail}", LogLevel.Warn);
                        break;
                }
            }
            catch (Exception ex)
            {
                detail = $"Command error: {ex.Message}";
                this.monitor.Log($"{companionName}: {action} failed: {ex.Message}", LogLevel.Error);
            }

            this.commandResults[companionName] = new { action, success, detail };
            this.monitor.Log($"{companionName}: {action} — {detail}", LogLevel.Info);
        }

        // ======================
        // GLOBAL HELPERS
        // ======================

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
