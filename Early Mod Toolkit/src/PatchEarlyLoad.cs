using System.Reflection;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.ClientNative;
using Vintagestory.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

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

        public static Stopwatch timer = new Stopwatch();

        public static bool IsOverride(MethodInfo m) {
            return m.GetBaseDefinition().DeclaringType != m.DeclaringType;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModLoader), "TryRunModPhase")]
        public static bool TryRunModPhase(Mod mod, ModSystem system, ICoreAPI api, ModRunPhase phase, ref bool __result) {
            if (mod?.Info?.Authors[0] == "Tyron") return true;

            try {
                MethodInfo m = null;
                bool useApi = true;
				switch (phase) {
                    case ModRunPhase.Pre:
                        m = system.GetType().GetMethod("StartPre");
                        break;
                    case ModRunPhase.Start:
                        m = system.GetType().GetMethod("Start");
                        break;
                    case ModRunPhase.AssetsLoaded:
                        m = system.GetType().GetMethod("AssetsLoaded");
                        break;
                    case ModRunPhase.AssetsFinalize:
                        m = system.GetType().GetMethod("AssetsFinalize");
                        break;
                    case ModRunPhase.Normal:
                        if (api.Side == EnumAppSide.Client) {
                            m = system.GetType().GetMethod("StartClientSide");
                        } else {
                            m = system.GetType().GetMethod("StartServerSide");
                        }
                        break;
                    case ModRunPhase.Dispose:
                        m = system.GetType().GetMethod("Dispose");
                        useApi = false;
                        break;
				}
                if (IsOverride(m)) {
                    mod.Logger.Debug("Running system \"{0}\" phase \"{1}\"", system.GetType().Name, phase);
                    timer.Restart();
                    m.Invoke(system, useApi ? new object[] {api} : null);
                    timer.Stop();
                    mod.Logger.Debug("Completed in {0}", timer.Elapsed);
                }
                __result = true;
				return false;
			} catch (Exception ex) {
				mod.Logger.Error("An exception was thrown when trying to run mod system \"{0}\" phase \"{1}\":\n{2}", new object[] { system.GetType().Name, phase, ex });
            } finally {
                timer.Stop();
            }
            __result = false;
            return false;
        }
    }

}