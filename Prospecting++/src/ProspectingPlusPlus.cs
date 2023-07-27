using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

using Vintagestory.GameContent;

namespace ProspectingPlusPlus {
    public class ProspectingNodeMode {
        public int sizeMultiplier = 1;
        public int costMultiplier = 1;
        public double falseNegativePercent = 0.0;
        public String id = "node";
        public String icon = "textures/icons/rocks.svg";
        public String description = "Node Search Mode (Short range, exact search)";
        public ProspectingNodeMode() {}
        public ProspectingNodeMode(int sizeMult, int costMult, double failPerc, String id, String icon, String desc) {
            sizeMultiplier = sizeMult;
            costMultiplier = costMult;
            falseNegativePercent = failPerc;
            this.id = id;
            this.icon = icon;
            description = desc;
        }
    }

    public class ProspectingPlusPlusConfig {
        public bool densityModeEnabled = true;
        public ProspectingNodeMode[] modes = new ProspectingNodeMode[] {
            new ProspectingNodeMode(1, 1, 0,
                "node",
                "textures/icons/rocks.svg",
                "Node Search Mode (Short range, exact search)"
            ),
            new ProspectingNodeMode(2, 4, 1.0/3.0,
                "doublenode",
                "prospectingplusplus:textures/icons/doublerocks.svg",
                "Double Node Search Mode (2x short range, 67% exact search)"
            ),
            new ProspectingNodeMode(3, 9, 0.5,
                "triplenode",
                "prospectingplusplus:textures/icons/triplerocks.svg",
                "Triple Node Search Mode (3x short range, 50% exact search)"
            )
        };
    }

    public class ProspectingPlusPlus : ModSystem
    {
        public static ICoreAPI api;
        public static ProspectingPlusPlusConfig config;
        public static Random rng = new Random();

        public override void Start(ICoreAPI api)
        {
            ProspectingPlusPlus.api = api;

            try {
                config = api.LoadModConfig<ProspectingPlusPlusConfig>("prospecting++.json");
            } catch {
                config = null;
            }
            if (config == null) {
                config = new ProspectingPlusPlusConfig();
                api.StoreModConfig<ProspectingPlusPlusConfig>(config, "prospecting++.json");
            }

            base.Start(api);

            api.RegisterItemClass("ItemProspectingPlusPlusPick", typeof(ItemProspectingPlusPlusPick));
        }
    }

    class ItemProspectingPlusPlusPick : ItemProspectingPick {
        public override void OnLoaded(ICoreAPI api) {
            ICoreClientAPI capi = api as ICoreClientAPI;

            var toolModes = ObjectCacheUtil.GetOrCreate(api, "proPlusPickToolModes", () => {
                ObjectCacheUtil.Delete(api, "proPickToolModes");

                int modeCount = ProspectingPlusPlus.config.modes.Length;
                if (ProspectingPlusPlus.config.densityModeEnabled) modeCount++;

                SkillItem[] modes;
                if (api.World.Config.GetString("propickNodeSearchRadius").ToInt() > 0) {
                    modes = new SkillItem[modeCount];
                    int n = 0;
                    if (ProspectingPlusPlus.config.densityModeEnabled) {
                        modes[0] = new SkillItem() { Code = new AssetLocation("density"), Name = Lang.Get("Density Search Mode (Long range, chance based search)") };
                        n++;
                        if (capi != null) {
                            modes[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/heatmap.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                            modes[0].TexturePremultipliedAlpha = false;
                        }
                    }
                    for (int i = 0; n < modeCount; i++, n++) {
                        ProspectingNodeMode nmode = ProspectingPlusPlus.config.modes[i];
                        modes[n] = new SkillItem() { Code = new AssetLocation(nmode.id), Name = Lang.Get(nmode.description) };
                        if (capi != null) {
                            modes[n].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation(nmode.icon), 48, 48, 5, ColorUtil.WhiteArgb));
                            modes[n].TexturePremultipliedAlpha = false;
                        }
                    }
                } else {
                    modes = new SkillItem[1];
                    modes[0] = new SkillItem() { Code = new AssetLocation("density"), Name = Lang.Get("Density Search Mode (Long range, chance based search)") };
                    if (capi != null) {
                        modes[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/heatmap.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                        modes[0].TexturePremultipliedAlpha = false;
                    }
                }

                return modes;
            });

            ObjectCacheUtil.GetOrCreate(api, "proPickToolModes", () => toolModes);

            base.OnLoaded(api);
        }
        public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1) {
            int toolMode = GetToolMode(itemslot, (byEntity as EntityPlayer)?.Player, blockSel);
            int radius = api.World.Config.GetString("propickNodeSearchRadius").ToInt();
            int damage = 1;

            if (toolMode >= 1 && radius > 0) {
                double r = ProspectingPlusPlus.rng.NextDouble();
                ProspectingNodeMode nmode = ProspectingPlusPlus.config.modes[toolMode-1];
                if (r < nmode.falseNegativePercent) {
                    // False negatives
                    IPlayer byPlayer = null;
                    if (byEntity is EntityPlayer) byPlayer = world.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);

                    Block block = world.BlockAccessor.GetBlock(blockSel.Position);
                    block.OnBlockBroken(world, blockSel.Position, byPlayer, 0);

                    if (block?.Attributes?["propickable"].AsBool(false) == true) {
                        IServerPlayer splr = byPlayer as IServerPlayer;
			            if (splr != null) {
                            splr.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(splr.LanguageCode, "No ore node nearby"), EnumChatType.Notification);
                        }
                    }
                } else {
                    ProbeBlockNodeMode(world, byEntity, itemslot, blockSel, radius * nmode.sizeMultiplier);
                }

                damage = 2 * nmode.costMultiplier;
            } else {
                ProbeBlockDensityMode(world, byEntity, itemslot, blockSel);
            }

            if (DamagedBy != null && DamagedBy.Contains(EnumItemDamageSource.BlockBreaking)) {
                DamageItem(world, byEntity, itemslot, damage);
            }

            return true;
        }
    }
    
}