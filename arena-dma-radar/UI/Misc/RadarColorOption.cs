using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.SKWidgetControl;
using System.Windows.Markup.Localizer;

namespace arena_dma_radar.UI
{
    public enum RadarColorOption
    {
        LocalPlayer,
        Friendly,
        USEC,
        BEAR,
        Focused,
        Streamer,
        AimbotTarget,
        Special,
        AI,
        DeathMarker,
        ThrowablesFilterLoot,
        WeaponsFilterLoot,
        MedsFilterLoot,
        BackpacksFilterLoot,
        ContainerLoot,
        RefillContainer,
        Explosives,
        GroupLines
    }

    internal static class RadarColorOptions
    {
        #region Static Interfaces

        /// <summary>
        /// Load all ESP Color Config. Run once at start of application.
        /// </summary>
        internal static void LoadColors(Config config)
        {
            config.Colors ??= new Dictionary<RadarColorOption, string>();

            foreach (var defaultColor in GetDefaultColors())
                config.Colors.TryAdd(defaultColor.Key, defaultColor.Value);

            SetColors(config.Colors);
        }

        /// <summary>
        /// Returns all default color combinations for Radar.
        /// </summary>
        internal static Dictionary<RadarColorOption, string> GetDefaultColors() =>
            new()
            {
                [RadarColorOption.LocalPlayer] = SKColors.White.ToString(),
                [RadarColorOption.Friendly] = SKColors.LimeGreen.ToString(),
                [RadarColorOption.USEC] = SKColors.Red.ToString(),
                [RadarColorOption.BEAR] = SKColors.Blue.ToString(),
                [RadarColorOption.Focused] = SKColors.Coral.ToString(),
                [RadarColorOption.Streamer] = SKColors.MediumPurple.ToString(),
                [RadarColorOption.AimbotTarget] = SKColors.Blue.ToString(),
                [RadarColorOption.Special] = SKColors.MediumPurple.ToString(),
                [RadarColorOption.AI] = SKColors.Yellow.ToString(),
                [RadarColorOption.DeathMarker] = SKColors.Black.ToString(),
                [RadarColorOption.ThrowablesFilterLoot] = SKColors.Orange.ToString(),
                [RadarColorOption.WeaponsFilterLoot] = SKColors.WhiteSmoke.ToString(),
                [RadarColorOption.MedsFilterLoot] = SKColors.LightSalmon.ToString(),
                [RadarColorOption.BackpacksFilterLoot] = SKColor.Parse("00b02c").ToString(),
                [RadarColorOption.ContainerLoot] = SKColor.Parse("FFFFCC").ToString(),
                [RadarColorOption.RefillContainer] = SKColor.Parse("FFFFCC").ToString(),
                [RadarColorOption.Explosives] = SKColors.OrangeRed.ToString(),
                [RadarColorOption.GroupLines] = SKColors.LimeGreen.ToString(),
            };

