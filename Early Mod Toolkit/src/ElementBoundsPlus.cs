using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace EMTK {
    public static class ElementBoundsPlus {
        public static ElementBounds Dynamic(
            double width = 0.0, double height = 0.0,
            double pwidth = 0.0, double pheight = 0.0,
            double x = 0.0, double y = 0.0,
            double px = 0.0, double py = 0.0
        ) {
            ElementBounds bounds = new ElementBounds {
                fixedOffsetX = x,
                fixedOffsetY = y,
                fixedWidth = -width,
                fixedHeight = -height,
                percentX = px,
                percentY = py,
                percentWidth = pwidth,
                percentHeight = pheight,
				BothSizing = ElementSizing.PercentualSubstractFixed
            };
            return bounds;
        }

        public static ElementBounds Dynamic(
            EnumDialogArea alignment,
            double width = 0.0, double height = 0.0,
            double pwidth = 0.0, double pheight = 0.0,
            double x = 0.0, double y = 0.0,
            double px = 0.0, double py = 0.0
        ) {
            ElementBounds bounds = new ElementBounds {
                Alignment = alignment,
                fixedOffsetX = x,
                fixedOffsetY = y,
                fixedWidth = -width,
                fixedHeight = -height,
                percentX = px,
                percentY = py,
                percentWidth = pwidth,
                percentHeight = pheight,
				BothSizing = ElementSizing.PercentualSubstractFixed
            };
            return bounds;
        }

        public static Size2d GetBoxSize() {
            double scrwidth = ScreenManager.Platform.WindowSize.Width;
            double scrheight = ScreenManager.Platform.WindowSize.Height;

            // Courtesy of dialogBase(), calculate width of panel size
            return new Size2d(
                Math.Max(400.0, (double)scrwidth * 0.5) / (double)ClientSettings.GUIScale + 40.0,
                Math.Max(300, scrheight) / ClientSettings.GUIScale - 120
            );
        }
    }
}