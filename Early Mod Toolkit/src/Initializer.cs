using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading;

using HarmonyLib;

using Vintagestory.API.Config;
using Vintagestory.Client;

[assembly: CompilationRelaxations(CompilationRelaxations.NoStringInterning)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: ComVisible(false)]
[assembly: Guid("782e0587-e211-4a27-a5ac-3ecb057860dc")]
[assembly: SecurityPermission((SecurityAction)8, SkipVerification = true)]

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

            GamePaths.DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData");
            modProfilePath = Path.Combine(GamePaths.DataPath, "ModProfiles");
            activeModProfilePath = Path.Combine(modProfilePath, "ActiveProfile");
            
            #endregion
            Console.WriteLine("EMTK: Patching complete!");

            if (Directory.Exists(Path.Combine(GamePaths.Cache, "trash"))) {
                Console.WriteLine("EMTK: Trashing pseudo trashed items");
                Directory.Delete(Path.Combine(GamePaths.Cache, "trash"), true);
            }

            Console.WriteLine("EMTK: Searching ModDB for mods in another thread!");
            new Thread(() => {ModAPI.CheckEMTKUpdate();}).Start();
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

                string[] potentialPaths;
                if (Path.DirectorySeparatorChar == '\\') {
                    potentialPaths = new[] {
                        basedir,
                        Path.GetDirectoryName(basedir),
                        Path.Combine(appdata, "Vintagestory"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vintagestory"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Vintagestory")
                    };
                } else {
                    potentialPaths = new[] {
                        basedir,
                        Path.GetDirectoryName(basedir),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ApplicationData/vintagestory"),
                        "/usr/share/vintagestory",
                        "/opt/vintagestory"
                    };
                }

                foreach (string path in potentialPaths) {
                    // Vintagestory.dll is present in .NET7 builds. We don't want to launch on these.
                    if (File.Exists(Path.Combine(path, "Vintagestory.exe")) && !File.Exists(Path.Combine(path, "Vintagestory.dll"))) {
                        ASSEMBLY_PATHS = new[] {
                            path,
                            Path.Combine(path, "Lib"),
                            Path.Combine(path, "Mods"),
                            Path.Combine(appdata, "VintagestoryData", "Mods")
                        };
                        basepath = path;
                        break;
                    }
                }

                if (ASSEMBLY_PATHS == null) {
                    string error =
                        "Error; Vintagestory.exe not found!\r\n" +
                        "Make sure you're using the right .NET version (.NET4 or .NET7).\r\n" +
                        "Otherwise, place the executable in the same folder where you installed Vintagestory.exe, or " + 
                        "install Vintagestory to one of \"%AppData%\\Vintagestory\", \"C:\\Program Files\\Vintagestory\", \"C:\\Program Files (x86)\\Vintagestory\" on Windows or /usr/share/vintagestory on Linux.";
                    Console.WriteLine(error);
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ERROR.txt"), error);
                    throw new Exception("Failed to locate vintagestory executable.");
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