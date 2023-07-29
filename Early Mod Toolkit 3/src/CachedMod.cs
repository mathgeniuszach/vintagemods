using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

using HarmonyLib;

using Newtonsoft.Json;

using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Common;

namespace EMTK {
    public class CachedMod {
        public static FieldInfo cctx = AccessTools.Field(typeof(ModLoader), "compilationContext");

        public ModContainer mod;

        public bool early = false;
        public EarlyModInfo emi;

        public Type[] systems = new Type[] {};

        public CachedMod(ModContainer mod, ModLoader loader, ModAssemblyLoader asmloader) {
            this.mod = mod;

            switch (mod.SourceType) {
                case EnumModSourceType.ZIP:
                    using (ZipArchive zip = ZipFile.OpenRead(mod.SourcePath)) {
                        ZipArchiveEntry entry = zip.GetEntry("earlymod.json");
                        if (entry == null) return;
                        early = true;
                        
                        try {
                            using (var reader = new StreamReader(entry.Open())) {
                                emi = JsonConvert.DeserializeObject<EarlyModInfo>(reader.ReadToEnd());
                            }
                        } catch (Exception ex) {
                            ScreenManager.Platform.Logger.Error(
                                "An exception was thrown trying to load the EarlyModInfo of \"{0}\":\n{1}",
                                new object[] {mod.Info.ModID, ex}
                            );
                            emi = new EarlyModInfo();
                        }
                    }
                    break;
                case EnumModSourceType.Folder:
                    string path = Path.Combine(mod.FolderPath, "earlymod.json");
                    if (!File.Exists(path)) return;
                    early = true;

                    try {
                        emi = JsonConvert.DeserializeObject<EarlyModInfo>(File.ReadAllText(path));
                    } catch (Exception ex) {
                        ScreenManager.Platform.Logger.Error(
                            "An exception was thrown trying to load the EarlyModInfo of \"{0}\":\n{1}",
                            new object[] {mod.Info.ModID, ex}
                        );
                        emi = new EarlyModInfo();
                    }
                    break;
                default:
                    if (mod.Assembly == null) return;

                    var attrs = mod.Assembly.GetCustomAttributes(typeof(EarlyModAttribute), false);
                    if (attrs.Length < 1) return;
                    early = true;

                    emi = ((EarlyModAttribute)attrs[0]).ToInfo();

                    return;
            }

            mod.Unpack(loader.UnpackPath);
            mod.LoadAssembly((ModCompilationContext)cctx.GetValue(loader), asmloader);

            if (mod.Assembly == null) return;

            try {
                systems = Enumerable.Where<Type>(mod.Assembly.GetTypes(), (Type type) => typeof(ModSystem).IsAssignableFrom(type) && !type.IsAbstract).ToArray();
            } catch (Exception ex) {
                ScreenManager.Platform.Logger.Error("EMTK: Exception thrown when obtaining assembly types of {0}: {1}, InnerException: {2}. Skipping Assembly", new object[] {
                    mod.Assembly.FullName,
                    ex,
                    ex.InnerException
                });
            }
        }
    }

    public class EarlyModInfo {
        public bool ReloadTitle = false;
        public double LoadOrder = 0.0;
    }

    public class EarlyModAttribute : Attribute {
        public bool ReloadTitle = false;
        public double LoadOrder = 0.0;

        public EarlyModAttribute(bool ReloadTitle = false, double LoadOrder = 0.0) {
            this.ReloadTitle = ReloadTitle;
            this.LoadOrder = LoadOrder;
        }

        public EarlyModInfo ToInfo() {
            return new EarlyModInfo {
                ReloadTitle = ReloadTitle,
                LoadOrder = LoadOrder
            };
        }
    }
}