        /// <summary>
        /// Save all ESP Color Changes.
        /// </summary>
        internal static void SetColors(IReadOnlyDictionary<RadarColorOption, string> colors)
        {
            try
            {
                foreach (var color in colors)
                {
                    if (!SKColor.TryParse(color.Value, out var skColor))
                        throw new Exception($"Invalid Color Value for {color.Key}!");

                    switch (color.Key)
                    {
                        case RadarColorOption.LocalPlayer:
                            SKPaints.PaintLocalPlayer.Color = skColor;
                            SKPaints.PaintMiniLocalPlayer.Color = skColor;
                            SKPaints.TextLocalPlayer.Color = skColor;
                            AimviewWidget.PaintESPWidgetLocalPlayer.Color = skColor;
                            break;
                        case RadarColorOption.Friendly:
                            SKPaints.PaintTeammate.Color = skColor;
                            SKPaints.PaintMiniTeammate.Color = skColor;
                            SKPaints.TextTeammate.Color = skColor;
                            AimviewWidget.PaintESPWidgetTeammate.Color = skColor;
                            break;
                        case RadarColorOption.USEC:
                            SKPaints.PaintUSEC.Color = skColor;
                            SKPaints.PaintMiniUSEC.Color = skColor;
                            SKPaints.TextUSEC.Color = skColor;
                            AimviewWidget.PaintESPWidgetUSEC.Color = skColor;
                            break;
                        case RadarColorOption.BEAR:
                            SKPaints.PaintBEAR.Color = skColor;
                            SKPaints.PaintMiniBEAR.Color = skColor;
                            SKPaints.TextBEAR.Color = skColor;
                            AimviewWidget.PaintESPWidgetBEAR.Color = skColor;
                            break;
                        case RadarColorOption.Focused:
                            SKPaints.PaintFocused.Color = skColor;
                            SKPaints.PaintMiniFocused.Color = skColor;
                            SKPaints.TextFocused.Color = skColor;
                            AimviewWidget.PaintESPWidgetFocused.Color = skColor;
                            break;
                        case RadarColorOption.Streamer:
                            SKPaints.PaintStreamer.Color = skColor;
                            SKPaints.PaintMiniStreamer.Color = skColor;
                            SKPaints.TextStreamer.Color = skColor;
                            AimviewWidget.PaintESPWidgetStreamer.Color = skColor;
                            break;
                        case RadarColorOption.AimbotTarget:
                            SKPaints.PaintAimbotLocked.Color = skColor;
                            SKPaints.PaintMiniAimbotLocked.Color = skColor;
                            SKPaints.TextAimbotLocked.Color = skColor;
                            AimviewWidget.PaintESPWidgetAimbotLocked.Color = skColor;
                            break;
                        case RadarColorOption.Special:
                            SKPaints.PaintSpecial.Color = skColor;
                            SKPaints.PaintMiniSpecial.Color = skColor;
                            SKPaints.TextSpecial.Color = skColor;
                            AimviewWidget.PaintESPWidgetSpecial.Color = skColor;
                            break;
                        case RadarColorOption.AI:
                            SKPaints.PaintAI.Color = skColor;
                            SKPaints.PaintMiniAI.Color = skColor;
                            SKPaints.TextAI.Color = skColor;
                            AimviewWidget.PaintESPWidgetScav.Color = skColor;
                            break;
                        case RadarColorOption.DeathMarker:
                            SKPaints.PaintDeathMarker.Color = skColor;
                            SKPaints.PaintMiniDeathMarker.Color = skColor;
                            break;
                        case RadarColorOption.ThrowablesFilterLoot:
                            SKPaints.PaintThrowableLoot.Color = skColor;
                            SKPaints.PaintMiniThrowableLoot.Color = skColor;
                            SKPaints.TextThrowableLoot.Color = skColor;
                            AimviewWidget.PaintESPWidgetThrowableLoot.Color = skColor;
                            AimviewWidget.TextESPWidgetThrowableLoot.Color = skColor;
                            break;
                        case RadarColorOption.WeaponsFilterLoot:
                            SKPaints.PaintWeaponLoot.Color = skColor;
                            SKPaints.PaintMiniWeaponLoot.Color = skColor;
                            SKPaints.TextWeaponLoot.Color = skColor;
                            AimviewWidget.PaintESPWidgetWeaponLoot.Color = skColor;
                            AimviewWidget.TextESPWidgetWeaponLoot.Color = skColor;
                            break;
                        case RadarColorOption.MedsFilterLoot:
                            SKPaints.PaintMeds.Color = skColor;
                            SKPaints.PaintMiniMeds.Color = skColor;
                            SKPaints.TextMeds.Color = skColor;
                            AimviewWidget.PaintESPWidgetMeds.Color = skColor;
                            AimviewWidget.TextESPWidgetMeds.Color = skColor;
                            break;
                        case RadarColorOption.BackpacksFilterLoot:
                            SKPaints.PaintBackpacks.Color = skColor;
                            SKPaints.PaintMiniBackpacks.Color = skColor;
                            SKPaints.TextBackpacks.Color = skColor;
                            AimviewWidget.PaintESPWidgetBackpacks.Color = skColor;
                            AimviewWidget.TextESPWidgetBackpacks.Color = skColor;
                            break;
                        case RadarColorOption.ContainerLoot:
                            SKPaints.PaintContainerLoot.Color = skColor;
                            SKPaints.PaintMiniContainerLoot.Color = skColor;
                            SKPaints.TextContainer.Color = skColor;
                            AimviewWidget.PaintESPWidgetContainers.Color = skColor;
                            AimviewWidget.TextESPWidgetContainers.Color = skColor;
                            break;
                        case RadarColorOption.RefillContainer:
                            SKPaints.PaintRefillContainer.Color = skColor;
                            SKPaints.PaintMiniRefillContainer.Color = skColor;
                            SKPaints.TextRefillContainer.Color = skColor;
                            AimviewWidget.PaintESPWidgetBackpacks.Color = skColor;
                            AimviewWidget.TextESPWidgetBackpacks.Color = skColor;
                            break;
                        case RadarColorOption.Explosives:
                            SKPaints.PaintExplosives.Color = skColor;
                            SKPaints.TextExplosives.Color = skColor;
                            break;
                        case RadarColorOption.GroupLines:
                            SKPaints.PaintConnectorGroup.Color = skColor;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Setting Radar Colors", ex);
            }
        }

        #endregion
    }
}