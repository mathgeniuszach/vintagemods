using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;

namespace EMTK {
    public class GuiScreenInfo : GuiScreen {
        public string value;
		public Action Back;

		public override bool ShouldDisposePreviousScreen {
			get {
				return false;
			}
		}

		public GuiScreenInfo(string text, Action Back, ScreenManager screenManager, GuiScreen parentScreen) : base(screenManager, parentScreen) {
            this.Back = Back;
			this.ShowMainMenu = true;

			CairoFont font = CairoFont.WhiteSmallText().WithFontSize(17f).WithLineHeightMultiplier(1.25);
			double unscheight = screenManager.api.Gui.Text.GetMultilineTextHeight(
                font, text, GuiElement.scaled(650.0), EnumLinebreakBehavior.Default
            ) / (double)RuntimeEnv.GUIScale;

			ElementBounds titleBounds = ElementStdBounds.Rowed(0f, 0.0, EnumDialogArea.LeftFixed).WithFixedWidth(400.0);
			ElementBounds btnBounds = ElementBounds.Fixed(EnumDialogArea.LeftBottom, 0.0, 0.0, 0.0, 0.0).WithFixedPadding(10.0, 2.0);
			
			Size2d box = ElementBoundsPlus.GetBoxSize();

            TextDrawUtil textUtil = this.ScreenManager.api.Gui.Text;
			double minHeight = textUtil.GetMultilineTextHeight(CairoFont.WhiteSmallishText(), text, GuiElement.scaled(box.Width), EnumLinebreakBehavior.Default) / (double)RuntimeEnv.GUIScale;

			this.ElementComposer = base.dialogBase("mainmenu-entry", -1.0, minHeight + 60.0)
                .AddStaticText(
                    text, CairoFont.WhiteSmallishText(),
                    titleBounds
                ).AddButton(
                    Lang.Get("Back"), new ActionConsumable(BackWrapper), btnBounds.FlatCopy().WithAlignment(EnumDialogArea.RightBottom), EnumButtonStyle.Normal
                ).EndChildElements().Compose(true);
		}

		public bool BackWrapper() {
			this.Back.Invoke();
			return true;
		}
	}
}