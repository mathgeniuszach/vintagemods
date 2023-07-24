using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using HarmonyLib;
using ProperVersion;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace EMTK {
    public class GuiScreenModInfo : GuiScreen {
        public static readonly MethodInfo invalidateParentScreen = AccessTools.Method(typeof(GuiScreenMods), "invalidate");

        public string modid;
        public ModContainer lmod;
        public APIStatusModInfo smod;
        public TextDrawUtil textUtil;

        public GuiScreen parentScreen;

        public ElementBounds childElementBounds;

        public BitmapExternal icon;
        public Size2i iconSize;

        public string selectedVersion;

		public override bool ShouldDisposePreviousScreen {
			get {
				return false;
			}
		}

        public GuiScreenModInfo(string modid, ModContainer lmod, ScreenManager screenManager, GuiScreen parentScreen) : base(screenManager, parentScreen) {
            this.ShowMainMenu = true;
            this.parentScreen = parentScreen;

            textUtil = this.ScreenManager.api.Gui.Text;

            InitMeta(modid, lmod);
            
            // Build UI. Some parts are conditional on whether or not the mod is local or not
            InitGui();
			screenManager.GamePlatform.WindowResized += (int w, int h) => {invalidate();};
			ClientSettings.Inst.AddWatcher<float>("guiScale", (float s) => {invalidate();});
		}

        public void InitMeta(string modid, ModContainer lmod) {
            this.modid = modid.ToLower();
            this.lmod = lmod;

            while (!ModAPI.modsQueryFinished) Thread.Sleep(100);

            Thread iconThread = new Thread(() => {
                if (ModAPI.modListSummary.ContainsKey(this.modid) && ModAPI.modListSummary[this.modid].logo != null) {
                    this.icon = ModAPI.GetImage("https://mods.vintagestory.at/"+ModAPI.modListSummary[this.modid].logo);
                } else if (lmod != null) {
                    this.icon = lmod.Icon;
                } else {
                    this.icon = new BitmapExternal(Path.Combine(GamePaths.AssetsPath, "game/textures/gui/3rdpartymodicon.png"));
                }
            });
            Thread modAPIThread = new Thread(() => {
                this.smod = ModAPI.GetMod(modid);
            });

            iconThread.Start();
            modAPIThread.Start();
            iconThread.Join();
            modAPIThread.Join();
        }

        public void InitGui() {
            CairoFont titleFont = CairoFont.WhiteMediumText();
            CairoFont smallFont = CairoFont.WhiteSmallishText();

            if (lmod == null && smod.statuscode != "200") {
                // In this if statement, we failed a web request on a mod we don't locally have.
                // Therefore, we have absolutely no clue what the mod is, other than its modid.
                // So we build a placeholder ui, with an error code and the mod id.

                string message = 
                    smod.statuscode == "404"
                        ? "No mod with id \"" + modid + "\" exists..."
                        : "Can't get mod with id \"" + modid + "\"; " + "Error " + smod.statuscode;

                this.ElementComposer = base.dialogBase("mainmenu-modinfo", -1.0, -1.0)
                    .AddInset(ElementBoundsPlus.Dynamic(-20.0, -65.0, 1.0, 1.0, 10.0, 10.0))
                    .BeginChildElements(ElementBoundsPlus.Dynamic(-40.0, -65.0, 1.0, 1.0, 20.0, 10.0))
                        .AddInset(ElementBounds.Fill)
                        .AddStaticText(message, smallFont, ElementBoundsPlus.Dynamic(pwidth: 1.0, height: 30.0))
                    .EndChildElements()
                    .AddButton(
                        Lang.Get("Back"), () => {EMTK.sm.LoadScreen(parentScreen); return true;},
                        ElementBounds.Fixed(EnumDialogArea.RightBottom, 0.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                    )
                    .Compose();

                return;
            }

            APIModInfo mod = smod.mod;
            APIModRelease latestRelease = mod != null && mod.releases != null && mod.releases.Length > 0 ? mod.releases[0] : null;
            bool outOfDate = false;
            outOfDate = lmod != null && mod != null && latestRelease != null && EMTK.ParseVersion(modid, latestRelease.modversion) > EMTK.ParseVersion(modid, lmod.Info.Version);

            Size2d box = ElementBoundsPlus.GetBoxSize();

            // Collect common mod info with lmod and mod
            // Other info is conditional to being local or online
            string title = lmod != null ? lmod.Info.Name : mod.name;
            if (lmod != null) {
                if (outOfDate) {
                    title += " (*" + lmod.Info.Version + "*)";
                } else {
                    title += " (" + lmod.Info.Version + ")";
                }
            }

            string authors =
                mod != null && (lmod == null || lmod.Info == null || lmod.Info.Authors.Count <= 1)
                    ? mod.author
                    : String.Join(", ", lmod.Info.Authors);
            // string authors = lmod != null ? String.Join(", ", lmod.Info.Authors) : mod.author;

            string side =
                mod != null
                    ? char.ToUpper(mod.side[0]) + mod.side.Substring(1)
                    : (lmod.Info.Side.IsClient() ? "Client" : lmod.Info.Side.IsServer() ? "Server" : lmod.Info.Side.IsUniversal() ? "Both" : "Unknown");

            string desc = mod?.text ?? lmod?.Info?.Description ?? "";

            this.ElementComposer = base.dialogBase("mainmenu-modinfo", -1.0, -1.0)
                .AddStaticText(title, titleFont, EnumTextOrientation.Center, ElementBoundsPlus.Dynamic(pwidth: 1.0, height: 30.0))
                .AddStaticText(authors, smallFont, EnumTextOrientation.Center, ElementBoundsPlus.Dynamic(pwidth: 1.0, height: 20.0, y: 40.0));
            
            // this.ElementComposer.GetScrollbar("scrollbar").SetHeights((float)this.clippingBounds.fixedHeight, (float)this.listBounds.fixedHeight);
            
            if (mod != null) {
                this.ElementComposer
                    .AddStaticText(mod.downloads.ToString(), smallFont, EnumTextOrientation.Right, ElementBounds.Fixed(EnumDialogArea.RightTop, -25.0, 8.0, 300.0, 20.0))
                    .AddRichtext("<icon name=move>", smallFont, ElementBounds.Fixed(EnumDialogArea.RightTop, 0.0, 12.0, 20.0, 20.0))
                    .AddStaticText(mod.follows.ToString(), smallFont, EnumTextOrientation.Right, ElementBounds.Fixed(EnumDialogArea.RightTop, -25.0, 33.0, 300.0, 20.0))
                    .AddRichtext("<icon name=wpStar2>", smallFont, ElementBounds.Fixed(EnumDialogArea.RightTop, 0.0, 37.0, 20.0, 20.0));
                
                if (mod.releases != null && mod.releases.Length > 0) {
                    var versions = mod.releases
                        .Where(release => release?.modversion != null)
                        .Select(release => release.modversion.Trim())
                        .ToArray();

                    selectedVersion = versions[0];
                    this.ElementComposer
                        .AddDropDown(versions, versions, 0, SelectVersion, ElementBounds.Fixed(0.0, 5.0, 100.0, 30.0), smallFont)
                        .AddSmallButton(lmod == null ? "Install" : "Reinstall", InstallMod, ElementBounds.Fixed(0.0, 35.0, 100.0, 30.0));
                }
            }

            childElementBounds = ElementBounds.FixedPos(EnumDialogArea.LeftTop, 10.0, 80.0).WithFixedWidth(box.Width-60.0);

            this.ElementComposer
                .AddInset(ElementBoundsPlus.Dynamic(-40.0, -135.0, 1.0, 1.0, 10.0, 80.0))
                .AddVerticalScrollbar(
                    (v) => {
                        childElementBounds.fixedY = -v;
                        childElementBounds.CalcWorldBounds();
                    },
                    ElementStdBounds.VerticalScrollbar(ElementBounds.Fixed(20.0, 80.0, box.Width-50.0, box.Height-135.0)), "scrollbar")
                .BeginClip(ElementBoundsPlus.Dynamic(-60.0, -135.0, 1.0, 1.0, 20.0, 80.0))
                .BeginChildElements(childElementBounds);
            
            if (this.icon != null) {
                // Make sure the icon is sized to have a height less than or equal to 200 and a width less than half the inner box - 5
                double sizeRatio = Math.Min(200.0 / (double)this.icon.Height, ((box.Width-100.0) / 2.0) / (double)this.icon.Width);

                iconSize = new Size2i((int)(this.icon.Width*sizeRatio), (int)(this.icon.Height*sizeRatio));
                this.ElementComposer.AddDynamicCustomDraw(ElementBoundsPlus.Dynamic(iconSize.Width, iconSize.Height, px: 0.25, x: -(iconSize.Width/2.0)-10.0, y: 10.0 /*+ (200.0-iconSize.Height) / 2.0*/), DrawIcon);
            } else {
                iconSize = new Size2i(200, 200);
            }
            
            var fields = new Dictionary<string, string>() {
                {"ModID", modid},
                {"Side", side},
            };
            if (lmod != null) {
                fields.Add("Type", lmod.Info.Type.ToString());
            }
            if (mod != null) {
                fields.Add("Tags", String.Join(", ", mod.tags));
                fields.Add("Created", mod.created.ToLocalTime().ToString("yyyy MMM d, h:mm tt"));

                var lmt = latestRelease != null ? latestRelease.created : mod.lastmodified;
                fields.Add("Updated", lmt.ToString("yyyy MMM d, h:mm tt"));
            }

            double h = 5.0;
            double rwidth = box.Width / 2.0 - 110.0;

            foreach (var kvp in fields) {
                this.ElementComposer
                    .AddRichtext(
                        "<font align='right'>" + kvp.Key + ":</font>", smallFont,
                        ElementBounds.Fixed((box.Width-60.0)/2.0-100.0, h, 170.0, 20.0))
                    .AddRichtext(
                        kvp.Value, smallFont,
                        ElementBounds.Fixed((box.Width-60.0)/2.0+80.0, h, (box.Width-60.0)/2.0-80.0, 300.0));
                
                h += textUtil.GetMultilineTextHeight(smallFont, kvp.Value, GuiElement.scaled(rwidth), EnumLinebreakBehavior.Default) / (double)RuntimeEnv.GUIScale;
            }
            h = Math.Max(h, 20.0 + iconSize.Height);

            PatchVTML.cleanText = true;
            PatchVTML.tableWidth = (int)((box.Width - 70.0) / 12.0); // FIXME: This calculation needs to be done everywhere, not just here
            try {
                this.ElementComposer.AddRichtext(desc, smallFont, ElementBounds.Fixed(0.0, h, box.Width - 70.0, box.Height - h), "description");
            } catch (Exception ex) {
                ScreenManager.Platform.Logger.Error("Failure to display mod description of '{0}': {1}", modid, ex);
                this.ElementComposer.AddRichtext(
                    "There was a problem displaying the description from this mod. Report your log on <a href='https://discord.gg/pBFqEcXvW5'>Discord</a>!",
                    smallFont, ElementBounds.Fixed(0.0, h, box.Width - 70.0, box.Height - h), "description");
            }
            this.ElementComposer
                .EndClip().EndChildElements()
                .AddButton(
                    Lang.Get("Back"), () => {EMTK.sm.LoadScreen(parentScreen); return true;},
                    ElementBounds.Fixed(EnumDialogArea.RightBottom, 0.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                );
            
            if (mod != null) {
                this.ElementComposer
                    .AddButton(
                        "Open ModDB Page", () => {ScreenManager.api.Gui.OpenLink("https://mods.vintagestory.at/"+mod.urlalias); return true;},
                        ElementBounds.Fixed(EnumDialogArea.LeftBottom, 0.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                    );
            }
            if (lmod != null) {
                this.ElementComposer
                    .AddButton(
                        "Uninstall", RemoveMod,
                        ElementBounds.Fixed(EnumDialogArea.RightBottom, -90.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                    );
            }

            
            this.ElementComposer.Compose();

            this.ElementComposer.GetScrollbar("scrollbar").SetHeights(
                (float)(box.Height - 135.0),
                (float)(h + this.ElementComposer.GetRichtext("description").Bounds.fixedHeight + 20.0)
            );
        }

        private void SelectVersion(string code, bool selected) {
            selectedVersion = code;
        }

        public bool InstallMod() {
            // Download new mod
            string cfile = null;
            string filename = null;
            foreach (APIModRelease release in smod.mod.releases) {
                if (release.modversion != selectedVersion) continue;

                cfile = ModAPI.GetAsset("https://mods.vintagestory.at/"+release.mainfile);
                filename = release.filename;
                if (cfile == null) return true;

                break;
            }
            if (cfile == null) return true; // Should never happen, but just in case

            try {
                if (lmod != null) {
                    // Remove existing mod
                    if (Directory.Exists(lmod.SourcePath)) {
                        Directory.Delete(lmod.SourcePath, true);
                    } else {
                        File.Delete(lmod.SourcePath);
                    }
                }

                // Install new mod
                File.Move(cfile, Path.Combine(GamePaths.DataPathMods, filename));

                // Reload mods and mod screen

                EMTK.sm.loadMods();
                EMTK.FullEarlyReloadMods();

                InitMeta(modid, EMTK.loadedMods[modid]);

                if (parentScreen is GuiScreenMods) {
                    invalidateParentScreen.Invoke(parentScreen, null);
                }
                invalidate();
            } catch (Exception ex) {
                ScreenManager.Platform.Logger.Error("Could not install {0}@{1}: {2}", modid, selectedVersion, ex);

                string message = lmod?.SourcePath?.EndsWith(".dll") == true
                    ? "Could not install {0}@{1}. Note that Vintage Story has some issues deleting dlls on Windows, which may have occurred here."
                    : "Could not install {0}@{1}. Check the log for more info.";
                EMTK.sm.LoadScreen(new GuiScreenInfo(String.Format(message, modid, selectedVersion), () => EMTK.sm.LoadScreen(this), EMTK.sm, this));
            }
            return true;
        }

        public bool RemoveMod() {
            try {
                if (Directory.Exists(lmod.SourcePath)) {
                    Directory.Delete(lmod.SourcePath, true);
                } else {
                    File.Delete(lmod.SourcePath);
                }

                EMTK.sm.loadMods();
                EMTK.FullEarlyReloadMods();
                if (parentScreen is GuiScreenMods) {
                    invalidateParentScreen.Invoke(parentScreen, null);
                    EMTK.sm.LoadScreen(parentScreen);
                }
            } catch (Exception ex) {
                ScreenManager.Platform.Logger.Error("Could not uninstall {0}: {2}", modid, selectedVersion, ex);

                string message = lmod?.SourcePath?.EndsWith(".dll") == true
                    ? "Could not uninstall {0}. Note that Vintage Story has some issues deleting dlls on Windows, which likely has occurred here."
                    : "Could not uninstall {0}. Check the log for more info.";
                EMTK.sm.LoadScreen(new GuiScreenInfo(String.Format(message, modid, selectedVersion), () => EMTK.sm.LoadScreen(this), EMTK.sm, this));
            }

            return true;
        }

        public void DrawIcon(Cairo.Context ctx, Cairo.ImageSurface surface, ElementBounds currentBounds) {
            surface.Image(icon, 0, 0, iconSize.Width, iconSize.Height);
        }

		public void invalidate() {
			if (base.IsOpened) {
				InitGui();
				return;
			}
			ScreenManager.GuiComposers.Dispose("mainmenu-modinfo");
		}
    }
}