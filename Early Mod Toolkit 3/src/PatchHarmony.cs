using System.Linq;

using HarmonyLib;

namespace EMTK {
    // HACK: This is some self-referential harmony patching of harmony itself, to prevent EMTK from being unpatched unintentionally.
    // By default, UnpatchAll() with no arguments unpatches every thing, including EMTK.
    // This changes UnpatchAll() to instead use the stored id as a default, which is a more commonly expected behavior.
    [HarmonyPatch]
    public static class PatchHarmony {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Harmony), "UnpatchAll")]
        public static bool UnpatchAll(Harmony __instance, string harmonyID = null) {
            string id = harmonyID ?? __instance.Id;
            bool IDCheck(Patch patchInfo) => patchInfo.owner == id;

			var originals = Harmony.GetAllPatchedMethods().ToList(); // keep as is to avoid "Collection was modified"
			foreach (var original in originals) {
				var hasBody = original.HasMethodBody();
				var info = Harmony.GetPatchInfo(original);
				if (hasBody) {
					info.Postfixes.DoIf(IDCheck, patchInfo => __instance.Unpatch(original, patchInfo.PatchMethod));
					info.Prefixes.DoIf(IDCheck, patchInfo => __instance.Unpatch(original, patchInfo.PatchMethod));
				}
				info.Transpilers.DoIf(IDCheck, patchInfo => __instance.Unpatch(original, patchInfo.PatchMethod));
				if (hasBody) info.Finalizers.DoIf(IDCheck, patchInfo => __instance.Unpatch(original, patchInfo.PatchMethod));
			}
            return false;
        }
    }
}