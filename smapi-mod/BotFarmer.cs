using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewMCPBridge
{
    /// <summary>
    /// A shadow Farmer that exists for game mechanics only (tools, combat, fishing).
    /// Never rendered, never added to Game1.otherFarmers.
    /// Based on Farmtronics BotFarmer pattern.
    /// </summary>
    public class BotFarmer : Farmer
    {
        public override void SetMovingUp(bool b)
        {
            if (!b) Halt();
            else moveUp = true;
        }

        public override void SetMovingRight(bool b)
        {
            if (!b) Halt();
            else moveRight = true;
        }

        public override void SetMovingDown(bool b)
        {
            if (!b) Halt();
            else moveDown = true;
        }

        public override void SetMovingLeft(bool b)
        {
            if (!b) Halt();
            else moveLeft = true;
        }

        // Simplified movement that doesn't reference Game1.player internally
        public new void tryToMoveInDirection(int direction, bool isFarmer, int damagesFarmer, bool glider)
        {
            bool canPass = currentLocation.isTilePassable(nextPosition(direction), Game1.viewport);
            if (canPass)
            {
                switch (direction)
                {
                    case 0: position.Y -= speed + addedSpeed; break;
                    case 1: position.X += speed + addedSpeed; break;
                    case 2: position.Y += speed + addedSpeed; break;
                    case 3: position.X -= speed + addedSpeed; break;
                }
            }
        }

        public void FaceToward(Vector2 targetTile)
        {
            Vector2 diff = targetTile * 64f - this.Position;
            if (Math.Abs(diff.X) > Math.Abs(diff.Y))
                this.FacingDirection = diff.X > 0 ? 1 : 3;
            else
                this.FacingDirection = diff.Y > 0 ? 2 : 0;
        }
    }
}
