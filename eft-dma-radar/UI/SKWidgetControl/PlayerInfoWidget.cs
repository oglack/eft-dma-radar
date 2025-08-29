using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public sealed class PlayerInfoWidget : SKWidget
    {
        private static Config Config => Program.Config;
        private readonly float _padding;
        private readonly List<(float TopY, float BottomY, string PlayerName)> _playerRows = new();

        /// <summary>
        /// Constructs a Player Info Overlay.
        /// </summary>
        public PlayerInfoWidget(SKGLElement parent, SKRect location, bool minimized, float scale)
            : base(parent, "Player Info", new SKPoint(location.Left, location.Top),
                new SKSize(location.Width, location.Height), scale, false)
        {
            Minimized = minimized;
            _padding = 2.5f * scale;
            SetScaleFactor(scale);
        }

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
        }

        public void Draw(SKCanvas canvas, Player localPlayer, IEnumerable<Player> players)
        {
            if (Minimized)
            {
                base.Draw(canvas);
                return;
            }

            var localPlayerPos = localPlayer.Position;
            var hostiles = players.Where(x => x.IsHostileActive).ToList();
            var hostileCount = hostiles.Count;
            var pmcCount = hostiles.Count(x => x.IsPmc);
            var pscavCount = hostiles.Count(x => x.Type is Player.PlayerType.PScav);
            var aiCount = hostiles.Count(x => x.IsAI && x.Type is not Player.PlayerType.AIBoss);
            var bossCount = hostiles.Count(x => x.Type is Player.PlayerType.AIBoss);

            var filteredPlayers = players.Where(x => x.IsHumanHostileActive)
                .OrderBy(x => Vector3.Distance(localPlayerPos, x.Position))
                .ToList();

            _playerRows.Clear();

            var sb = new StringBuilder();

            sb.AppendFormat("{0,-25}", "Fac / P / Lvl / Name")
              .AppendFormat("{0,-15}", "Last Updated")
              .AppendFormat("{0,-5}", "Acct");

            if (Config.AlternateProfileService)
                sb.AppendFormat("{0,-10}", "Streamer");

            sb.AppendFormat("{0,-7}", "K/D")
              .AppendFormat("{0,-7}", "Hours")
              .AppendFormat("{0,-6}", "Raids")
              .AppendFormat("{0,-6}", "S/R%")
              .AppendFormat("{0,-5}", "Grp")
              .AppendFormat("{0,-8}", "Value")
              .AppendFormat("{0,-30}", "In Hands")
              .AppendFormat("{0,-5}", "Dist")
              .AppendLine();

            foreach (var player in filteredPlayers)
            {
                AppendPlayerData(sb, player, localPlayerPos);
            }

            var data = sb.ToString()
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var lineSpacing = _textPlayersOverlay.FontSpacing;
            var maxLength = 0f;

            foreach (var line in data)
            {
                var lineLength = _textPlayersOverlay.MeasureText(line);
                if (lineLength > maxLength)
                    maxLength = lineLength;
            }

            Size = new SKSize(maxLength + _padding * 2, data.Count * lineSpacing + _padding * 1.5f);
            Location = Location;

            // Set clean title and right-side info
            Title = "Player Info";
            RightTitleInfo = $"Hostiles: {hostileCount} | PMC: {pmcCount} | PScav: {pscavCount} | AI: {aiCount} | Boss: {bossCount}";

            base.Draw(canvas);

            var drawPt = new SKPoint(ClientRectangle.Left + _padding, ClientRectangle.Top + lineSpacing / 2 + _padding);

            for (int i = 0; i < data.Count; i++)
            {
                var line = data[i];

                canvas.DrawText(line, drawPt, _textPlayersOverlay);

                if (i > 0 && i < data.Count - 1)
                {
                    var lineY = drawPt.Y + (lineSpacing * 0.2f);
                    var lineStartX = ClientRectangle.Left + _padding;
                    var lineEndX = ClientRectangle.Right - _padding;

                    canvas.DrawLine(lineStartX, lineY, lineEndX, lineY, _rowSeparatorPaint);
                }

                if (i > 0 && (i - 1) < filteredPlayers.Count)
                {
                    var topY = drawPt.Y - lineSpacing;
                    var bottomY = drawPt.Y;
                    var name = filteredPlayers[i - 1].Name;

                    _playerRows.Add((topY, bottomY, name));
                }

                drawPt.Y += lineSpacing;
            }

            if (data.Count > 1)
            {
                var headerSeparatorY = ClientRectangle.Top + lineSpacing / 2 + _padding + lineSpacing * 0.2f;
                var lineStartX = ClientRectangle.Left + _padding;
                var lineEndX = ClientRectangle.Right - _padding;

                canvas.DrawLine(lineStartX, headerSeparatorY, lineEndX, headerSeparatorY, _headerSeparatorPaint);
            }
        }

        private void AppendPlayerData(StringBuilder sb, Player player, Vector3 localPlayerPos)
        {
            var playerTypeKey = player.DeterminePlayerTypeKey();
            var typeSettings = Config.PlayerTypeSettings.GetSettings(playerTypeKey);

            var name = Config.MaskNames && player.IsHuman ? "<Hidden>" : player.Name;
            var faction = player.PlayerSide.GetDescription()[0];
            var hands = $"{player.Hands?.CurrentItem}/{player.Hands?.CurrentAmmo}";
            var inHands = hands is not null ? hands : "--";

            string edition = "--";
            string level = "0";
            string prestige = "0";
            string kd = "--";
            string raidCount = "--";
            string survivePercent = "--";
            string hours = "--";
            string lastUpdated = "--";
            string isStreamer = "--";

            if (player is ObservedPlayer observed)
            {
                edition = observed.Profile?.Acct ?? "--";
                if (observed.Profile?.Level is int levelResult)
                    level = levelResult.ToString();
                if (observed.Profile?.Overall_KD is float kdResult)
                    kd = kdResult.ToString("n2");
                if (observed.Profile?.RaidCount is int raidCountResult)
                    raidCount = raidCountResult.ToString();
                if (observed.Profile?.SurvivedRate is float survivedResult)
                    survivePercent = survivedResult.ToString("n1");
                if (observed.Profile?.Hours is int hoursResult)
                    hours = hoursResult.ToString();
                if (observed.Profile?.Prestige is int prestigeResult)
                    prestige = prestigeResult.ToString();

                if (Config.AlternateProfileService)
                {
                    if (observed.Profile?.LastUpdatedReadable is string lastUpdatedResult)
                        lastUpdated = lastUpdatedResult;
                }
                else
                {
                    if (observed.Profile?.Updated is string lastUpdatedResult)
                        lastUpdated = lastUpdatedResult;
                }

                if (Config.AlternateProfileService)
                {
                    if (observed.Profile?.IsStreamer is bool isStreamerResult)
                        isStreamer = isStreamerResult.ToString();
                }
                else
                    isStreamer = "N/A";
            }

            var grp = player.GroupID != -1 ? player.GroupID.ToString() : "--";
            var focused = player.IsFocused ? "*" : null;
            var distance = (int)Math.Round(Vector3.Distance(localPlayerPos, player.Position));

            sb.AppendFormat("{0,-25}", $"{focused}{faction}/P{prestige}/L{level}:{name}")
              .AppendFormat("{0,-15}", lastUpdated)
              .AppendFormat("{0,-5}", edition);

            if (Config.AlternateProfileService)
                sb.AppendFormat("{0,-10}", isStreamer);

            sb.AppendFormat("{0,-7}", kd)
              .AppendFormat("{0,-7}", hours)
              .AppendFormat("{0,-6}", raidCount)
              .AppendFormat("{0,-6}", survivePercent)
              .AppendFormat("{0,-5}", grp)
              .AppendFormat("{0,-8}", TarkovMarketItem.FormatPrice(player.Gear?.Value ?? 0))
              .AppendFormat("{0,-30}", inHands)
              .AppendFormat("{0,-5}", distance)
              .AppendLine();
        }

        public override void SetScaleFactor(float newScale)
        {
            base.SetScaleFactor(newScale);

            lock (_textPlayersOverlay)
            {
                _textPlayersOverlay.TextSize = 12 * newScale;
            }

            _rowSeparatorPaint.StrokeWidth = 1.0f * newScale;
            _headerSeparatorPaint.StrokeWidth = 1.5f * newScale;
        }

        public override bool HandleClientAreaClick(SKPoint point)
        {
            foreach (var row in _playerRows)
            {
                if (point.Y >= row.TopY && point.Y <= row.BottomY)
                {
                    MainWindow.Window.WatchlistControl.AddPlayerDirectly(row.PlayerName);
                    return true;
                }
            }

            return false;
        }

        private static readonly SKPaint _textPlayersOverlay = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas"), // Do NOT change this font
            FilterQuality = SKFilterQuality.High
        };

        private static readonly SKPaint _rowSeparatorPaint = new()
        {
            Color = SKColors.LightGray.WithAlpha(160),
            StrokeWidth = 1.0f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _headerSeparatorPaint = new()
        {
            Color = SKColors.LightGray.WithAlpha(200),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
    }
}