using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace EMTK {
    [HarmonyPatch]
    public static class PatchProfiles {
        public static GuiScreenMods gsm;
        public static List<string> profiles = new List<string>();
        public static string activeProfile;

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GuiElementModCell), "Compose")]
        public static IEnumerable<CodeInstruction> Compose(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            return new CodeMatcher(instructions, generator)
                .MatchStartForward(new CodeMatch(OpCodes.Conv_I4))
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Pop),
                    new CodeInstruction(OpCodes.Ldc_R8, 64.0),
                    CodeInstruction.Call(typeof(GuiElement), "scaled")
                )
                .End()
                .MatchStartBackwards(new CodeMatch(instr => instr.opcode == OpCodes.Brfalse))
                .Advance(-2)
                .SetAndAdvance(OpCodes.Nop, null)
                .SetAndAdvance(OpCodes.Ldc_I4_1, null)
                .InstructionEnumeration();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GuiElementModCell), "UpdateCellHeight")]
        public static IEnumerable<CodeInstruction> UpdateCellHeight(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            return new CodeMatcher(instructions, generator)
                .MatchStartForward(new CodeMatch(OpCodes.Conv_I4))
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Pop),
                    new CodeInstruction(OpCodes.Ldc_R8, 69.0), // Extra 5 pixels for the image
                    CodeInstruction.Call(typeof(GuiElement), "scaled")
                )
                .InstructionEnumeration();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiScreenMods), "OnClickCellLeft")]
        public static bool OnClickCellLeft(GuiScreen __instance, int cellIndex) {
            GuiElementModCell guicell = (GuiElementModCell) __instance.ElementComposer.GetCellList<ModCellEntry>("modstable").elementCells[cellIndex];
            EMTK.sm.LoadScreen(new GuiScreenModInfo(
                guicell.cell.Mod.Info.ModID,
                guicell.cell.Mod,
                EMTK.sm, gsm
            ));
            return false; // Not technically necessary
        }

        public static bool fromMouseDown = false;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiElementModCell), "OnMouseUpOnElement")]
        public static bool OnMouseUpOnElement(MouseEvent args) {
            if (fromMouseDown) {
                fromMouseDown = false;
                return true;
            }
            return false;
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiElementModCell), "OnMouseDownOnElement")]
        public static bool OnMouseDownOnElement(GuiElementModCell __instance, MouseEvent args, int elementIndex) {
            fromMouseDown = true;
            __instance.OnMouseUpOnElement(args, elementIndex);
            return false; // Not technically necessary
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GuiScreenMods), "InitGui")]
        public static IEnumerable<CodeInstruction> InitGui(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            return new CodeMatcher(instructions, generator)
                .MatchStartForward(new CodeMatch(OpCodes.Ldstr, "Installed mods"))
                .RemoveInstruction()
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    CodeInstruction.Call(typeof(PatchProfiles), "modModScreen"),
                    new CodeInstruction(OpCodes.Ldstr, "")
                )
                .MatchStartForward(new CodeMatch(OpCodes.Ldc_R8, -150.0))
                .SetOperandAndAdvance(-140.0)
                .InstructionEnumeration();
        }

        public static GuiComposer modModScreen(GuiComposer composer, GuiScreenMods gui) {
            gsm = gui;
            if (!Directory.Exists(Initializer.modProfilePath)) Directory.CreateDirectory(Initializer.modProfilePath);

            profiles.Clear();
            if (File.Exists(Initializer.activeModProfilePath)) {
                activeProfile = File.ReadAllText(Initializer.activeModProfilePath);
            } else {
                activeProfile = "Default Profile";
                File.WriteAllText(Initializer.activeModProfilePath, activeProfile);
            }

            profiles.Add(activeProfile);
            foreach (string dir in Directory.EnumerateDirectories(Initializer.modProfilePath)) {
                profiles.Add(Path.GetFileName(dir));
            }
            profiles.Sort();

            string[] names = profiles.ToArray();

            return composer.AddRichtext(
                "Mods (<a href='https://mods.vintagestory.at/emtk'>EMTK v" + EMTK.version + "</a>" + (EMTK.updateAvailable ? " Update!!!" : "") + ")",
                CairoFont.WhiteSmallishText(),
                ElementBounds.Fixed(EnumDialogArea.LeftTop, 0.0, 0.0, 690.0, 0.0),
                null
            // ).AddIconButton(
            //     "copy", copyModProfile,
            //     ElementBounds.Fixed(EnumDialogArea.RightTop, -451.0, -1.0, 30.0, 30.0)
            // ).AddIconButton(
            //     "import", pasteModProfile,
            //     ElementBounds.Fixed(EnumDialogArea.RightTop, -421.0, -1.0, 30.0, 30.0)
            ).AddIconButton(
                "plus", addModProfile,
                ElementBounds.Fixed(EnumDialogArea.RightTop, -381.0, -1.0, 30.0, 30.0)
            ).AddIconButton(
                "eraser", deleteModProfile,
                ElementBounds.Fixed(EnumDialogArea.RightTop, -351.0, -1.0, 30.0, 30.0)
            ).AddIconButton(
                "paintbrush", renameModProfile,
                ElementBounds.Fixed(EnumDialogArea.RightTop, -321.0, -1.0, 30.0, 30.0)
            ).AddDropDown(
                names, names, profiles.BinarySearch(activeProfile), changeModProfile,
                ElementBounds.Fixed(EnumDialogArea.RightTop, -16.0, 0.0, 300.0, 30.0)
            ).AddSmallButton(
                Lang.Get("Browse..."), browseMods,
                ElementBounds.Fixed(EnumDialogArea.RightBottom, -417.0, 12.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
            ).AddSmallButton(
                Lang.Get("Update All"), updateMods,
                ElementBounds.Fixed(EnumDialogArea.RightBottom, -310.0, 12.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
            );
        }

        public static bool browseMods() {
            EMTK.sm.LoadScreen(new GuiScreenBrowseMods(EMTK.sm, gsm));
            return true;
        }

        public static bool updateMods() {
            List<ModContainer> mods = (List<ModContainer>) AccessTools.Field(typeof(ScreenManager), "allMods").GetValue(EMTK.sm);
            List<string> modUpdates = ModAPI.GetUpdates(mods);

            if (modUpdates == null) {
                EMTK.sm.LoadScreen(new GuiScreenInfo(Lang.Get("Could not check for mod updates, try again later"), () => EMTK.sm.LoadScreen(gsm), EMTK.sm, gsm));
            } else if (modUpdates.Count <= 0) {
                EMTK.sm.LoadScreen(new GuiScreenInfo(Lang.Get("All mods are up to date!"), () => EMTK.sm.LoadScreen(gsm), EMTK.sm, gsm));
            } else {
                EMTK.sm.LoadScreen(new GuiScreenUpdateMods(modUpdates, EMTK.sm, gsm));
            }

            return true;
        }

        public static string cleanName(string value) {
            string nvalue = value;

            // Remove illegal characters in a filename
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) {
                nvalue = nvalue.Replace(c, '_');
            }

            // Ensure uniqueness
            while (profiles.Contains(nvalue)) nvalue += '_';

            return nvalue;
        }

        public static void copyModProfile(bool obj) {
            // Not implemented on linux I guess? Oh well, screw you I guess.
            EMTK.sm.api.Input.ClipboardText = String.Join(", ", EMTK.loadedMods
                .Where(mod => mod.Value?.Info?.Version != null)
                .Select(mod => {
                    return mod.Key + "@" + mod.Value.Info.Version.ToString();
                }));
        }
        public static void pasteModProfile(bool obj) {}
        
        public static void renameModProfile(bool obj) {
            EMTK.sm.LoadScreen(new GuiScreenEntry(
                activeProfile, new Action<bool, string>(renameFinished), EMTK.sm, gsm
            ));
        }
        public static void renameFinished(bool confirmed, string value) {
            EMTK.sm.LoadScreen(gsm);
            if (!confirmed) return;

            string nvalue = cleanName(value);
            File.WriteAllText(Initializer.activeModProfilePath, nvalue);

            // FIXME: Overkill, reload not required here, the gui just needs to be reloaded
            AccessTools.Method(typeof(GuiScreenMods), "OnReloadMods").Invoke(gsm, null);
        }

        public static void addModProfile(bool obj) {
            EMTK.sm.LoadScreen(new GuiScreenEntry(
                "", new Action<bool, string>(addFinished), EMTK.sm, gsm
            ));
        }
        public static void addFinished(bool confirmed, string value) {
            EMTK.sm.LoadScreen(gsm);
            if (!confirmed) return;

            string nvalue = cleanName(value);
            Directory.CreateDirectory(Path.Combine(Initializer.modProfilePath, nvalue));

            changeModProfile(nvalue, true);
        }

        public static void deleteModProfile(bool obj) {
            EMTK.sm.LoadScreen((GuiScreen)Activator.CreateInstance(
                AccessTools.TypeByName("GuiScreenConfirmAction"),
                new object[] {
                    Lang.Get("Delete") + " '" + activeProfile + "'?",
                    new Action<bool>(deleteFinished), EMTK.sm, gsm, false
                }
            ));
        }
        public static void deleteFinished(bool confirmed) {
            EMTK.sm.LoadScreen(gsm);
            if (!confirmed) return;

            // Delete profile
            Directory.Delete(GamePaths.DataPathMods, true);
            ClientSettings.DisabledMods.Clear();

            if (profiles.Count > 1) {
                // Swap to an existing profile if possible
                var code = profiles[0] == activeProfile ? profiles[1] : profiles[0];
                Directory.Move(Path.Combine(Initializer.modProfilePath, code), GamePaths.DataPathMods);
                File.WriteAllText(Initializer.activeModProfilePath, code);
                if (File.Exists(Path.Combine(GamePaths.DataPathMods, "Disabled"))) {
                    ClientSettings.DisabledMods.AddRange(File.ReadAllLines(Path.Combine(GamePaths.DataPathMods, "Disabled")));
                    File.Delete(Path.Combine(GamePaths.DataPathMods, "Disabled"));
                }
            } else {
                // Otherwise delete the active profile file
                File.Delete(Initializer.activeModProfilePath);
                // Remake the empty mod folder
                Directory.CreateDirectory(GamePaths.DataPathMods);
            }

            ClientSettings.Inst.Save(true);

            AccessTools.Method(typeof(GuiScreenMods), "OnReloadMods").Invoke(gsm, null);
        }

        public static void changeModProfile(string code, bool selected) {
            // Don't do anything if we didn't really change the profile
            if (code == activeProfile) return;

            // Hotswap mod folder
            Directory.Move(GamePaths.DataPathMods, Path.Combine(Initializer.modProfilePath, activeProfile));
            Directory.Move(Path.Combine(Initializer.modProfilePath, code), GamePaths.DataPathMods);

            // Change text in the active mod profile file
            File.WriteAllText(Initializer.activeModProfilePath, code);

            // Save client disabled mods
            File.WriteAllLines(Path.Combine(Initializer.modProfilePath, activeProfile, "Disabled"), ClientSettings.DisabledMods);

            // Load client disabled mods
            ClientSettings.DisabledMods.Clear();
            if (File.Exists(Path.Combine(GamePaths.DataPathMods, "Disabled"))) {
                ClientSettings.DisabledMods.AddRange(File.ReadAllLines(Path.Combine(GamePaths.DataPathMods, "Disabled")));
                File.Delete(Path.Combine(GamePaths.DataPathMods, "Disabled"));
            }
            ClientSettings.Inst.Save(true);

            AccessTools.Method(typeof(GuiScreenMods), "OnReloadMods").Invoke(gsm, null);
        }
    }
}