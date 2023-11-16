using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace NightLight {

    public class NightLightConfig {
        public double MinimumBrightnessPercent = 0.1;
    }

    [HarmonyPatch]
    public class NightLight : ModSystem {
        public static ICoreAPI api;
        public static NightLightConfig config;
        public static Harmony harmony;

        public static CultureInfo culture = new CultureInfo("en-US");

        public override bool ShouldLoad(EnumAppSide side) {
            return side == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api) {
            NightLight.api = api;

            // Load config
            try {
                config = api.LoadModConfig<NightLightConfig>("nightlight.json");
            } catch {
                config = null;
            }
            if (config == null) {
                config = new NightLightConfig();
                api.StoreModConfig<NightLightConfig>(config, "nightlight.json");
            }

            // Apply Harmony patches
            harmony = new Harmony("nightlight");
            harmony.PatchAll();

            // Add nightlight command
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("nlight")
                .WithAlias("light")
                .BeginSubCommand("get")
                    .HandleWith(args => {
                        return TextCommandResult.Success(Lang.Get(
                            "nightlight:Command.Get", (int)(config.MinimumBrightnessPercent * 100.0), config.MinimumBrightnessPercent
                        ));
                    })
                .EndSubCommand()
                .BeginSubCommand("set")
                    .WithArgs(parsers.All("brightness"))
                    .HandleWith(args => {
                        try {
                            string percentStr = ((string)args[0]).Trim();
                            Console.WriteLine(percentStr);
                            double percent = 0.0;
                            if (percentStr[percentStr.Length-1] == '%') {
                                percent = double.Parse(percentStr.Substring(0, percentStr.Length-1));
                                percent /= 100.0;
                            } else {
                                percent = double.Parse(percentStr);
                                if (percent > 1) percent /= 100.0;
                            }
                            if (percent < 0.0 || percent > 1.0) throw new FormatException();

                            config.MinimumBrightnessPercent = percent;
                            api.StoreModConfig(config, "nightlight.json");
                            api.Shader.ReloadShaders();
                            return TextCommandResult.Success(Lang.Get("nightlight:Command.Set", (int)(percent * 100.0), percent));
                        } catch (FormatException) {
                            return TextCommandResult.Error(Lang.Get("nightlight:Command.Set.Fail"));
                        }
                    })
                .EndSubCommand();

            // Reload shaders to apply shader patches
            api.Shader.ReloadShaders();
        }
        
        // public override void Dispose() {
        //     harmony.UnpatchAll("nightlight");
        //     base.Dispose();
        // }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AssetManager), "Reload", typeof(AssetCategory))]
        public static void PatchShaderAssets(AssetCategory category) {
            if (category != AssetCategory.shaderincludes) return;

            // Patch "fogandlight.vsh" for brightness
            AssetLocation assetLoc = new AssetLocation("game", "shaderincludes/fogandlight.vsh");
            Asset asset = (Asset)api.Assets.Get(assetLoc);

            string shader = asset.ToText();
            shader = Regex.Replace(
                shader,
                "(vec3 (?:block|sun)LightColor) = (.*?);",
                "$1 = mix($2, vec3(1.0, 1.0, 1.0), MINIMUM_BRIGHTNESS_PERCENT + 0);"
            );

            asset.Data = Encoding.UTF8.GetBytes(shader);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShaderRegistry), "registerDefaultShaderCodePrefixes")]
        public static void registerDefaultShaderCodePrefixes(ShaderProgram program) {
            double mbp = config?.MinimumBrightnessPercent ?? 0.1;

            Shader vxs = program.VertexShader;
            vxs.PrefixCode = vxs.PrefixCode + "#define MINIMUM_BRIGHTNESS_PERCENT " + mbp.ToString(culture) + "\r\n";
        }
    }

}