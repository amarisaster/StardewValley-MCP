using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace StardewMCPBridge
{
    /// <summary>
    /// Pairs a visible NPC (custom sprite) with a shadow BotFarmer (game mechanics).
    /// The NPC walks around and is what the player sees.
    /// The shadow farmer performs tool use, combat, fishing, and shopping.
    /// </summary>
    public class CompanionFarmer
    {
        public BotFarmer Shadow { get; private set; }
        public NPC Visual { get; private set; }
        public string Name { get; private set; }

        private readonly IMonitor monitor;
        private readonly IModHelper helper;

        public CompanionFarmer(NPC visualNpc, string name, IMonitor monitor, IModHelper helper)
        {
            this.Visual = visualNpc;
            this.Name = name;
            this.monitor = monitor;
            this.helper = helper;

            this.Shadow = new BotFarmer();
            this.Shadow.UniqueMultiplayerID = helper.Multiplayer.GetNewID();
            this.Shadow.Name = name + "_shadow";
            this.Shadow.displayName = name;
            this.Shadow.Speed = 2;
            this.Shadow.Stamina = Farmer.startingStamina;
            this.Shadow.MaxItems = 36;
            this.Shadow.Money = 0; // companions don't need their own money

            // Give initial tools
            var tools = Farmer.initialTools();
            foreach (var tool in tools)
                this.Shadow.Items.Add(tool);

            // Pad inventory
            for (int i = this.Shadow.Items.Count; i < 36; i++)
                this.Shadow.Items.Add(null);

            this.SyncFromNpc();
            this.monitor.Log($"Shadow farmer created for {name}", LogLevel.Info);
        }

        /// <summary>Sync shadow farmer position/location from the visible NPC.</summary>
        public void SyncFromNpc()
        {
            this.Shadow.Position = this.Visual.Position;
            this.Shadow.currentLocation = this.Visual.currentLocation ?? Game1.player.currentLocation;
            this.Shadow.FacingDirection = this.Visual.FacingDirection;
        }

        // ======================
        // TOOL USE
        // ======================

        /// <summary>Use a tool at a specific tile. Shadow farmer handles the game mechanics.</summary>
        public bool UseToolAt(Vector2 tile, Type toolType)
        {
            this.SyncFromNpc();

            var tool = this.Shadow.Items.FirstOrDefault(i => i != null && toolType.IsInstanceOfType(i)) as Tool;
            if (tool == null)
            {
                this.monitor.Log($"{this.Name}: Don't have a {toolType.Name}", LogLevel.Warn);
                return false;
            }

            if (this.Shadow.Stamina <= 0)
            {
                this.monitor.Log($"{this.Name}: Out of energy", LogLevel.Warn);
                return false;
            }

            this.Shadow.FaceToward(tile);
            float oldStamina = this.Shadow.Stamina;

            try
            {
                if (tool is MeleeWeapon weapon)
                {
                    var toolLoc = this.Shadow.GetToolLocation(true);
                    weapon.DoDamage(this.Shadow.currentLocation,
                        (int)toolLoc.X, (int)toolLoc.Y,
                        this.Shadow.FacingDirection, 1, this.Shadow);
                }
                else
                {
                    int tilePixelX = (int)(tile.X * 64f);
                    int tilePixelY = (int)(tile.Y * 64f);
                    tool.DoFunction(this.Shadow.currentLocation, tilePixelX, tilePixelY, 1, this.Shadow);
                }

                this.Shadow.checkForExhaustion(oldStamina);
                this.monitor.Log($"{this.Name}: Used {tool.Name} at ({tile.X},{tile.Y})", LogLevel.Trace);
                return true;
            }
            catch (Exception ex)
            {
                this.monitor.Log($"{this.Name}: Tool use failed: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        // ======================
        // COMBAT
        // ======================

        /// <summary>Find the nearest monster within range.</summary>
        public Monster FindNearestMonster(float range = 256f)
        {
            this.SyncFromNpc();
            Monster nearest = null;
            float nearestDist = range;

            foreach (var c in this.Shadow.currentLocation.characters)
            {
                if (c is Monster m && !m.IsInvisible && m.Health > 0)
                {
                    float dist = Vector2.Distance(this.Shadow.Position, m.Position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = m;
                    }
                }
            }
            return nearest;
        }

        /// <summary>Attack nearby monsters using equipped weapon.</summary>
        public bool AttackNearbyMonsters()
        {
            this.SyncFromNpc();

            var weapon = this.Shadow.Items.FirstOrDefault(i => i is MeleeWeapon) as MeleeWeapon;
            if (weapon == null) return false;

            var monster = this.FindNearestMonster();
            if (monster == null) return false;

            // Face the monster
            Vector2 monsterTile = monster.Tile;
            this.Shadow.FaceToward(monsterTile);

            var toolLoc = this.Shadow.GetToolLocation(true);
            try
            {
                weapon.DoDamage(this.Shadow.currentLocation,
                    (int)toolLoc.X, (int)toolLoc.Y,
                    this.Shadow.FacingDirection, 1, this.Shadow);
                this.monitor.Log($"{this.Name}: Attacked monster at ({monsterTile.X},{monsterTile.Y})", LogLevel.Trace);
                return true;
            }
            catch (Exception ex)
            {
                this.monitor.Log($"{this.Name}: Attack failed: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>Damage monsters in an area around the companion (fallback if weapon DoDamage fails).</summary>
        public bool AreaAttack(int minDmg = 5, int maxDmg = 15)
        {
            this.SyncFromNpc();
            var pos = this.Shadow.Position;
            var area = new Rectangle((int)pos.X - 64, (int)pos.Y - 64, 192, 192);

            try
            {
                bool hit = this.Shadow.currentLocation.damageMonster(
                    area, minDmg, maxDmg, false, 1f, 0, 0.02f, 3f, true, this.Shadow);
                if (hit)
                    this.monitor.Log($"{this.Name}: Area attack hit!", LogLevel.Trace);
                return hit;
            }
            catch { return false; }
        }

        // ======================
        // MINING
        // ======================

        /// <summary>Break a rock/stone at a tile using the pickaxe.</summary>
        public bool MineRock(Vector2 tile)
        {
            return this.UseToolAt(tile, typeof(Pickaxe));
        }

        /// <summary>Find nearest breakable rock in the current location.</summary>
        public Vector2? FindNearestRock()
        {
            this.SyncFromNpc();
            var location = this.Shadow.currentLocation;
            Vector2 myTile = this.Shadow.Tile;
            Vector2? nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value.Name != null && pair.Value.Name.Contains("Stone"))
                {
                    float dist = Vector2.Distance(myTile, pair.Key);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = pair.Key;
                    }
                }
            }
            return nearest;
        }

        // ======================
        // FISHING
        // ======================

        /// <summary>Cast the fishing rod. Call from a waterfront tile.</summary>
        public bool CastFishingRod()
        {
            this.SyncFromNpc();
            var rod = this.Shadow.Items.FirstOrDefault(i => i is FishingRod) as FishingRod;
            if (rod == null)
            {
                this.monitor.Log($"{this.Name}: No fishing rod", LogLevel.Warn);
                return false;
            }

            try
            {
                var pos = this.Shadow.Position;
                rod.beginUsing(this.Shadow.currentLocation, (int)pos.X, (int)pos.Y, this.Shadow);
                rod.castingPower = 1f; // max distance
                this.monitor.Log($"{this.Name}: Cast fishing rod", LogLevel.Trace);
                return true;
            }
            catch (Exception ex)
            {
                this.monitor.Log($"{this.Name}: Fishing cast failed: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>Check if rod is nibbling and hook the fish.</summary>
        public bool CheckAndHookFish()
        {
            var rod = this.Shadow.Items.FirstOrDefault(i => i is FishingRod) as FishingRod;
            if (rod == null) return false;

            if (rod.isNibbling && !rod.isReeling && !rod.hit && !rod.pullingOutOfWater)
            {
                rod.DoFunction(this.Shadow.currentLocation, 1, 1, 1, this.Shadow);
                this.monitor.Log($"{this.Name}: Hooked a fish!", LogLevel.Info);
                return true;
            }
            return false;
        }

        // ======================
        // SHOPPING
        // ======================

        /// <summary>Buy an item by directly adding to inventory and deducting from the player's money.</summary>
        public bool BuyItem(string qualifiedItemId, int quantity, int unitPrice)
        {
            // Use the player's money since companions don't have their own economy
            int total = unitPrice * quantity;
            if (Game1.player.Money >= total)
            {
                Game1.player.Money -= total;
                Item item = ItemRegistry.Create(qualifiedItemId, quantity);
                Game1.player.addItemToInventory(item);
                this.monitor.Log($"{this.Name}: Bought {quantity}x {item.DisplayName} for {total}g", LogLevel.Info);
                return true;
            }
            this.monitor.Log($"{this.Name}: Not enough money for purchase ({total}g needed)", LogLevel.Warn);
            return false;
        }

        // ======================
        // NAVIGATION
        // ======================

        /// <summary>Warp both visual NPC and shadow farmer to a new location.</summary>
        public void WarpTo(string locationName, int tileX, int tileY)
        {
            GameLocation target = Game1.getLocationFromName(locationName);
            if (target == null)
            {
                this.monitor.Log($"{this.Name}: Location '{locationName}' not found", LogLevel.Warn);
                return;
            }

            // Remove NPC from old location
            this.Visual.currentLocation?.characters.Remove(this.Visual);

            // Move both to new location
            var pos = new Vector2(tileX * 64f, tileY * 64f);
            this.Visual.Position = pos;
            this.Visual.currentLocation = target;
            target.addCharacter(this.Visual);

            this.Shadow.Position = pos;
            this.Shadow.currentLocation = target;

            this.monitor.Log($"{this.Name}: Warped to {locationName} ({tileX},{tileY})", LogLevel.Info);
        }

        /// <summary>Get current stamina as a percentage.</summary>
        public float GetStaminaPercent()
        {
            return this.Shadow.Stamina / this.Shadow.MaxStamina * 100f;
        }

        /// <summary>Rest to recover stamina (simulated).</summary>
        public void Rest(float amount = 10f)
        {
            this.Shadow.Stamina = Math.Min(this.Shadow.Stamina + amount, this.Shadow.MaxStamina);
        }
    }
}
