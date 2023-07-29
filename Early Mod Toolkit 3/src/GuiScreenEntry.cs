using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;

namespace EMTK {
    public class GuiScreenEntry : GuiScreen {
        public string value;
		public Action<bool, string> DidPressButton;

		public override bool ShouldDisposePreviousScreen {
			get {
				return false;
			}
		}

		public GuiScreenEntry(string text, Action<bool, string> DidPressButton, ScreenManager screenManager, GuiScreen parentScreen) : base(screenManager, parentScreen) {
            this.value = text;
            this.DidPressButton = DidPressButton;
			this.ShowMainMenu = true;

			CairoFont font = CairoFont.WhiteSmallText().WithFontSize(17f).WithLineHeightMultiplier(1.25);
			double unscheight = screenManager.api.Gui.Text.GetMultilineTextHeight(
                font, text, GuiElement.scaled(650.0), EnumLinebreakBehavior.Default
            ) / (double)RuntimeEnv.GUIScale;

			ElementBounds titleBounds = ElementStdBounds.Rowed(0f, 0.0, EnumDialogArea.LeftFixed).WithFixedWidth(400.0);
			ElementBounds btnBounds = ElementBounds.Fixed(EnumDialogArea.LeftBottom, 0.0, 0.0, 0.0, 0.0).WithFixedPadding(10.0, 2.0);

			this.ElementComposer = base.dialogBase("mainmenu-entry", -1.0, 150.0)
                .AddStaticText(
                    Lang.Get("Name"), CairoFont.WhiteSmallishText().WithWeight(Cairo.FontWeight.Bold),
                    titleBounds, null
                ).AddTextInput(
                    ElementBounds.Percentual(0.0, 0.2, 1.0, 0.2), new Action<string>(OnTextChange),
                    font, "mainmenu-entry-field"
                ).AddButton(
                    Lang.Get("Cancel"), new ActionConsumable(this.OnCancel),
                    btnBounds, EnumButtonStyle.Normal
                ).AddButton(
                    Lang.Get("Confirm"), new ActionConsumable(this.OnConfirm), btnBounds.FlatCopy().WithAlignment(EnumDialogArea.RightBottom), EnumButtonStyle.Normal, "confirmButton"
                ).EndChildElements().Compose(true);
            
            this.ElementComposer.GetTextInput("mainmenu-entry-field").SetValue(text);
		}

        public void OnTextChange(string value) {
            this.value = value;
        }

		public bool OnConfirm() {
			this.ElementComposer.GetButton("confirmButton").Enabled = false;
			this.DidPressButton.Invoke(true, value);
			return true;
		}

		public bool OnCancel() {
			this.DidPressButton.Invoke(false, value);
			return true;
		}
	}
}