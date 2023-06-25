using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Client;
using Vintagestory.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using System.IO;
using System.Linq;

[assembly: EMTK.CoreMod(
    name: "Early Mod Toolkit",
    modID: "emtk",
    Version = "1.0.0",
    Side = "Client",
    Description = "Adds useful features for designing early mods.",
    Authors = new[] { "mathgeniuszach"}
)]

namespace EMTK {

    public class CoreModAttribute : ModInfoAttribute {
        public static bool patched = false;
        public CoreModAttribute(string name, string modID) : base(name, modID) {
            if (patched) return;
            patched = true;

            EMTK.harmony = new Harmony("emtk");
            EMTK.harmony.PatchAll();
        }
    }

    public class CachedModSystem {
        public ModContainer mod;
        public double order;
        public Type system;

        public CachedModSystem(ModContainer mod, double order, Type system) {
            this.mod = mod;
            this.order = order;
            this.system = system;
        }
    }

    [HarmonyPatch]
    public static class EMTK {
        public static Harmony harmony;
        public static volatile ScreenManager sm;

        public static string version;

        public static List<CachedModSystem> modSystems = new List<CachedModSystem>();

        public static void EarlyLoadMods() {
            while (sm == null) Thread.Sleep(100);

            ModLoader modloader = (ModLoader) AccessTools.Field(typeof(ScreenManager), "modloader").GetValue(sm);
            var ogMods = (List<ModContainer>) AccessTools.Field(typeof(ScreenManager), "allMods").GetValue(sm);

            // Remove EMTK from the original mod list, as it's not meant to be disabled
            // A harmony patch changes the mod list slightly to show that emtk is enabled.
            ogMods.RemoveAll((m) => {
                if (m.Info.ModID == "emtk") {
                    version = m.Info.Version;
                    return true;
                }
                return false;
            });

            // Collect enabled mods and the mod loader
            var mods = new List<ModContainer>(ogMods);
            mods.RemoveAll((m) => m.Status != ModStatus.Enabled);

            ScreenManager.Platform.Logger.Notification("EMTK: Early loading mod assets!");

            // Unpack all mods to load them and the code inside early
            foreach (ModContainer mod in mods) {
                mod.Unpack(modloader.UnpackPath);
            }
            AccessTools.Method(typeof(ModLoader), "ClearCacheFolder").Invoke(modloader, new[] {mods});


            // Compile code of each assembly for use
            ModCompilationContext ctx = (ModCompilationContext) AccessTools.Field(typeof(ModLoader), "compilationContext").GetValue(modloader);
            using (ModAssemblyLoader loader = new ModAssemblyLoader(modloader.ModSearchPaths, mods)) {
				foreach (ModContainer mod in mods) {
					mod.LoadAssembly(ctx, loader);
				}
			}

            // Setup origin locations for preloading assets
            var contentAssetOrigins = new OrderedDictionary<string, IAssetOrigin>();
			var themeAssetOrigins = new OrderedDictionary<string, IAssetOrigin>();
            AccessTools.Field(typeof(ModLoader), "contentAssetOrigins").SetValue(modloader, contentAssetOrigins);
            AccessTools.Field(typeof(ModLoader), "themeAssetOrigins").SetValue(modloader, themeAssetOrigins);

			var textureSizes = new OrderedDictionary<string, int>();
			foreach (ModContainer mod in mods) {
				if (mod.FolderPath != null && Directory.Exists(Path.Combine(mod.FolderPath, "assets"))) {
                    if (mod.Info.Type == EnumModType.Theme) {
                        themeAssetOrigins.Add(mod.FileName, new ThemeFolderOrigin(mod.FolderPath));
                    } else {
                        contentAssetOrigins.Add(mod.FileName, new FolderOrigin(mod.FolderPath));
                    }

                    textureSizes.Add(mod.FileName, mod.Info.TextureSize);
				}
			}
			if (textureSizes.Count > 0) {
                modloader.TextureSize = Enumerable.Last<int>(textureSizes.Values);
			}

            // Preload assets for the title screen
            AssetManager asm = ScreenManager.Platform.AssetManager;
            asm.AddExternalAssets(ScreenManager.Platform.Logger, modloader);
            foreach (KeyValuePair<string, ITranslationService> locale in Lang.AvailableLanguages) {
                locale.Value.Invalidate();
            }
            Lang.Load(ScreenManager.Platform.Logger, asm, ClientSettings.Language);

            ScreenManager.Platform.Logger.Notification("EMTK: Early loading mods!");

            // Collect mod systems
            modSystems.Clear();
            foreach (ModContainer mod in mods) {
                if (mod.Assembly == null) continue;
                
                IEnumerable<Type> systems;
                try {
                    systems = Enumerable.Where<Type>(mod.Assembly.GetTypes(), (Type type) => typeof(ModSystem).IsAssignableFrom(type) && !type.IsAbstract);
                } catch (Exception ex) {
                    ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when obtaining assembly types of {0}: {1}, InnerException: {2}. Skipping Assembly", new object[] {
                        mod.Assembly.FullName,
                        ex,
                        ex.InnerException
                    });
                    continue;
                }

