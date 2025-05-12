using System.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace EMTK {
    public class GuiScreenUpdateMods : GuiScreen {
        public static readonly MethodInfo invalidateParentScreen = AccessTools.Method(typeof(GuiScreenMods), "invalidate");
        public static readonly MethodInfo handlehandler = AccessTools.Method(typeof(GuiElementScrollbar), "UpdateHandlePositionAbs");
        public static readonly FieldInfo handlepos = AccessTools.Field(typeof(GuiElementScrollbar), "currentHandlePosition");
        public static readonly FieldInfo showModifyIcons = AccessTools.Field(typeof(GuiElementModCell), "showModifyIcons");

        public GuiScreen parentScreen;
        public ElementBounds clippingBounds;
        public ElementBounds listBounds;

        public List<CustomModCellEntry> modCells;
        public string query = "";
        public string sortingMethod = "Recently Updated";
        public string side = "any";
        public float scrollLoc = 0;

        public static float visibleHeight;

		public override bool ShouldDisposePreviousScreen {
			get {
				return true;
			}
		}

        public GuiScreenUpdateMods(List<string> modUpdates, ScreenManager screenManager, GuiScreen parentScreen) : base(screenManager, parentScreen) {
			this.ShowMainMenu = true;
            this.parentScreen = parentScreen;

            modCells = new List<CustomModCellEntry>();
            foreach (string modid in modUpdates) {
                modCells.Add(new CustomModCellEntry() {
                    ModID = modid,
                    Title = modid,
                    DetailText = ModAPI.latestReleaseCache[modid].filename,
                    RightTopText = String.Format("{0} -> {1}", EMTK.loadedMods[modid].Info.Version, ModAPI.latestVersionCache[modid])
                });
            }
			InitGui();
            
			screenManager.GamePlatform.WindowResized += (int w, int h) => {invalidate();};
			ClientSettings.Inst.AddWatcher<float>("guiScale", (float s) => {invalidate();});
		}

        public void InitGui() {
            Size2d box = ElementBoundsPlus.GetBoxSize();

            this.ElementComposer = base.dialogBase("mainmenu-browsemods", -1.0, -1.0)
                .AddStaticText("Update Mods - " + modCells.Count + " Total", CairoFont.WhiteSmallishText(), ElementBounds.Fixed(0.0, 0.0, 400.0, 30.0))
                .AddInset(ElementBounds.Fixed(10.0, 40.0, box.Width-40.0, box.Height-95.5))
                .AddVerticalScrollbar(
                    (v) => {
                        scrollLoc = (float)handlepos.GetValue(this.ElementComposer.GetScrollbar("scrollbar"));
                        ElementBounds bounds = this.ElementComposer.GetCellList<CustomModCellEntry>("modsupdatelist").Bounds;
                        bounds.fixedY = -v;
                        bounds.CalcWorldBounds();
                    },
                    ElementStdBounds.VerticalScrollbar(ElementBounds.Fixed(20.0, 40.0, box.Width-50.0, box.Height-95.0)), "scrollbar")
                .BeginClip(clippingBounds = ElementBounds.Fixed(20.0, 40.0, box.Width-60.0, box.Height-95.0))
                    .AddCellList(
                        listBounds = clippingBounds.ForkContainingChild(0.0, 0.0, 0.0, 0.0).WithFixedPadding(0.0, 10.0),
                        new OnRequireCell<CustomModCellEntry>(this.createCellElem),
                        modCells,
                        "modsupdatelist"
                    )
                .EndClip()
                .AddButton(
                    "Update", UpdateMods,
                    ElementBounds.Fixed(EnumDialogArea.RightBottom, 0.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                )
                .AddButton(
                    "Cancel", () => {EMTK.sm.LoadScreen(parentScreen); return true;},
                    ElementBounds.Fixed(EnumDialogArea.LeftBottom, 0.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                )
                .Compose();
            
            this.listBounds.CalcWorldBounds();
            this.clippingBounds.CalcWorldBounds();

            float oldScrollLoc = scrollLoc;

            this.ElementComposer.GetScrollbar("scrollbar").SetHeights(
                (float)clippingBounds.fixedHeight,
                (float)listBounds.fixedHeight
            );

            if (oldScrollLoc > 0) {
                handlehandler.Invoke(this.ElementComposer.GetScrollbar("scrollbar"), new object[] {oldScrollLoc});
            }
        }

        public bool UpdateMods() {
            ConcurrentBag<Tuple<APIModRelease, ModContainer>> updates = new ConcurrentBag<Tuple<APIModRelease, ModContainer>>();
            foreach (CustomModCellEntry entry in modCells) {
                if (!EMTK.Config.excludedFromUpdates.Contains(entry.ModID)) {
                    updates.Add(new Tuple<APIModRelease, ModContainer>(ModAPI.latestReleaseCache[entry.ModID], EMTK.loadedMods[entry.ModID]));
                }
            }

            ConcurrentBag<string> failed = new ConcurrentBag<string>();

            // Download and install all mods in a concurrent way
            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < 8; i++) {
                Thread t = new Thread(() => {
                    while (true) {
                        Tuple<APIModRelease, ModContainer> tuple;
                        if (!updates.TryTake(out tuple)) return;

                        // Download mod file
                        try {
                            string fileloc = ModAPI.GetAsset(tuple.Item1.mainfile, 3);
                            if (fileloc == null) failed.Add(tuple.Item1.modidstr);

                            // Delete old mod
                            if (Directory.Exists(tuple.Item2.SourcePath)) {
                                Directory.Delete(tuple.Item2.SourcePath, true);
                            } else {
                                try {
                                    File.Delete(tuple.Item2.SourcePath);
                                } catch (UnauthorizedAccessException) {
                                    EMTK.PseudoDelete(tuple.Item2.SourcePath);
                                } catch (IOException) {
                                    EMTK.PseudoDelete(tuple.Item2.SourcePath);
                                }
                            }

                            // Install new mod
                            File.Move(fileloc, Path.Combine(GamePaths.DataPathMods, tuple.Item1.filename));
                        } catch {
                            failed.Add(tuple.Item1.modidstr);
                        }
                    }
                });
                threads.Add(t);
            }
            foreach (Thread t in threads) t.Start();
            foreach (Thread t in threads) t.Join();

            EMTK.sm.LoadScreen(new GuiScreenInfo(
                "Updated all mods." + (failed.IsEmpty ? "" : "\r\nThese mods failed the update: " + String.Join(", ", failed)),
                () => {
                    EMTK.sm.LoadScreen(parentScreen);
                    AccessTools.Method(typeof(GuiScreenMods), "OnReloadMods").Invoke(parentScreen, null);
                },
                EMTK.sm, parentScreen
            ));
            return true;
        }

        public IGuiElementCell createCellElem(CustomModCellEntry cell, ElementBounds bounds) {
			var modcell = new GuiElementModCell(this.ScreenManager.api, cell, bounds, null) {
                On = !EMTK.Config.excludedFromUpdates.Contains(cell.ModID.ToLower()),
				OnMouseDownOnCellLeft = new Action<int>(this.OnClickCellLeft),
				OnMouseDownOnCellRight = new Action<int>(this.OnClickCellRight)
			};
            showModifyIcons.SetValue(modcell, false);
            return modcell;
		}

        public override void OnScreenLoaded() {
            this.invalidate();
        }

        public void OnClickCellLeft(int cellIndex) {}

        public void OnClickCellRight(int cellIndex) {
            GuiElementCellList<CustomModCellEntry> cellList = this.ElementComposer.GetCellList<CustomModCellEntry>("modsupdatelist");
            GuiElementModCell guiCell = (GuiElementModCell)cellList.elementCells[cellIndex];
            guiCell.On = !guiCell.On;

            string modid = ((CustomModCellEntry)guiCell.cell).ModID.ToLower();
            if (guiCell.On) {
                EMTK.Config.excludedFromUpdates.Remove(modid);
            } else {
                EMTK.Config.excludedFromUpdates.Add(modid);
            }
            EMTKConfig.Save();
        }

        public void invalidate() {
            if (base.IsOpened) {
                InitGui();
                return;
            }
            ScreenManager.GuiComposers.Dispose("mainmenu-browsemods");
        }
	}
}
