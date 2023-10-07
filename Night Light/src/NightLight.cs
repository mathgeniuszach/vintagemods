using System;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace NightLight {

    public class NightLightConfig {
        public double MinimumBrightnessPercent = 0.1;
    }

    [HarmonyPatch]
    public class NightLight : ModSystem {
        public static ICoreAPI api;
        public static NightLightConfig config;
        public static Harmony harmony;

        public override bool ShouldLoad(EnumAppSide side) {
            return side == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api) {
            NightLight.api = api;

            try {
                config = api.LoadModConfig<NightLightConfig>("nightlight.json");
            } catch {
                config = null;
            }
            if (config == null) {
                config = new NightLightConfig();
                api.StoreModConfig<NightLightConfig>(config, "nightlight.json");
            }

            harmony = new Harmony("nightlight");
            harmony.PatchAll();

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
                            }

                            config.MinimumBrightnessPercent = percent;
                            api.StoreModConfig<NightLightConfig>(config, "nightlight.json");
                            api.Shader.ReloadShaders();
                            return TextCommandResult.Success(Lang.Get("nightlight:Command.Set", (int)(percent * 100.0), percent));
                        } catch (FormatException) {
                            return TextCommandResult.Error(Lang.Get("nightlight:Command.Set.Fail"));
                        }
                    })
                .EndSubCommand();
            
            api.Shader.ReloadShaders();
        }
        
        public override void Dispose() {
            harmony.UnpatchAll("nightlight");
            base.Dispose();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShaderRegistry), "registerDefaultShaderCodePrefixes")]
        public static void registerDefaultShaderCodePrefixes(ShaderProgram program) {
            double mbp = NightLight.config?.MinimumBrightnessPercent ?? 0.1;

            Shader vxs = program.VertexShader;
            vxs.PrefixCode = vxs.PrefixCode + "#define MINIMUM_BRIGHTNESS_PERCENT " + mbp.ToString() + "\r\n";
        }
    }

}