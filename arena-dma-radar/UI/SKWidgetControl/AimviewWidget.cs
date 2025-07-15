using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.Arena.GameWorld.Interactive;
using arena_dma_radar.Arena.Loot;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.Pages;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace arena_dma_radar.UI.SKWidgetControl
{
    public sealed class AimviewWidget : SKWidget
    {
        private SKBitmap _aimviewBitmap;
        private SKCanvas _aimviewCanvas;
        private readonly float _textOffsetY;
        private readonly float _boxHalfSize;
        private static Config Config => Program.Config;

        public AimviewWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "ESP", new SKPoint(location.Left, location.Top), new SKSize(location.Width, location.Height), scale)
        {
            _aimviewBitmap = new SKBitmap((int)location.Width, (int)location.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            _aimviewCanvas = new SKCanvas(_aimviewBitmap);
            _textOffsetY = 12.5f * scale;
            _boxHalfSize = 4f * scale;
            Minimized = minimized;
            SetScaleFactor(scale);
        }

        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;
        private static IReadOnlyCollection<ArenaPresetRefillContainer> RefillContainers => Memory.Interactive?.RefillContainers;
        private static bool InRaid => Memory.InRaid;

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (!Minimized)
                RenderESPWidget(canvas, ClientRectangle);
        }

        private void RenderESPWidget(SKCanvas parent, SKRect dest)
        {
            EnsureBitmapSize();

            _aimviewCanvas.Clear(SKColors.Transparent);

            try
            {
                var inRaid = InRaid;
                var localPlayer = LocalPlayer;

                if (inRaid && localPlayer != null)
                {
                    DrawLoot(localPlayer);
                    DrawContainers(localPlayer);
                    DrawRefillContainers(localPlayer);
                    DrawPlayers();
                    DrawCrosshair();
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CRITICAL ESP WIDGET RENDER ERROR: {ex}");
            }

            _aimviewCanvas.Flush();
            parent.DrawBitmap(_aimviewBitmap, dest, SharedPaints.PaintBitmap);
        }

        private void EnsureBitmapSize()
        {
            var size = Size;
            if (_aimviewBitmap == null || _aimviewCanvas == null ||
                _aimviewBitmap.Width != size.Width || _aimviewBitmap.Height != size.Height)
            {
                _aimviewCanvas?.Dispose();
                _aimviewCanvas = null;
                _aimviewBitmap?.Dispose();
                _aimviewBitmap = null;

                _aimviewBitmap = new SKBitmap((int)size.Width, (int)size.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                _aimviewCanvas = new SKCanvas(_aimviewBitmap);
            }
        }

        private void DrawLoot(LocalPlayer localPlayer)
        {
            var loot = Loot;
            if (loot == null) return;

            foreach (var item in loot)
            {
                var dist = Vector3.Distance(localPlayer.Position, item.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref item.Position, out var itemScrPos)) continue;

                var adjPos = ScaleESPPoint(itemScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                var paints = GetPaints(item);

                _aimviewCanvas.DrawRect(boxPt, paints.Item1);
                var label = item.GetUILabel() + $" ({dist:n1}m)";
                _aimviewCanvas.DrawText(label, textPt, paints.Item2);
            }
        }

        private void DrawContainers(LocalPlayer localPlayer)
        {
            var containers = Containers;
            if (containers == null) return;

            foreach (var container in containers)
            {
                if (container.Searched) continue;

                var dist = Vector3.Distance(localPlayer.Position, container.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref container.Position, out var containerScrPos)) continue;

                var adjPos = ScaleESPPoint(containerScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                _aimviewCanvas.DrawRect(boxPt, PaintESPWidgetContainers);
                var label = $"{container.Name} ({dist:n1}m)";
                _aimviewCanvas.DrawText(label, textPt, TextESPWidgetContainers);
            }
        }

        private void DrawRefillContainers(LocalPlayer localPlayer)
        {
            var refilContainers = RefillContainers;
            if (refilContainers == null) return;

            foreach (var refilContainer in refilContainers)
            {

                var dist = Vector3.Distance(localPlayer.Position, refilContainer.Position);
                if (dist >= 10f) continue;

                if (!CameraManagerBase.WorldToScreen(ref refilContainer.Position, out var refilContainerScrPos)) continue;

                var adjPos = ScaleESPPoint(refilContainerScrPos);
                var boxPt = new SKRect(
                    adjPos.X - _boxHalfSize,
                    adjPos.Y + _boxHalfSize,
                    adjPos.X + _boxHalfSize,
                    adjPos.Y - _boxHalfSize);

                var textPt = new SKPoint(adjPos.X, adjPos.Y + _textOffsetY);

                _aimviewCanvas.DrawRect(boxPt, PaintESPWidgetRefillContainers);
                var label = $"Refill Container ({dist:n1}m)";
                _aimviewCanvas.DrawText(label, textPt, TextESPWidgetRefillContainers);
            }
        }

        private void DrawPlayers()
        {
            var allPlayers = AllPlayers?
                .Where(x => x.IsActive && x.IsAlive && x is not Arena.ArenaPlayer.LocalPlayer);

            if (allPlayers == null) return;

            var scaleX = _aimviewBitmap.Width / (float)CameraManagerBase.Viewport.Width;
            var scaleY = _aimviewBitmap.Height / (float)CameraManagerBase.Viewport.Height;

            foreach (var player in allPlayers)
            {
                if (player.Skeleton.UpdateESPWidgetBuffer(scaleX, scaleY))
                    _aimviewCanvas.DrawPoints(SKPointMode.Lines, Skeleton.ESPWidgetBuffer, GetPlayerPaint(player));
            }
        }

        private void DrawCrosshair()
        {
            var bounds = _aimviewBitmap.Info.Rect;
            float centerX = bounds.Left + bounds.Width / 2;
            float centerY = bounds.Top + bounds.Height / 2;

            _aimviewCanvas.DrawLine(bounds.Left, centerY, bounds.Right, centerY, PaintESPWidgetCrosshair);
            _aimviewCanvas.DrawLine(centerX, bounds.Top, centerX, bounds.Bottom, PaintESPWidgetCrosshair);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SKPoint ScaleESPPoint(SKPoint original)
        {
            var scaleX = _aimviewBitmap.Width / (float)CameraManagerBase.Viewport.Width;
            var scaleY = _aimviewBitmap.Height / (float)CameraManagerBase.Viewport.Height;

            return new SKPoint(original.X * scaleX, original.Y * scaleY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerPaint(Player player)
        {
            if (player.IsAimbotLocked)
                return PaintESPWidgetAimbotLocked;

            if (player.IsFocused)
                return PaintESPWidgetFocused;

            if (player is LocalPlayer)
                return PaintESPWidgetLocalPlayer;

            switch (player.Type)
            {
                case Player.PlayerType.Teammate:
                    return PaintESPWidgetTeammate;
                case Player.PlayerType.USEC:
                    return PaintESPWidgetUSEC;
                case Player.PlayerType.BEAR:
                    return PaintESPWidgetBEAR;
                case Player.PlayerType.AI:
                    return PaintESPWidgetScav;
                case Player.PlayerType.SpecialPlayer:
                    return PaintESPWidgetSpecial;
                case Player.PlayerType.Streamer:
                    return PaintESPWidgetStreamer;
                default:
                    return PaintESPWidgetUSEC;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ValueTuple<SKPaint, SKPaint> GetPaints(LootItem loot)
        {
            if (Config.ShowThrowables && loot.IsThrowableWeapon)
                return new(PaintESPWidgetThrowableLoot, TextESPWidgetThrowableLoot);
            if (Config.ShowWeapons && loot.IsWeapon)
                return new(PaintESPWidgetWeaponLoot, TextESPWidgetWeaponLoot);
            if (Config.ShowMeds && loot.IsMeds)
                return new(PaintESPWidgetMeds, TextESPWidgetMeds);
            if (Config.ShowBackpacks && loot.IsBackpack)
                return new(PaintESPWidgetBackpacks, TextESPWidgetBackpacks);

            return new(PaintESPWidgetDefaultLoot, TextESPWidgetDefaultLoot);
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);

            lock (PaintESPWidgetCrosshair)
            {
                PaintESPWidgetCrosshair.StrokeWidth = 1 * newScale;
                PaintESPWidgetLocalPlayer.StrokeWidth = 1 * newScale;
                PaintESPWidgetUSEC.StrokeWidth = 1 * newScale;
                PaintESPWidgetBEAR.StrokeWidth = 1 * newScale;
                PaintESPWidgetSpecial.StrokeWidth = 1 * newScale;
                PaintESPWidgetStreamer.StrokeWidth = 1 * newScale;
                PaintESPWidgetTeammate.StrokeWidth = 1 * newScale;
                PaintESPWidgetBoss.StrokeWidth = 1 * newScale;
                PaintESPWidgetAimbotLocked.StrokeWidth = 1 * newScale;
                PaintESPWidgetScav.StrokeWidth = 1 * newScale;
                PaintESPWidgetRaider.StrokeWidth = 1 * newScale;
                PaintESPWidgetPScav.StrokeWidth = 1 * newScale;
                PaintESPWidgetFocused.StrokeWidth = 1 * newScale;
                PaintESPWidgetThrowableLoot.StrokeWidth = 0.75f * newScale;
                PaintESPWidgetWeaponLoot.StrokeWidth = 0.75f * newScale;
                PaintESPWidgetMeds.StrokeWidth = 0.75f * newScale;
                PaintESPWidgetBackpacks.StrokeWidth = 0.75f * newScale;
                PaintESPWidgetContainers.StrokeWidth = 0.75f * newScale;
                PaintESPWidgetRefillContainers.StrokeWidth = 0.75f * newScale;
                TextESPWidgetThrowableLoot.TextSize = 9f * newScale;
                TextESPWidgetWeaponLoot.TextSize = 9f * newScale;
                TextESPWidgetMeds.TextSize = 9f * newScale;
                TextESPWidgetBackpacks.TextSize = 9f * newScale;
                TextESPWidgetContainers.TextSize = 9f * newScale;
                TextESPWidgetRefillContainers.TextSize = 9f * newScale;
            }
        }

        public override void Dispose()
        {
            _aimviewBitmap?.Dispose();
            _aimviewCanvas?.Dispose();
            _aimviewBitmap = null;
            _aimviewCanvas = null;
            base.Dispose();
        }

        #region Mini ESP Paints
        private static SKPaint PaintESPWidgetCrosshair { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        internal static SKPaint PaintESPWidgetLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetUSEC { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetScav { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetPScav { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetFocused { get; } = new()
        {
            Color = SKColors.Coral,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        internal static SKPaint PaintESPWidgetDefaultLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetDefaultLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.WhiteSmoke,
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint PaintESPWidgetThrowableLoot { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetThrowableLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Orange,
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint PaintESPWidgetWeaponLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetWeaponLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.WhiteSmoke,
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint PaintESPWidgetMeds { get; } = new()
        {
            Color = SKColors.LightSalmon,
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetMeds { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.LightSalmon,
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint PaintESPWidgetBackpacks { get; } = new()
        {
            Color = SKColor.Parse("00b02c"),
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetBackpacks { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("00b02c"),
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint PaintESPWidgetContainers { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetContainers { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("FFFFCC"),
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint PaintESPWidgetRefillContainers { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            StrokeWidth = 0.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        internal static SKPaint TextESPWidgetRefillContainers { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("FFFFCC"),
            IsStroke = false,
            TextSize = 9f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.High
        };

        #endregion
    }
}