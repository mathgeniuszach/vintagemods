using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;

namespace TitleScreenTweak {
    public class ImageButton : GuiElementControl {
        public override bool Focusable { get { return true; } }
        public bool Visible = true;
        public bool PlaySound = true;
        
        public bool active = false;
        public bool isOver = false;
        public bool currentlyMouseDownOnElement = false;

        public string normalImg;
        public string hoverImg;
        public string disabledImg;
        public LoadedTexture normalTexture = null;
        public LoadedTexture hoverTexture = null;
        public LoadedTexture disabledTexture = null;

        public ActionConsumable onClick;

        public ImageButton(ICoreClientAPI capi, string normalImg, string hoverImg, string disabledImg, ActionConsumable onClick, ElementBounds bounds) : base(capi, bounds) {
            this.normalImg = normalImg;
            this.hoverImg = hoverImg;
            this.disabledImg = disabledImg;

            this.onClick = onClick;

            normalTexture = new LoadedTexture(capi);
            if (hoverImg != null) hoverTexture = new LoadedTexture(capi);
            if (disabledImg != null) disabledTexture = new LoadedTexture(capi);
        }

        public override void ComposeElements(Context ctxStatic, ImageSurface surface) {
			Bounds.CalcWorldBounds();

			BitmapExternal normalBitmap = (BitmapExternal)ScreenManager.Platform.CreateBitmapFromPng(this.api.Assets.Get(normalImg));
			ImageSurface normalSurface = new ImageSurface(0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
            normalSurface.Image(normalBitmap, 0, 0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);

            if (hoverImg != null) {
                BitmapExternal hoverBitmap = (BitmapExternal)ScreenManager.Platform.CreateBitmapFromPng(this.api.Assets.Get(hoverImg));
                ImageSurface hoverSurface = new ImageSurface(0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
                hoverSurface.Image(hoverBitmap, 0, 0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
                base.generateTexture(hoverSurface, ref hoverTexture, true);
                hoverBitmap.Dispose();
                hoverSurface.Dispose();
            }

            if (disabledImg != null) {
                BitmapExternal disabledBitmap = (BitmapExternal)ScreenManager.Platform.CreateBitmapFromPng(this.api.Assets.Get(normalImg));
                ImageSurface disabledSurface = new ImageSurface(0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
                disabledSurface.Image(disabledBitmap, 0, 0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
                base.generateTexture(disabledSurface, ref disabledTexture, true);
                disabledBitmap.Dispose();
                disabledSurface.Dispose();
            }

            base.generateTexture(normalSurface, ref normalTexture, true);
            normalBitmap.Dispose();
            normalSurface.Dispose();
        }

        public override void RenderInteractiveElements(float deltaTime) {
			if (!this.Visible) return;
			
			if (!this.enabled) {
                this.api.Render.Render2DTexture(this.disabledTexture?.TextureId ?? this.normalTexture.TextureId, this.Bounds, 50f, null);
			} else if (this.isOver || this.currentlyMouseDownOnElement) {
				this.api.Render.Render2DTexture(this.hoverTexture?.TextureId ?? this.normalTexture.TextureId, this.Bounds, 50f, null);
			} else {
                this.api.Render.Render2DTexture(this.normalTexture.TextureId, this.Bounds, 50f, null);
            }
		}

        public override void OnKeyDown(ICoreClientAPI api, KeyEvent args) {
			if (!this.Visible) return;
			if (!base.HasFocus) return;
			if (args.KeyCode != 49) return;
			
            args.Handled = true;
            if (!this.enabled) return;

            if (this.PlaySound) api.Gui.PlaySound("menubutton_press", false, 1f);
            args.Handled = this.onClick();
		}

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args) {
			if (!this.Visible) return;

			if (this.active || this.enabled && this.Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) {
				if (!this.isOver && this.PlaySound) api.Gui.PlaySound("menubutton", false, 1f);
				this.isOver = true;
				return;
			}

            this.isOver = false;
		}

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) {
            // This function doesn't get called for some reason
			if (!this.Visible) return;
			if (!this.enabled) return;

			base.OnMouseDownOnElement(api, args);
			this.currentlyMouseDownOnElement = true;
		}

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args) {
			if (!this.Visible) return;

			base.OnMouseUp(api, args);
			this.currentlyMouseDownOnElement = false;
		}

        public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args) {
            // This comment is necessary because OnMouseDownOnElement isn't getting called for some reason
			if (this.enabled /*&& this.currentlyMouseDownOnElement*/ && this.Bounds.PointInside(args.X, args.Y) && args.Button == EnumMouseButton.Left) {
				if (this.PlaySound) api.Gui.PlaySound("menubutton_press", false, 1f);
				args.Handled = this.onClick();
			}
			this.currentlyMouseDownOnElement = false;
		}

        public void SetActive(bool active) {
			this.active = active;
		}

        public override void Dispose() {
			base.Dispose();
            if (normalTexture != null) normalTexture.Dispose();
            if (hoverTexture != null) hoverTexture.Dispose();
            if (disabledTexture != null) disabledTexture.Dispose();
		}
    }
}