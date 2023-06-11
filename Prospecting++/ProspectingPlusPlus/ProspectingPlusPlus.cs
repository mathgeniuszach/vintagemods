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

    public class ProspectingPlusPlus : ModSystem
    {
        public static ICoreAPI api;
        public static Random rng = new Random();

        public override void Start(ICoreAPI api)
        {
            ProspectingPlusPlus.api = api;

            base.Start(api);

            api.RegisterItemClass("ItemProspectingPlusPlusPick", typeof(ItemProspectingPlusPlusPick));
        }
    }

    class ItemProspectingPlusPlusPick : ItemProspectingPick {
        public override void OnLoaded(ICoreAPI api) {
            ICoreClientAPI capi = api as ICoreClientAPI;

            var toolModes = ObjectCacheUtil.GetOrCreate(api, "proPlusPickToolModes", () => {
                ObjectCacheUtil.Delete(api, "proPickToolModes");

                SkillItem[] modes;
                if (api.World.Config.GetString("propickNodeSearchRadius").ToInt() > 0) {
                    modes = new SkillItem[4];
                    modes[0] = new SkillItem() { Code = new AssetLocation("density"), Name = Lang.Get("Density Search Mode (Long range, chance based search)") };
                    modes[1] = new SkillItem() { Code = new AssetLocation("node"), Name = Lang.Get("Node Search Mode (Short range, exact search)") };
                    modes[2] = new SkillItem() { Code = new AssetLocation("doublenode"), Name = Lang.Get("Double Node Search Mode (2x short range, 75% exact search)") };
                    modes[3] = new SkillItem() { Code = new AssetLocation("triplenode"), Name = Lang.Get("Triple Node Search Mode (3x short range, 50% exact search)") };
                } else {
                    modes = new SkillItem[1];
                    modes[0] = new SkillItem() { Code = new AssetLocation("density"), Name = Lang.Get("Density Search Mode (Long range, chance based search)") };
                }

                if (capi != null) {
                    modes[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/heatmap.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                    modes[0].TexturePremultipliedAlpha = false;
                    if (modes.Length > 1) {
                        modes[1].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/rocks.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                        modes[1].TexturePremultipliedAlpha = false;
                        modes[2].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("prospectingplusplus:textures/icons/doublerocks.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                        modes[2].TexturePremultipliedAlpha = false;
                        modes[3].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("prospectingplusplus:textures/icons/triplerocks.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                        modes[3].TexturePremultipliedAlpha = false;
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
                if (toolMode == 2 && r < 1.0/3.0 || toolMode >= 3 && r < 0.5) {
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
                    ProbeBlockNodeMode(world, byEntity, itemslot, blockSel, radius * toolMode);
                }

                damage = 2 * toolMode;
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