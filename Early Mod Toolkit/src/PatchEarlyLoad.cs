using HarmonyLib;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.ClientNative;
using Vintagestory.Common;

namespace EMTK {
    [HarmonyPatch]
    public static class EarlyLoadPatcher {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GamePaths), "Binaries", MethodType.Getter)]
        public static bool GetBinaries(ref string __result) {
            __result = Initializer.basepath;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GamePaths), "BinariesMods", MethodType.Getter)]
        public static bool GetBinariesMods(ref string __result) {
            __result = Path.Combine(Initializer.basepath, "Mods");
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ScreenManager), "Render")]
        public static void Render(ScreenManager __instance) {
            EMTK.sm = __instance;

            // Single-use patch.
            EMTK.harmony.Unpatch(AccessTools.Method(typeof(ScreenManager), "Render"), HarmonyPatchType.Prefix, "emtk");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ScreenManager), "EnqueueMainThreadTask")]
        public static void EnqueueMainThreadTask() {
            EMTK.EarlyLoadMods();

            // Single-use patch.
            EMTK.harmony.Unpatch(typeof(ScreenManager).GetMethod("EnqueueMainThreadTask"), HarmonyPatchType.Prefix, "emtk");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GuiScreenMods), "OnReloadMods")]
        public static void OnReloadMods() {
            EMTK.FullEarlyReloadMods();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GuiScreenMods), "OnClickCellRight")]
        public static void OnClickCellRight(GuiScreenMods __instance, int cellIndex) {
            EMTK.FastEarlyReloadMods();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(CrashReporter), "Crash")]
        public static IEnumerable<CodeInstruction> Crash(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            return new CodeMatcher(instructions, generator)
                .MatchStartForward(new CodeMatch(OpCodes.Ldstr, "Game Version: "))
                .SetOperandAndAdvance("EMTK Version: " + EMTK.version + Environment.NewLine + "Game Version: ")
                .InstructionEnumeration();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModContainer), "LoadModInfo")]
        public static void LoadModInfo(ModContainer __instance) {
            if (__instance.Icon == null) {
                AccessTools.PropertySetter(typeof(ModContainer), "Icon").Invoke(__instance, new[] {
                    new BitmapExternal(Path.Combine(GamePaths.AssetsPath, "game/textures/gui/3rdpartymodicon.png"))
                });
            }
        }
    }

}