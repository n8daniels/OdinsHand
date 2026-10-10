using HarmonyLib;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// Feeds synthetic input to the local player.
    ///
    /// Calling Player.SetControls from a coroutine does not work: vanilla calls it itself
    /// every frame from real input, so whichever call lands last wins and the harness's
    /// wins only by accident. The first movement run reported every form travelling
    /// exactly 0.00m with the animator never leaving zero - which looked like eight
    /// broken forms and was actually one broken test.
    ///
    /// Overwriting the arguments as they pass through is the reliable way: vanilla's own
    /// call carries the harness's intent, so ordering stops mattering.
    /// </summary>
    public static class VirtualInput
    {
        public static bool Active;

        public static Vector3 MoveDir = Vector3.zero;
        public static bool Run;
        public static bool Attack;
        public static bool SecondaryAttack;
        public static bool Jump;

        public static void Clear()
        {
            Active = false;
            MoveDir = Vector3.zero;
            Run = false;
            Attack = false;
            SecondaryAttack = false;
            Jump = false;
        }

        /// <summary>Presses attack for a single frame, then releases it.</summary>
        public static void TapAttack() => Attack = true;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class VirtualInputPatch
    {
        private static void Prefix(
            Player __instance,
            ref Vector3 movedir,
            ref bool attack,
            ref bool secondaryAttack,
            ref bool jump,
            ref bool run)
        {
            if (!VirtualInput.Active) return;
            if (__instance == null || __instance != Player.m_localPlayer) return;

            // movedir is interpreted in the player's own space, so forward is +Z.
            movedir = VirtualInput.MoveDir;
            run = VirtualInput.Run;
            jump = VirtualInput.Jump;

            if (VirtualInput.Attack) attack = true;
            if (VirtualInput.SecondaryAttack) secondaryAttack = true;

            // Attack is edge-triggered: holding it down does not repeat, so the press is
            // consumed here and the case presses again when it wants another swing.
            VirtualInput.Attack = false;
            VirtualInput.SecondaryAttack = false;
            VirtualInput.Jump = false;
        }
    }
}
