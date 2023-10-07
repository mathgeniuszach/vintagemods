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
    [HarmonyPatch]
    public static class TitleScreenPatches {
        public static object GetField<T>(T obj, string field) {
            return AccessTools.Field(typeof(T), field).GetValue(obj);
        }
        public static void SetField<T>(T obj, string field, object value) {
            AccessTools.Field(typeof(T), field).SetValue(obj, value);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ScreenManager), "StartMainMenu")]
        public static void StartMainMenu() {
            // AccessTools.Field(typeof(ScreenManager), "mainScreen").SetValue(sm, new NothingRightScreen(sm, null));
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiCompositeMainMenuLeft), "Compose")]
        public static bool ComposeLeft(GuiCompositeMainMenuLeft __instance) {
            ((ParticleRenderer2D)GetField(__instance, "particleSystem")).Compose("textures/particle/white-spec.png");

            ElementBounds sidebarBounds = new ElementBounds();

			sidebarBounds = new ElementBounds();
			sidebarBounds.horizontalSizing = ElementSizing.Fixed;
			sidebarBounds.verticalSizing = ElementSizing.Percentual;
			sidebarBounds.percentHeight = 1.0;
			sidebarBounds.fixedWidth = 1.0;

            SetField(__instance, "sidebarBounds", sidebarBounds);

            SetField(__instance, "renderStartMs", 0L);
            SetField(__instance, "curdx", 0f);
            SetField(__instance, "curdy", 0f);

            SetField(TitleScreenTweak.sm, "mainMenuComposer", TitleScreenTweak.CreateSidebar(__instance));

            string filename = TitleScreenTweak.GetBackgroundImage();

			IAsset asset = TitleScreenTweak.sm.GamePlatform.AssetManager.TryGet_BaseAssets(new AssetLocation(filename), true);
			BitmapRef bmp = (asset != null) ? asset.ToBitmap(TitleScreenTweak.sm.api) : null;
			if (bmp != null) {
				SetField(__instance, "bgtex", new LoadedTexture(TitleScreenTweak.sm.api, TitleScreenTweak.sm.GamePlatform.LoadTexture(bmp, true, 0, false), bmp.Width, bmp.Height));
				bmp.Dispose();
			} else {
				SetField(__instance, "bgtex", new LoadedTexture(TitleScreenTweak.sm.api, 0, 1, 1));
			}

			ClientSettings.Inst.AddWatcher<float>("guiScale", new OnSettingsChanged<float>(OnGuiScaleChanged));

			IAsset logo = ScreenManager.Platform.AssetManager.Get("textures/gui/logo.png");
			BitmapExternal bitmap = (BitmapExternal)ScreenManager.Platform.CreateBitmapFromPng(logo);
			ImageSurface logosurface = new ImageSurface(0, bitmap.Width, bitmap.Height);
			logosurface.Image(bitmap, 0, 0, bitmap.Width, bitmap.Height);
			bitmap.Dispose();

			LoadedTexture logoTexture = new LoadedTexture(TitleScreenTweak.sm.api);
			TitleScreenTweak.sm.api.Gui.LoadOrUpdateCairoTexture(logosurface, true, ref logoTexture);
            SetField(__instance, "logoTexture", logoTexture);

			logosurface.Dispose();
            return false;
        }

        // [HarmonyPrefix]
        // [HarmonyPatch(typeof(GuiCompositeMainMenuLeft), "Render")]
        // public static bool RenderLeft(GuiCompositeMainMenuLeft __instance) {
        //     return false;
        // }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiCompositeMainMenuLeft), "updateButtonActiveFlag")]
        public static bool updateButtonActiveFlag(GuiCompositeMainMenuLeft __instance) {
            return false;
        }

        public static void OnGuiScaleChanged(float newValue) {
			// sm.GamePlatform.GLDeleteTexture(sm.versionNumberTexture.TextureId);
			// sm.versionNumberTexture = sm.api.Gui.TextTexture.GenUnscaledTextTexture(GameVersion.LongGameVersion, CairoFont.WhiteDetailText(), null);
		}

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiScreenMainRight), "Compose")]
        public static bool ComposeRight(GuiScreenMainRight __instance) {
            SetField(__instance, "ElementComposer", 
                TitleScreenTweak.CreateTitleScreen()
            );

            LoadedTexture quoteTexture = TitleScreenTweak.sm.api.Gui.TextTexture.GenUnscaledTextTexture("„" + GetField(__instance, "quote") + "‟", CairoFont.WhiteDetailText().WithSlant(FontSlant.Italic), null);
            SetField(__instance, "quoteTexture", quoteTexture);

            LoadedTexture grayBg = new LoadedTexture(TitleScreenTweak.sm.api);
            ImageSurface surface = new ImageSurface(0, 1, 1);
            Context context = new Context(surface);
            context.SetSourceRGBA(0.0, 0.0, 0.0, 0.25);
            context.Paint();
            TitleScreenTweak.sm.api.Gui.LoadOrUpdateCairoTexture(surface, true, ref grayBg);
            SetField(__instance, "grayBg", grayBg);
            context.Dispose();
            surface.Dispose();
            
            return false;
        }

        // [HarmonyPrefix]
        // [HarmonyPatch(typeof(GuiScreenMainRight), "Render")]
        // public static bool RenderRight(GuiScreenMainRight __instance, float dt, long ellapsedMs, bool onlyBackground = false) {
        //     if (__instance.renderStartMs == 0L)
        //     {
        //         __instance.renderStartMs = ellapsedMs;
        //     }
        //     __instance.ensureLOHCompacted(dt);
        //     float windowSizeX = (float)__instance.ScreenManager.GamePlatform.WindowSize.Width;
        //     float windowSizeY = (float)__instance.ScreenManager.GamePlatform.WindowSize.Height;
        //     double x = __instance.ScreenManager.guiMainmenuLeft.Width + GuiElement.scaled(15.0);
        //     if (onlyBackground) return;
            
        //     __instance.ElementComposer.Render(dt);
        //     if (__instance.ElementComposer.MouseOverCursor != null)
        //     {
        //         __instance.FocusedMouseCursor = __instance.ElementComposer.MouseOverCursor;
        //     }
        //     __instance.ScreenManager.api.Render.Render2DTexturePremultipliedAlpha(this.grayBg.TextureId, this.ScreenManager.guiMainmenuLeft.Width, (double)(windowSizeY - (float)this.quoteTexture.Height) - GuiElement.scaled(10.0), (double)windowSizeX, (double)this.quoteTexture.Height + GuiElement.scaled(10.0), 50f, null);
        //     __instance.ScreenManager.RenderMainMenuParts(dt, this.ElementComposer.Bounds, this.ShowMainMenu, false);
        //     if (__instance.ScreenManager.mainMenuComposer.MouseOverCursor != null)
        //     {
        //         __instance.FocusedMouseCursor = this.ScreenManager.mainMenuComposer.MouseOverCursor;
        //     }
        //     __instance.ScreenManager.api.Render.Render2DTexturePremultipliedAlpha(this.quoteTexture.TextureId, x, (double)(windowSizeY - (float)this.quoteTexture.Height) - GuiElement.scaled(5.0), (double)this.quoteTexture.Width, (double)this.quoteTexture.Height, 50f, null);
        //     __instance.ElementComposer.PostRender(dt);
        //     __instance.ScreenManager.GamePlatform.UseMouseCursor((__instance.FocusedMouseCursor == null) ? "normal" : this.FocusedMouseCursor, false);
        //     return false;
        // }
    }
}