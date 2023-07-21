using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace EMTK {
    public class GuiScreenBrowseMods : GuiScreen {
        public static readonly MethodInfo handlehandler = AccessTools.Method(typeof(GuiElementScrollbar), "UpdateHandlePositionAbs");
        public static readonly FieldInfo handlepos = AccessTools.Field(typeof(GuiElementScrollbar), "currentHandlePosition");

        public static readonly string[] sortingMethods = new[] {
            "Recently Updated",
            "Recently Added",
            "Most Downloads",
            "Most Follows",
            "Most Comments",
            "Most Trending",
            "Alphabetical",
        };
        public static readonly Dictionary<string, Func<IEnumerable<CustomModCellEntry>, IEnumerable<CustomModCellEntry>>> filters = 
            new Dictionary<string, Func<IEnumerable<CustomModCellEntry>, IEnumerable<CustomModCellEntry>>>() {
                {"Recently Updated", mx => mx.OrderByDescending(m => m.Summary.lastreleased)},
                {"Most Downloads", mx => mx.OrderByDescending(m => m.Summary.downloads)},
                {"Most Follows", mx => mx.OrderByDescending(m => m.Summary.follows)},
                {"Most Comments", mx => mx.OrderByDescending(m => m.Summary.comments)},
                {"Most Trending", mx => mx.OrderByDescending(m => m.Summary.trendingpoints)},
                {"Alphabetical", mx => mx.OrderBy(m => m.Summary.name.ToLower())},
            };

        public GuiScreen parentScreen;
        public ElementBounds clippingBounds;
        public ElementBounds listBounds;

        public List<CustomModCellEntry> modCells = new List<CustomModCellEntry>();
        public string query = "";
        public string sortingMethod = "Recently Updated";
        public string side = "any";
        public bool ascending = false;
        public float scrollLoc = 0;

        public static float visibleHeight;

        public GuiScreenBrowseMods(ScreenManager screenManager, GuiScreen parentScreen) : base(screenManager, parentScreen) {
			this.ShowMainMenu = true;
            this.parentScreen = parentScreen;

			InitGui();
            
			screenManager.GamePlatform.WindowResized += (int w, int h) => {invalidate();};
			ClientSettings.Inst.AddWatcher<float>("guiScale", (float s) => {invalidate();});
		}

        public void InitGui() {
            Size2d box = ElementBoundsPlus.GetBoxSize();

            this.ElementComposer = base.dialogBase("mainmenu-browsemods", -1.0, -1.0)
                .AddTextInput(
                    ElementBounds.Fixed(10.0, 5.0, box.Width-15.0, 30.0),
                    s => {this.query = s.ToLower().Trim(); QueueReloadCells();},
                    CairoFont.WhiteSmallishText(), "querytext"
                )
                .AddDropDown(
                    sortingMethods, sortingMethods, 0,
                    (c, s) => {this.sortingMethod = c; QueueReloadCells();},
                    ElementBounds.Fixed(10.0, 45.0, 170.0, 30.0), "querysortmethod"
                )
                .AddDropDown(
                    new[] {"default", "reversed"}, new[] {"Descending", "Ascending"}, 0,
                    (c, s) => {ascending = c == "reversed"; QueueReloadCells();},
                    ElementBounds.Fixed(200.0, 46.0, 130.0, 31.0), "querysortreversed"
                )
                .AddDropDown(
                    new[] {"any", "client", "server", "both"}, new[] {"Any-sided", "Client-only", "Server-only", "Both-sided"}, 0,
                    (c, s) => {side = c; QueueReloadCells();},
                    ElementBounds.Fixed(350.0, 46.0, 130.0, 32.0), "queryside"
                )
                .AddInset(ElementBounds.Fixed(10.0, 90.0, box.Width-40.0, box.Height-145.5))
                .AddVerticalScrollbar(
                    (v) => {
                        scrollLoc = (float)handlepos.GetValue(this.ElementComposer.GetScrollbar("scrollbar"));
                        ElementBounds bounds = this.ElementComposer.GetCellList<CustomModCellEntry>("modsbrowselist").Bounds;
                        bounds.fixedY = -v;
                        bounds.CalcWorldBounds();
                    },
                    ElementStdBounds.VerticalScrollbar(ElementBounds.Fixed(20.0, 90.0, box.Width-50.0, box.Height-145.0)), "scrollbar")
                .BeginClip(clippingBounds = ElementBounds.Fixed(20.0, 90.0, box.Width-60.0, box.Height-145.0))
                    .AddCellList(
                        listBounds = clippingBounds.ForkContainingChild(0.0, 0.0, 0.0, 0.0).WithFixedPadding(0.0, 10.0),
                        new OnRequireCell<CustomModCellEntry>(this.createCellElem),
                        null,
                        "modsbrowselist"
                    )
                .EndClip()
                .AddButton(
                    "Back", () => {EMTK.sm.LoadScreen(parentScreen); return true;},
                    ElementBounds.Fixed(EnumDialogArea.RightBottom, 0.0, 0.0, 60.0, 30.0).WithFixedPadding(10.0, 2.0)
                )
                .Compose();
            
            QueueReloadCells();
            
            this.listBounds.CalcWorldBounds();
            this.clippingBounds.CalcWorldBounds();

            float oldScrollLoc = scrollLoc;

            this.ElementComposer.GetScrollbar("scrollbar").SetHeights(
                (float)clippingBounds.fixedHeight,
                (float)listBounds.fixedHeight
            );

            this.ElementComposer.GetTextInput("querytext").SetValue(query);
            this.ElementComposer.GetDropDown("queryside").SetSelectedValue(side);
            if (ascending) this.ElementComposer.GetDropDown("querysortreversed").SetSelectedValue("reversed");
            this.ElementComposer.GetDropDown("querysortmethod").SetSelectedValue(sortingMethod);
            if (oldScrollLoc > 0) {
                handlehandler.Invoke(this.ElementComposer.GetScrollbar("scrollbar"), new object[] {oldScrollLoc});
            }
        }

        public List<CustomModCellEntry> LoadModCells() {
            while (!ModAPI.modsQueryFinished) Thread.Sleep(100);

            // Sort by sorting methods
            IEnumerable<CustomModCellEntry> cells = ModAPI.modCells;
            if (filters.ContainsKey(sortingMethod)) {
                cells = filters[sortingMethod](cells);
            }

            if (ascending) cells = cells.Reverse();

            // Sort by side
            if (side != "any") cells = cells.Where(m => m.Summary.side == side);

            // Sort by queries
            if (query.Length == 0) {
                modCells = cells.ToList();
                return modCells;
            }

            string[][] wordSets = query.Split(
                new[] {"|"}, StringSplitOptions.RemoveEmptyEntries
            ).Select(s => s.Split(
                new char[] {' '}, StringSplitOptions.RemoveEmptyEntries
            )).ToArray();

            modCells = cells.Where(m => {
                string keywords = m.Keywords;
                foreach (string[] wordSet in wordSets) {
                    bool wordSetMatch = true;
                    foreach (string word in wordSet) {
                        if (!keywords.Contains(word)) {
                            wordSetMatch = false;
                            break;
                        } 
                    }
                    if (wordSetMatch) return true;
                }
                return false;
            }).ToList();
            return modCells;
		}

        public IGuiElementCell createCellElem(CustomModCellEntry cell, ElementBounds bounds) {
			return new GuiElementModCell(this.ScreenManager.api, cell, bounds) {
                On = EMTK.loadedMods.ContainsKey(cell.ModID),
				OnMouseDownOnCellLeft = new Action<int>(this.OnClickCellLeft),
				OnMouseDownOnCellRight = new Action<int>(this.OnClickCellRight)
			};
		}

        object reloadLock = new object();
        object varLock = new object();
        int queuedReloads = 0;

        public void QueueReloadCells() {
            modCells = new List<CustomModCellEntry>();
            ReloadCells();

            lock (varLock) {
                if (queuedReloads > 1) return;
                queuedReloads++;
            }

            new Thread(() => {
                lock (reloadLock) {
                    this.LoadModCells();
                    lock (varLock) {
                        queuedReloads--;
                    }

                    ScreenManager.EnqueueMainThreadTask(new Action(ReloadCells));
                }
            }).Start();
        }

        public void ReloadCells() {
            GuiElementCellList<CustomModCellEntry> cellList = this.ElementComposer.GetCellList<CustomModCellEntry>("modsbrowselist");
            this.ElementComposer.GetCellList<CustomModCellEntry>("modsbrowselist").ReloadCells(modCells);

            ElementBounds bounds = cellList.Bounds;
            bounds.CalcWorldBounds();

            this.ElementComposer.GetScrollbar("scrollbar").SetHeights(
                (float)clippingBounds.fixedHeight,
                (float)bounds.fixedHeight
            );
        }

        public override void OnScreenLoaded() {
            this.invalidate();
        }

        public void OnClickCellLeft(int cellIndex) {
            string modid = modCells[cellIndex].ModID.ToLower();
            ModContainer mod = null;
            EMTK.loadedMods.TryGetValue(modid, out mod);

            EMTK.sm.LoadScreen(new GuiScreenModInfo(
                modid, mod, EMTK.sm, this
            ));
		}

        public void OnClickCellRight(int cellIndex) {
            GuiElementCellList<CustomModCellEntry> cellList = this.ElementComposer.GetCellList<CustomModCellEntry>("modsbrowselist");
            GuiElementModCell guiCell = (GuiElementModCell)cellList.elementCells[cellIndex];

            string modid = ((CustomModCellEntry)guiCell.cell).ModID.ToLower();

            if (guiCell.On) {
                ModContainer mod = EMTK.loadedMods[modid];

                // Time to uninstall the mod
                if (Directory.Exists(mod.SourcePath)) {
                    Directory.Delete(mod.SourcePath, true);
                } else {
                    File.Delete(mod.SourcePath);
                }
            } else {
                // Time to install the mod
                APIStatusModInfo smod = ModAPI.GetMod(modid);
                if (smod.statuscode != "200") return;

                APIModRelease release = smod.mod.releases[0];

                string cfile = ModAPI.GetAsset("https://mods.vintagestory.at/"+release.mainfile);
                if (cfile == null) return;
                
                File.Move(cfile, Path.Combine(GamePaths.DataPathMods, release.filename));
            }

            EMTK.sm.loadMods();
            EMTK.FullEarlyReloadMods();

            guiCell.On = !guiCell.On;
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