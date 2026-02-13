// Assets/Gironoid/_Project/Code/Core/Visual/ShipSpriteResolver.cs
using UnityEngine;

namespace Gironoid._Project.Code.Core.Visual
{
    public static class ShipSpriteResolver
    {
        // Положите спрайты в: Assets/Resources/Gironoid/Ships/AEGIS.png и т.п.
        public static Sprite LoadHullSprite(string shipId)
        {
            if (string.IsNullOrEmpty(shipId)) return null;
            return Resources.Load<Sprite>($"Gironoid/Ships/{shipId}");
        }
    }
}