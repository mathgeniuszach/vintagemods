using System;
using System.Reflection;
using Cairo;
using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace TitleScreenTweak {
    public class TitleScreenTweak : ModSystem {
        public static ScreenManager sm;
        public static Harmony harmony;

        public override bool ShouldLoad(EnumAppSide side) {
            return false;
        }
        
        public static void EarlyLoad(ModContainer mod, ScreenManager sm) {
            TitleScreenTweak.sm = sm;
            harmony = new Harmony("titlescreentweak");
            harmony.PatchAll();
        }
        
        public static void EarlyUnload() {
            harmony.UnpatchAll("titlescreentweak");
        }
        
        public static string GetBackgroundImage() {
			int day = DateTime.Now.Day;
            int month = DateTime.Now.Month;

			if (month == 12 /*&& day >= 20 && day <= 30*/) {
                return "textures/gui/backgrounds/mainmenu-xmas.png";
            } else if (month == 10 && day > 18 || month == 11 && day < 12) {
                return "textures/gui/backgrounds/mainmenu-halloween.png";
            }

            int numScreenshots = 10;
            int screenshot = (int)(1 + ((long)(DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds / 604800L) % numScreenshots);
			return "textures/gui/backgrounds/mainmenu" + screenshot.ToString() + ".png";
        }

        public static GuiComposer CreateTitleScreen() {
            return ScreenManager.GuiComposers.Create("welcomedialog", ElementBounds.Fill)
                .AddInteractiveElement(new ImageButton(
                    TitleScreenTweak.sm.api, "textures/gui/singleplayernormal.png", "textures/gui/singleplayerhover.png", null,
                    () => {TitleScreenTweak.sm.LoadAndCacheScreen(typeof(GuiScreenSingleplayer)); return true;},
                    ElementBounds.Fixed(EnumDialogArea.CenterBottom, -200.0, -100.0, 300.0, 301.0)
                ))
                .AddInteractiveElement(new ImageButton(
                    TitleScreenTweak.sm.api, "textures/gui/multiplayernormal.png", "textures/gui/multiplayerhover.png", null,
                    () => {TitleScreenTweak.sm.LoadAndCacheScreen(typeof(GuiScreenMultiplayer)); return true;},
                    ElementBounds.Fixed(EnumDialogArea.CenterBottom, 200.0, -100.0, 300.0, 301.0)
                ))
                .Compose(true);
        }

        public static GuiComposer CreateSidebar(GuiCompositeMainMenuLeft __instance) {
            return ScreenManager.GuiComposers.Create("compositemainmenu", ElementBounds.Fill)
                .AddButton(
                    "Home", () => {TitleScreenTweak.sm.LoadScreen((GuiScreen)TitleScreenPatches.GetField(TitleScreenTweak.sm, "mainScreen")); return true;},
                    ElementBounds.Fixed(EnumDialogArea.RightBottom, -10.0, -140.0, 100.0, 30.0)
                ).AddButton(
                    "Quit", () => {__instance.OnQuit(); return true;},
                    ElementBounds.Fixed(EnumDialogArea.RightBottom, -10.0, -100.0, 100.0, 30.0)
                )
                .Compose(true);
        }
    }

}