using System;
using HarmonyLib;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>A one-line description of where the game is, for timeout and failure messages.</summary>
    public static class GameState
    {
        public static string Describe()
        {
            try
            {
                var fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
                var where =
                    Player.m_localPlayer != null ? (Player.m_localPlayer.InCutscene() ? "in cutscene" : "in world")
                    : FejdStartup.instance != null ? "main menu"
                    : "loading / no player";

                var player = Player.m_localPlayer;
                var extra = player != null
                    ? $", hp {player.GetHealth():0}/{player.GetMaxHealth():0}, dead={player.IsDead()}, pos {player.transform.position}"
                    : "";

                var popup = UnifiedPopup.IsVisible() ? ", a blocking popup is showing" : "";
                return $"{where}, {fps:0} fps{extra}{popup}";
            }
            catch (Exception e)
            {
                return "unknown (" + e.GetType().Name + ")";
            }
        }
    }
}
