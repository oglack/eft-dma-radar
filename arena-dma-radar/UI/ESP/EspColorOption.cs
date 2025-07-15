using arena_dma_radar.UI.Misc;

namespace arena_dma_radar.UI
{
    public enum EspColorOption
    {
        Friendly,
        USEC,
        BEAR,
        Focused,
        Streamer,
        AimbotTarget,
        Special,
        AI,
        ThrowablesFilterLoot,
        WeaponsFilterLoot,
        MedsFilterLoot,
        BackpacksFilterLoot,
        ContainerLoot,
        RefillContainer,
        Explosives,
        FPS,
        RaidStats,
        StatusText,
        MagazineInfo,
        Crosshair,
        FireportAim,
        AimbotFOV,
        AimbotLock,
        ClosestPlayer,
        MiniRadarTheme
    }

    internal static class EspColorOptions
    {
        #region Static Interfaces

        /// <summary>
        /// Load all ESP Color Config. Run once at start of application.
        /// </summary>
        internal static void LoadColors(Config config)
        {
            config.ESP.Colors ??= new Dictionary<EspColorOption, string>();
            foreach (var defaultColor in GetDefaultColors())
                config.ESP.Colors.TryAdd(defaultColor.Key, defaultColor.Value);
            SetColors(config.ESP.Colors);
        }

