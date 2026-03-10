using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;

namespace StardewMCPBridge
{
    public class ModEntry : Mod
    {
        private string bridgePath;
        private string actionPath;
        private BotManager botManager;
        private Texture2D companion1Portrait;
        private Texture2D companion2Portrait;
        private Texture2D companion1Sprite;
        private Texture2D companion2Sprite;

        public override void Entry(IModHelper helper)
        {
            this.botManager = new BotManager(this.Monitor, helper);
            this.bridgePath = Path.Combine(helper.DirectoryPath, "bridge_data.json");
            this.actionPath = Path.Combine(helper.DirectoryPath, "actions.json");

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.DayEnding += this.OnDayEnding;
            helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;

            this.Monitor.Log("Stardew MCP Bridge initialized. Content pipeline registered.", LogLevel.Debug);
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            this.companion1Portrait = this.Helper.ModContent.Load<Texture2D>("assets/Companion1_portrait.png");
            this.companion2Portrait = this.Helper.ModContent.Load<Texture2D>("assets/Companion2_portrait.png");
            this.companion1Sprite = this.Helper.ModContent.Load<Texture2D>("assets/Companion1_sprite.png");
            this.companion2Sprite = this.Helper.ModContent.Load<Texture2D>("assets/Companion2_sprite.png");
            this.Monitor.Log("Bridge online. Portraits and sprites loaded. Waiting for world.", LogLevel.Info);
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            // Inject portrait textures
            if (e.NameWithoutLocale.IsEquivalentTo("Portraits/Companion1"))
            {
                e.LoadFrom(() => this.companion1Portrait, AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Portraits/Companion2"))
            {
                e.LoadFrom(() => this.companion2Portrait, AssetLoadPriority.Exclusive);
            }
            // Custom sprite sheets for walking animation
            else if (e.NameWithoutLocale.IsEquivalentTo("Characters/Companion1"))
            {
                e.LoadFrom(() => this.companion1Sprite, AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Characters/Companion2"))
            {
                e.LoadFrom(() => this.companion2Sprite, AssetLoadPriority.Exclusive);
            }
            // Inject NPC data so the game considers us valid
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Characters"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, CharacterData>();

                    if (!data.Data.ContainsKey("Companion1"))
                    {
                        data.Data["Companion1"] = new CharacterData
                        {
                            DisplayName = "Companion1",
                            HomeRegion = "Town",
                        };
                    }

                    if (!data.Data.ContainsKey("Companion2"))
                    {
                        data.Data["Companion2"] = new CharacterData
                        {
                            DisplayName = "Companion2",
                            HomeRegion = "Town",
                        };
                    }
                });
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // AI ticks every frame (60/sec) for responsive combat, pathfinding, stuck detection
            this.botManager.Update();

            // Bridge I/O every 30 ticks (~0.5s) to avoid thrashing disk
            if (e.IsMultipleOf(30))
            {
                this.SyncGameState();
                this.ProcessActions();
            }
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            // Safety net: if it's 2:00 AM (forced pass-out time), signal bots ready
            if (e.NewTime >= 2600)
                this.botManager.SignalAllSleepReady();
        }

        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            // Signal all bot farmers as sleep-ready so the game doesn't deadlock
            this.botManager.SignalAllSleepReady();
            this.Monitor.Log("Day ending: bot farmers signaled sleep ready", LogLevel.Debug);
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            this.botManager.OnDayStarted();
            this.Monitor.Log("New day: companion stamina restored", LogLevel.Info);
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            this.botManager.Cleanup();
            this.Monitor.Log("Returned to title: companions cleaned up", LogLevel.Info);
        }

        private void SyncGameState()
        {
            try
            {
                var state = new
                {
                    time = Game1.timeOfDay,
                    day = Game1.dayOfMonth,
                    season = Game1.currentSeason,
                    weather = Game1.isLightning ? "storm" : Game1.isRaining ? "rain" : Game1.isSnowing ? "snow" : Game1.isDebrisWeather ? "windy" : "sunny",
                    location = Game1.currentLocation?.Name,
                    player = new
                    {
                        name = Game1.player.Name,
                        health = Game1.player.health,
                        stamina = Game1.player.Stamina,
                        money = Game1.player.Money,
                        position = new { x = Game1.player.Position.X, y = Game1.player.Position.Y }
                    },
                    companions = this.botManager.GetBotStatus(),
                    npcs = Game1.currentLocation?.characters.Select(c => new {
                        name = c.Name,
                        position = new { x = c.Position.X, y = c.Position.Y }
                    }).ToList(),
                    syncedAt = DateTime.UtcNow.ToString("o")
                };

                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                // Atomic write: temp file then rename, so MCP never reads partial JSON
                string tmpPath = this.bridgePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, this.bridgePath, true);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Bridge Sync Error: {ex.Message}", LogLevel.Error);
            }
        }

        private void ProcessActions()
        {
            try
            {
                if (!File.Exists(this.actionPath))
                    return;

                string json = File.ReadAllText(this.actionPath);

                // Delete FIRST to avoid race: MCP writes new action between read and delete
                File.Delete(this.actionPath);

                if (string.IsNullOrWhiteSpace(json))
                    return;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("actionType", out var actionType))
                    return;

                // Route to bot manager for companion actions
                this.botManager.ProcessAction(json);

                // Handle chat separately (not a companion action)
                if (actionType.GetString() == "chat")
                {
                    if (root.TryGetProperty("metadata", out var meta) &&
                        meta.TryGetProperty("message", out var msg))
                    {
                        Game1.chatBox?.addMessage(msg.GetString(), Microsoft.Xna.Framework.Color.Gold);
                        this.Monitor.Log($"Chat sent: {msg.GetString()}", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Action Processing Error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
