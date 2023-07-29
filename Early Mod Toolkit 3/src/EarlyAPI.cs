using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;

using HarmonyLib;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client;
using Vintagestory.Client.Gui;

// Patches to add needed functionality to the main menu api.
namespace EMTK {
    [HarmonyPatch]
    public static class EarlyAPI {
        // Clipboard on linux still doesn't work, even after this.
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(MainMenuInputAPI), "ClipboardText", MethodType.Getter)]
        public static IEnumerable<CodeInstruction> PatchClipboardGet(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            yield return CodeInstruction.Call(typeof(EarlyAPI), "ClipboardGet");
            yield return new CodeInstruction(OpCodes.Ret, null);
        }
        public static string ClipboardGet() {
            return ScreenManager.Platform.XPlatInterface.GetClipboardText();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(MainMenuInputAPI), "ClipboardText", MethodType.Setter)]
        public static IEnumerable<CodeInstruction> PatchClipboardSet(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            yield return new CodeInstruction(OpCodes.Ldarg_1, null);
            yield return CodeInstruction.Call(typeof(EarlyAPI), "ClipboardSet");
            yield return new CodeInstruction(OpCodes.Ret, null);
        }
        public static void ClipboardSet(string v) {
            ScreenManager.Platform.XPlatInterface.SetClipboardText(v);
        }
        
        // These patchers do not work due to templates.

        // [HarmonyTranspiler]
        // [HarmonyPatch(typeof(MainMenuAPI), "StoreModConfig")]
        // public static IEnumerable<CodeInstruction> PatchStoreModConfig(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        //     yield return new CodeInstruction(OpCodes.Ldarg_1, null);
        //     yield return new CodeInstruction(OpCodes.Ldarg_2, null);
        //     yield return CodeInstruction.Call(typeof(PatchMenuAPI), "StoreModConfig");
        //     yield return new CodeInstruction(OpCodes.Ret, null);
        // }
        public static void StoreModConfig<T>(T jsonSerializeableData, string filename) {
			FileInfo fileInfo = new FileInfo(Path.Combine(GamePaths.ModConfig, filename));
			GamePaths.EnsurePathExists(fileInfo.Directory.FullName);
			string json = JsonConvert.SerializeObject(jsonSerializeableData, Formatting.Indented);
			File.WriteAllText(fileInfo.FullName, json);
		}
        public static void StoreModConfig(JsonObject jobj, string filename) {
            FileInfo fileInfo = new FileInfo(Path.Combine(GamePaths.ModConfig, filename));
			GamePaths.EnsurePathExists(fileInfo.Directory.FullName);
			File.WriteAllText(fileInfo.FullName, jobj.Token.ToString());
        }

        // [HarmonyTranspiler]
        // [HarmonyPatch(typeof(MainMenuAPI), "LoadModConfig", MethodType.)]
        // public static IEnumerable<CodeInstruction> PatchLoadModConfig(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        //     yield return new CodeInstruction(OpCodes.Ldarg_1, null);
        //     yield return CodeInstruction.Call(typeof(PatchMenuAPI), "LoadModConfig");
        //     yield return new CodeInstruction(OpCodes.Ret, null);
        // }
        public static T LoadModConfig<T>(string filename)
		{
			string path = Path.Combine(GamePaths.ModConfig, filename);
			if (File.Exists(path)) return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
			return default(T);
		}
        public static JsonObject LoadModConfig(string filename) {
            string path = Path.Combine(GamePaths.ModConfig, filename);
			if (File.Exists(path)) return new JsonObject(JObject.Parse(File.ReadAllText(path)));
			return null;
        }
    }
}