        /// <summary>
        /// Returns all default color combinations for ESP.
        /// </summary>
        internal static Dictionary<EspColorOption, string> GetDefaultColors() =>
            new()
            {
                [EspColorOption.Friendly] = SKColors.LimeGreen.ToString(),
                [EspColorOption.USEC] = SKColors.Red.ToString(),
                [EspColorOption.BEAR] = SKColors.Blue.ToString(),
                [EspColorOption.Focused] = SKColors.Coral.ToString(),
                [EspColorOption.Streamer] = SKColors.MediumPurple.ToString(),
                [EspColorOption.AimbotTarget] = SKColor.Parse("4654ff").ToString(), // Blue doesnt work on some fusers
                [EspColorOption.Special] = SKColors.MediumPurple.ToString(),
                [EspColorOption.AI] = SKColors.Yellow.ToString(),
                [EspColorOption.ThrowablesFilterLoot] = SKColors.Orange.ToString(),
                [EspColorOption.WeaponsFilterLoot] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.MedsFilterLoot] = SKColors.LightSalmon.ToString(),
                [EspColorOption.BackpacksFilterLoot] = SKColor.Parse("00b02c").ToString(),
                [EspColorOption.ContainerLoot] = SKColor.Parse("FFFFCC").ToString(),
                [EspColorOption.RefillContainer] = SKColor.Parse("FFFFCC").ToString(),
                [EspColorOption.Explosives] = SKColors.OrangeRed.ToString(),
                [EspColorOption.FPS] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.RaidStats] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.StatusText] = SKColors.Red.ToString(),
                [EspColorOption.MagazineInfo] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.Crosshair] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.FireportAim] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.AimbotFOV] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.AimbotLock] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.ClosestPlayer] = SKColors.WhiteSmoke.ToString(),
                [EspColorOption.MiniRadarTheme] = SKColors.WhiteSmoke.ToString()
            };

        /// <summary>
        /// Save all ESP Color Changes.
        /// </summary>
        internal static void SetColors(IReadOnlyDictionary<EspColorOption, string> colors)
        {
            try
            {
                foreach (var color in colors)
                {
                    if (!SKColor.TryParse(color.Value, out var skColor))
                        throw new Exception($"Invalid Color Value for {color.Key}!");
                    switch (color.Key)
                    {
                        case EspColorOption.Friendly:
                            SKPaints.PaintFriendlyESP.Color = skColor;
                            SKPaints.TextFriendlyESP.Color = skColor;
                            break;
                        case EspColorOption.USEC:
                            SKPaints.PaintUSECESP.Color = skColor;
                            SKPaints.TextUSECESP.Color = skColor;
                            break;
                        case EspColorOption.BEAR:
                            SKPaints.PaintBEARESP.Color = skColor;
                            SKPaints.TextBEARESP.Color = skColor;
                            break;
                        case EspColorOption.Focused:
                            SKPaints.PaintFocusedESP.Color = skColor;
                            SKPaints.TextFocusedESP.Color = skColor;
                            break;
                        case EspColorOption.Streamer:
                            SKPaints.PaintStreamerESP.Color = skColor;
                            SKPaints.TextStreamerESP.Color = skColor;
                            break;
                        case EspColorOption.AimbotTarget:
                            SKPaints.PaintAimbotLockedESP.Color = skColor;
                            SKPaints.TextAimbotLockedESP.Color = skColor;
                            break;
                        case EspColorOption.Special:
                            SKPaints.PaintSpecialESP.Color = skColor;
                            SKPaints.TextSpecialESP.Color = skColor;
                            break;
                        case EspColorOption.AI:
                            SKPaints.PaintAIESP.Color = skColor;
                            SKPaints.TextAIESP.Color = skColor;
                            break;
                        case EspColorOption.ThrowablesFilterLoot:
                            SKPaints.PaintThrowableLootESP.Color = skColor;
                            SKPaints.TextThrowableLootESP.Color = skColor;
                            break;
                        case EspColorOption.WeaponsFilterLoot:
                            SKPaints.PaintWeaponLootESP.Color = skColor;
                            SKPaints.TextWeaponLootESP.Color = skColor;
                            break;
                        case EspColorOption.MedsFilterLoot:
                            SKPaints.PaintMedsESP.Color = skColor;
                            SKPaints.TextMedsESP.Color = skColor;
                            break;
                        case EspColorOption.BackpacksFilterLoot:
                            SKPaints.PaintBackpacksESP.Color = skColor;
                            SKPaints.TextBackpacksESP.Color = skColor;
                            break;
                        case EspColorOption.ContainerLoot:
                            SKPaints.PaintContainerLootESP.Color = skColor;
                            SKPaints.TextContainerLootESP.Color = skColor;
                            break;
                        case EspColorOption.RefillContainer:
                            SKPaints.PaintRefillContainerESP.Color = skColor;
                            SKPaints.TextRefillContainerESP.Color = skColor;
                            break;
                        case EspColorOption.Explosives:
                            SKPaints.PaintExplosiveESP.Color = skColor;
                            SKPaints.PaintExplosiveRadiusESP.Color = skColor;
                            SKPaints.TextExplosiveESP.Color = skColor;
                            break;
                        case EspColorOption.FPS:
                            SKPaints.TextESPFPS.Color = skColor;
                            break;
                        case EspColorOption.RaidStats:
                            SKPaints.TextESPRaidStats.Color = skColor;
                            break;
                        case EspColorOption.StatusText:
                            SKPaints.TextESPStatusText.Color = skColor;
                            break;
                        case EspColorOption.MagazineInfo:
                            SKPaints.TextMagazineESP.Color = skColor;
                            SKPaints.TextMagazineInfoESP.Color = skColor;
                            break;
                        case EspColorOption.Crosshair:
                            SKPaints.PaintCrosshairESP.Color = skColor;
                            SKPaints.PaintCrosshairESPDot.Color = skColor;
                            break;
                        case EspColorOption.FireportAim:
                            SKPaints.PaintFireportAimESP.Color = skColor;
                            break;
                        case EspColorOption.AimbotFOV:
                            SKPaints.PaintAimbotFOVESP.Color = skColor;
                            break;
                        case EspColorOption.AimbotLock:
                            SKPaints.PaintAimbotLockedLineESP.Color = skColor;
                            break;
                        case EspColorOption.ClosestPlayer:
                            SKPaints.TextESPClosestPlayer.Color = skColor;
                            break;
                        case EspColorOption.MiniRadarTheme:
                            SKPaints.PaintMiniRadarOutlineESP.Color = skColor;
                            SKPaints.PaintMiniRadarResizeHandleESP.Color = skColor;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Setting ESP Colors", ex);
            }
        }

        #endregion
    }
}