                foreach (Type ms in systems) {
                    if (ms.GetMethod("EarlyLoad") != null) {
                        double order = 0.0;
                        try {
                            var method = ms.GetMethod("EarlyExecuteOrder");
                            if (method != null) method.Invoke(null, new[] {mod});
                            modSystems.Add(new CachedModSystem(mod, order, ms));
                        } catch (Exception ex) {
                            ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when determining early load order of {0}: {1}, InnerException: {2}. Skipping ModSystem", new object[] {
                                mod.Assembly.FullName,
                                ex,
                                ex.InnerException
                            });
                        }
                    }
                }
            }

            // Sort systems to execute them in a particular order
            modSystems.Sort((a, b) => a.order.CompareTo(b.order));
            ScreenManager.Platform.Logger.Notification("EMTK: Found {0} early mods", modSystems.Count);

            // Run load functions of early mods in order
            foreach (var cms in modSystems) {
                try {
                    cms.system.GetMethod("EarlyLoad").Invoke(null, new object[] {cms.mod, sm});
                } catch (Exception ex) {
                    ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when early loading {0}: {1}, InnerException: {2}.", new object[] {
						cms.mod.Assembly.FullName,
						ex,
						ex.InnerException
					});
                }
            }

            ScreenManager.Platform.Logger.Notification("EMTK: Early loading complete!");
        }

        public static void EarlyUnloadMods() {
            while (sm == null) Thread.Sleep(100);
            List<ModContainer> mods = (List<ModContainer>) AccessTools.Field(typeof(ScreenManager), "verifiedMods").GetValue(sm);

            ScreenManager.Platform.Logger.Notification("EMTK: Early unloading mods!");

            modSystems.Reverse();
            foreach (var cms in modSystems) {
                try {
                    cms.system.GetMethod("EarlyUnload").Invoke(null, null);
                } catch (Exception ex) {
                    ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when early unloading {0}: {1}, InnerException: {2}.", new object[] {
						cms.mod.Assembly.FullName,
						ex,
						ex.InnerException
					});
                }
            }
            modSystems.Clear();

            ScreenManager.Platform.Logger.Notification("EMTK: Early unloading mod assets!");

            // FIXME: THIS DOES NOT WORK
            // Unload assets by loading back to vanilla
            AssetManager asm = ScreenManager.Platform.AssetManager;
            asm.UnloadExternalAssets(ScreenManager.Platform.Logger);
            
            foreach (KeyValuePair<string, ITranslationService> locale in Lang.AvailableLanguages) {
                locale.Value.Invalidate();
            }
            Lang.Load(ScreenManager.Platform.Logger, asm, ClientSettings.Language);

            ScreenManager.Platform.Logger.Notification("EMTK: Early unloading complete!");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiScreenMods), "InitGui")]
        public static void InitGui(GuiScreenMods __instance) {
            // Here we prefix patch for the sole purpose of changing this one call to dialogBase.
            // This is important, because it lets us add text without recomposing the ui.
            harmony.Patch(
                AccessTools.Method(typeof(GuiScreen), "dialogBase"),
                null, new HarmonyMethod(typeof(EMTK), "dialogBase")
            );
        }

        public static void dialogBase(GuiScreen __instance, GuiComposer __result) {
            __result.AddStaticText(
                "EMTK v" + version,
                CairoFont.WhiteSmallishText(),
                ElementBounds.Fixed(EnumDialogArea.LeftBottom, 0.0, 0.0, 690.0, 20.0),
                null
            );
            // We only wanted to patch the call to this for a single gui, so we unpatch after adding text.
            harmony.Unpatch(
                AccessTools.Method(typeof(GuiScreen), "dialogBase"),
                HarmonyPatchType.Postfix,
                "emtk"
            );
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ScreenManager), "Render")]
        public static void Render(ScreenManager __instance) {
            sm = __instance;

            // Single-use patch.
            harmony.Unpatch(AccessTools.Method(typeof(ScreenManager), "Render"), HarmonyPatchType.Prefix, "emtk");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ScreenManager), "EnqueueMainThreadTask")]
        public static void EnqueueMainThreadTask() {
            EarlyLoadMods();

            // Single-use patch.
            harmony.Unpatch(typeof(ScreenManager).GetMethod("EnqueueMainThreadTask"), HarmonyPatchType.Prefix, "emtk");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GuiScreenMods), "OnReloadMods")]
        public static void OnReloadMods() {
            EarlyUnloadMods();
            EarlyLoadMods();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GuiScreenMods), "OnClickCellRight")]
        public static void OnClickCellRight(GuiScreenMods __instance, int cellIndex) {
            EarlyUnloadMods();
            EarlyLoadMods();
        }
    }

    public class EMTKSystem : ModSystem {
        public override bool ShouldLoad(EnumAppSide forSide) {
			return false;
		}

        public override bool AllowRuntimeReload => false;
    }

}