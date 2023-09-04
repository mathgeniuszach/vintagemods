using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;

using HarmonyLib;
using ProperVersion;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace EMTK {

    // TODO: Dana wants load times

    public class EMTKConfig {
        private static EMTKConfig instance = null;

        public static EMTKConfig Instance {
            get {
                if (instance != null) return instance;

                try {
                    instance = EarlyAPI.LoadModConfig<EMTKConfig>("emtk.json");
                } catch {}
                if (instance == null) {
                    instance = new EMTKConfig();
                    Save();
                }
                
                return instance;
            }
        }

        public static void Save() {
            EarlyAPI.StoreModConfig(instance, "emtk.json");
        }

        public HashSet<string> excludedFromUpdates = new HashSet<string>();
        public HashSet<string> enabledEarlyMods = new HashSet<string>();
    }

    [HarmonyPatch]
    public static class EMTK {
        public static Harmony harmony;
        public static volatile ScreenManager sm;

        public static volatile bool updateAvailable = false;
        public static string version;
        public static List<CachedMod> earlyModsCache;
        public static List<CachedMod> newEarlyModsCache = new();
        public static HashSet<string> earlyModsAvailable = new();
        public static Dictionary<string, ModContainer> loadedMods = new();

        public static Random rng = new Random();

        public static EMTKConfig Config {
            get {
                return EMTKConfig.Instance;
            }
        }

        public static void PseudoDelete(string path) {
            string trash = Path.Combine(GamePaths.Cache, "trash");
            if (!Directory.Exists(trash)) Directory.CreateDirectory(trash);
            
            byte[] uuidBytes = new byte[16];
            rng.NextBytes(uuidBytes);
            string uuid = new Guid(uuidBytes).ToString();

            File.Move(path, Path.Combine(trash, uuid));
        }

        public static SemVer ParseVersion(string modid, string ver) {
            SemVer version;
            string error = null;
            if (!SemVer.TryParse(ver, out version, out error) || error != null) {
                ScreenManager.Platform.Logger.Warning("Dependency '{0}': {1} (best guess: {2})", modid, error, version);
            }
            return version;
        }

        public static void FindEarlyMods() {
            // This needs to run before the mod screen is loaded/reloaded, that way early mods can disable themselves by default

            ModLoader modloader = (ModLoader) AccessTools.Field(typeof(ScreenManager), "modloader").GetValue(sm);
            var ogMods = (List<ModContainer>) AccessTools.Field(typeof(ScreenManager), "allMods").GetValue(sm);
            var mods = new List<ModContainer>(ogMods);

            bool saveCS = false;
            newEarlyModsCache.Clear();
            earlyModsAvailable.Clear();
            using (var asmloader = new ModAssemblyLoader(modloader.ModSearchPaths, mods)) {
                foreach (ModContainer mod in mods) {
                    string modid = mod.Info.ModID + "@" + mod.Info.Version;
                    CachedMod cmod = new(mod);
                    if (!cmod.early) continue;

                    earlyModsAvailable.Add(modid);
                    if (mod.Status != ModStatus.Enabled) continue;
                    
                    if (Config.enabledEarlyMods.Contains(modid)) {
                        cmod.Unpack(modloader, asmloader);
                        newEarlyModsCache.Add(cmod);
                    } else {
                        saveCS = true;
                        mod.Status = ModStatus.Disabled;
                        ClientSettings.DisabledMods.Add(modid);
                    }
                }
            }

            if (saveCS) ClientSettings.Inst.Save(true);

            mods.RemoveAll((m) => m.Status != ModStatus.Enabled);
            // AccessTools.Method(typeof(ModLoader), "ClearCacheFolder").Invoke(modloader, new[] {mods});

            ScreenManager.Platform.Logger.Notification("EMTK: Found {0} early mods", newEarlyModsCache.Count);
        }

        public static void EarlyLoadMods(bool reload = true) {
            // This, and all subsequent load attempts need to run after the screen is loaded.

            while (sm == null) Thread.Sleep(100);

            ModLoader modloader = (ModLoader) AccessTools.Field(typeof(ScreenManager), "modloader").GetValue(sm);
            var ogMods = (List<ModContainer>) AccessTools.Field(typeof(ScreenManager), "allMods").GetValue(sm);

            loadedMods.Clear();
            foreach (ModContainer mod in ogMods) {
                if (mod?.Info?.ModID == null) continue;
                loadedMods[mod.Info.ModID.ToLower()] = mod;
            }

            // Collect all enabled mods
            var mods = new List<ModContainer>(ogMods);
            mods.RemoveAll((m) => m.Status != ModStatus.Enabled);

            // Sort systems to execute them in a particular order
            earlyModsCache = new List<CachedMod>(newEarlyModsCache);
            earlyModsCache.Sort((a, b) => a.emi.LoadOrder.CompareTo(b.emi.LoadOrder));

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
            ScreenManager.Platform.Logger.Notification("EMTK: Early loading assets!");

            AddSomeExternalAssets(ScreenManager.Platform.Logger, modloader);

            foreach (KeyValuePair<string, ITranslationService> locale in Lang.AvailableLanguages) {
                locale.Value.Invalidate();
            }
            Lang.Load(ScreenManager.Platform.Logger, ScreenManager.Platform.AssetManager, ClientSettings.Language);

            // Run load functions of early mods in order
            ScreenManager.Platform.Logger.Notification("EMTK: Early loading mods!");

            foreach (var cmod in earlyModsCache) {
                foreach (Type system in cmod.systems) {
                    try {
                        system.GetMethod("EarlyLoad")?.Invoke(null, new object[] {cmod.mod, sm});
                    } catch (Exception ex) {
                        ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when early loading {0}: {1}, InnerException: {2}.", new object[] {
                            cmod.mod.Assembly.FullName,
                            ex,
                            ex.InnerException
                        });
                    }
                }
            }

            ScreenManager.Platform.Logger.Notification("EMTK: Early loading complete!");
        }

        public static void EarlyUnloadMods(bool reload = true) {
            while (sm == null) Thread.Sleep(100);
            // List<ModContainer> mods = (List<ModContainer>) AccessTools.Field(typeof(ScreenManager), "verifiedMods").GetValue(sm);

            ScreenManager.Platform.Logger.Notification("EMTK: Early unloading mods!");

            earlyModsCache.Reverse();
            foreach (var cmod in earlyModsCache) {
                foreach (Type system in cmod.systems) {
                    try {
                        system.GetMethod("EarlyUnload")?.Invoke(null, null);
                    } catch (Exception ex) {
                        ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when early unloading {0}: {1}, InnerException: {2}.", new object[] {
                            cmod.mod.Assembly.FullName,
                            ex,
                            ex.InnerException
                        });
                    }
                }
            }
            earlyModsCache.Clear();

            ScreenManager.Platform.Logger.Notification("EMTK: Early unloading mod assets!");

            // Unload assets by loading back to vanilla
            AssetManager asm = ScreenManager.Platform.AssetManager;
            asm.UnloadExternalAssets(ScreenManager.Platform.Logger);
            
            foreach (KeyValuePair<string, ITranslationService> locale in Lang.AvailableLanguages) {
                locale.Value.Invalidate();
            }
            Lang.Load(ScreenManager.Platform.Logger, asm, ClientSettings.Language);


            ScreenManager.Platform.Logger.Notification("EMTK: Early unloading complete!");
        }

        public static void ReloadTitle(bool saveScroll = true) {
            var curscrn = AccessTools.Field(typeof(ScreenManager), "CurrentScreen");
            var handlepos = AccessTools.Field(typeof(GuiElementScrollbar), "currentHandlePosition");
            var handlehandler = AccessTools.Method(typeof(GuiElementScrollbar), "UpdateHandlePositionAbs");

            // Save scroll location for later
            var scrn = curscrn.GetValue(sm);
            GuiScreenMods gsm = null;
            if (scrn is GuiScreenMods) gsm = (GuiScreenMods) scrn;

            float pos = gsm != null ? (float) handlepos.GetValue(gsm.ElementComposer.GetScrollbar("scrollbar")) : 0;
            // Reload the title screen
            AccessTools.Method(typeof(ScreenManager), "StartMainMenu").Invoke(sm, null);
            // Re-open the mods menu
            var mml = (GuiCompositeMainMenuLeft) AccessTools.Field(typeof(ScreenManager), "guiMainmenuLeft").GetValue(sm);
            AccessTools.Method(typeof(GuiCompositeMainMenuLeft), "OnMods").Invoke(mml, null);
            if (gsm == null) sm.LoadScreen((GuiScreen) scrn);
            // Reload scroll location so it seems like nothing happened
            if (saveScroll && gsm != null) {
                gsm = (GuiScreenMods) curscrn.GetValue(sm);
                handlehandler.Invoke(gsm.ElementComposer.GetScrollbar("scrollbar"), new object[] {pos});
            }
        }

        public static void FullEarlyReloadMods() {
            bool reloadTitle = false;

            foreach (CachedMod mod in earlyModsCache) {
                if (mod.emi != null && mod.emi.ReloadTitle) {
                    reloadTitle = true;
                    break;
                }
            }

            EarlyUnloadMods();
            EarlyLoadMods();

            if (!reloadTitle) {
                foreach (CachedMod mod in earlyModsCache) {
                    if (mod.emi != null && mod.emi.ReloadTitle) {
                        reloadTitle = true;
                        break;
                    }
                }
            }

            if (reloadTitle) ReloadTitle(false);
        }

        // Fast reloading of mods occurs when enabling or disabling mods.
        // Some mods - and particularly assets - depend on each other to work properly.
        public static void FastEarlyReloadMods() {
            bool reloadTitle = false;
            HashSet<string> reloaders = new();

            foreach (CachedMod mod in earlyModsCache) {
                if (mod.emi != null && mod.emi.ReloadTitle) {
                    // Count all the mods that modify the title screen.
                    reloaders.Add(mod.mod.Info.ModID + "@" + mod.mod.Info.Version);
                }
            }

            EarlyUnloadMods(false);
            EarlyLoadMods(false);

            foreach (CachedMod mod in earlyModsCache) {
                if (mod.emi != null && mod.emi.ReloadTitle) {
                    string id = mod.mod.Info.ModID + "@" + mod.mod.Info.Version;
                    if (reloaders.Contains(id)) {
                        // Title screen mods that have not been disabled do not trigger a title screen reload.
                        reloaders.Remove(id);
                    } else {
                        // A new mod with title screen changes was found. We're gonna have to reload.
                        reloadTitle = true;
                        break;
                    }
                }
            }

            if (reloadTitle || reloaders.Count > 0) ReloadTitle(true);
        }

        public static void AddSomeExternalAssets(ILogger Logger, ModLoader modloader) {
            AssetManager asm = ScreenManager.Platform.AssetManager;

            List<string> assetOriginsForLog = new();
			List<IAssetOrigin> externalOrigins = new();

            foreach (KeyValuePair<string, IAssetOrigin> val in modloader.GetContentArchives()) {
                asm.Origins.Add(val.Value);
                externalOrigins.Add(val.Value);
                assetOriginsForLog.Add("mod@" + val.Key);
            }
            foreach (KeyValuePair<string, IAssetOrigin> val2 in modloader.GetThemeArchives()) {
                asm.Origins.Add(val2.Value);
                externalOrigins.Add(val2.Value);
                assetOriginsForLog.Add("themepack@" + val2.Key);
            }

			if (assetOriginsForLog.Count > 0) {
				Logger.Notification("EMTK: Early External Origins in load order: {0}", new object[] { string.Join(", ", assetOriginsForLog)});
			}

            FieldInfo assetsByCategory = AccessTools.Field(typeof(AssetManager), "assetsByCategory");
            MethodInfo getAssetsDontLoad = AccessTools.Method(typeof(AssetManager), "GetAssetsDontLoad");

            var minCategories = new List<AssetCategory>() {
                AssetCategory.categories["lang"],
                AssetCategory.categories["textures"],
                AssetCategory.categories["sounds"],
                AssetCategory.categories["music"],
            };

			foreach (AssetCategory category in minCategories) {
                var categoryassets = (Dictionary<AssetLocation, IAsset>)getAssetsDontLoad.Invoke(asm, new object[] {category, externalOrigins});
                foreach (IAsset asset in categoryassets.Values) {
                    asm.Assets[asset.Location] = asset;
                }

                List<IAsset> list;
                var abc = (IDictionary<string, List<IAsset>>)assetsByCategory.GetValue(asm);
                if (!abc.TryGetValue(category.Code, out list)) {
                    list = (abc[category.Code] = new List<IAsset>());
                }
                list.AddRange(categoryassets.Values);

                Logger.Notification("EMTK: Found {1} external assets in category {0}", new object[] {category, categoryassets.Count});
			}
			asm.allAssetsLoaded = true;
        }
    }

}