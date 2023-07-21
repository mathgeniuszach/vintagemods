using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

using HarmonyLib;

using Vintagestory.API.Config;
using Vintagestory.Client;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ModuleInitializerAttribute : Attribute { }
}

namespace EMTK {

    public static class Initializer {
        public static MethodInfo ClientProgramAssemblyResolve;

        public static string[] ASSEMBLY_PATHS;
        public static volatile string basepath;
        public static string modProfilePath;
        public static string activeModProfilePath;

        [ModuleInitializer]
        public static void Initialize() {
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(Initializer.AssemblyResolve);
        }

        [STAThread]
        public static void Main(string[] rawArgs) {
            if (RuntimeEnv.OS == OS.Windows) {
                typeof(ClientProgram).GetMethod("LoadNativeLibrariesWindows", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
			}
			
            typeof(ClientProgram).GetField("rawArgs", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, rawArgs);
			
            Console.WriteLine("EMTK: Patching for early mods!");
            #region

            while (basepath == null) Thread.Sleep(100);

            EMTK.version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            EMTK.version = EMTK.version.Substring(0, EMTK.version.LastIndexOf("."));
            EMTK.harmony = new Harmony("emtk");
            EMTK.harmony.PatchAll();

            PatchVTML.Patch();

            typeof(GamePaths).GetProperty("AssetsPath").GetSetMethod(true).Invoke(null, new[] {
                Path.Combine(basepath, "assets")
            });
            if (!Directory.Exists(GamePaths.AssetsPath)) {
                string error = "Error; \"assets\" folder not found! Make sure the executable is in the same folder as the \"assets\" folder and Vintagestory.exe, or install Vintagestory to %appdata%/Vintagestory on Windows or /usr/share/vintagestory on Linux.";
                Console.WriteLine(error);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ERROR.txt"), error);
            }

            GamePaths.DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData");
            modProfilePath = Path.Combine(GamePaths.DataPath, "ModProfiles");
            activeModProfilePath = Path.Combine(modProfilePath, "ActiveProfile");
            
            #endregion
            Console.WriteLine("EMTK: Patching complete!");

            Console.WriteLine("EMTK: Searching ModDB for mods!");
            ModAPI.CheckEMTKUpdate();
            new Thread(() => {ModAPI.GetMods();}).Start();
			
			new ClientProgram(rawArgs);

        //     string imgDir = Path.Combine(GamePaths.Cache, "images");
        //     if (Directory.GetFiles(imgDir).Length > 50) {
        //         Directory.Delete(imgDir, true);
        //         Directory.CreateDirectory(imgDir);
        //     }
        }

        public static Assembly AssemblyResolve(object sender, ResolveEventArgs args) {
            if (ASSEMBLY_PATHS == null) {
                string basedir = AppDomain.CurrentDomain.BaseDirectory;
                string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string vsdir = Path.DirectorySeparatorChar == '\\' ? Path.Combine(appdata, "Vintagestory/") : "/usr/share/vintagestory/";

                if (File.Exists(Path.Combine(basedir, "Vintagestory.exe"))) {
                    ASSEMBLY_PATHS = new[] {
                        basedir,
                        Path.Combine(basedir, "Lib"),
                        Path.Combine(basedir, "Mods"),
                        Path.Combine(appdata, "VintagestoryData", "Mods")
                    };
                    basepath = basedir;
                } else {
                    ASSEMBLY_PATHS = new[] {
                        basedir,
                        Path.Combine(basedir, "Lib"),
                        Path.Combine(basedir, "Mods"),
                        vsdir,
                        Path.Combine(vsdir, "Lib"),
                        Path.Combine(vsdir, "Mods"),
                        Path.Combine(appdata, "VintagestoryData", "Mods")
                    };
                    basepath = vsdir;
                }
            }

            string dll = new AssemblyName(args.Name).Name + ".dll";
            string exe = new AssemblyName(args.Name).Name + ".exe";
			foreach (string folder in ASSEMBLY_PATHS) {
				string dllPath = Path.Combine(folder, dll);
                string exePath = Path.Combine(folder, exe);
                string path = File.Exists(dllPath) ? dllPath : File.Exists(exePath) ? exePath : null;
				if (path != null) {
					try {
						return Assembly.LoadFrom(path);
					} catch (Exception ex) {
						throw new Exception("Failed to load assembly '" + args.Name + "' from '" + path + "'", ex);
					}
				}
			}

			string msg = "Client side assembly resolver did not find the assembly in the binary path, the lib path or the mods path. Tested for the following paths: {0}";
			string paths = string.Concat(Enumerable.Select<string, string>(ASSEMBLY_PATHS, (string path) => "\n  " + path));

            Console.WriteLine(msg, paths);
			return null;
        }
    }
}