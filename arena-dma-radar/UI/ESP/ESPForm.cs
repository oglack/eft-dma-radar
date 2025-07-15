using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.Features.MemoryWrites;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.Arena.GameWorld.Interactive;
using arena_dma_radar.Arena.Loot;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.Pages;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Point = System.Drawing.Point;
using RectFSer = arena_dma_radar.UI.Misc.RectFSer;
using Size = System.Drawing.Size;

namespace arena_dma_radar.UI.ESP
{
    public partial class ESPForm : Form
    {
        #region Fields/Properties/Constructor

        public static bool ShowESP = true;
        private readonly Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;
        private int _fpsCounter;
        private int _fps;

        private string _lastStatusText = "";
        private string _lastMagazineText = "";
        private string _lastClosestPlayerText = "";
        private string _lastFPSText = "";

        // ghetto but used for optimising status text checking
        private bool _lastAimEnabled = false;
        private bool _lastRageMode = false;
        private bool _lastWideLeanEnabled = false;
        private bool _lastMoveSpeedEnabled = false;

        private SKPoint _fpsOffset = SKPoint.Empty;
        private SKPoint _statusTextOffset = SKPoint.Empty;
        private SKPoint _magazineOffset = SKPoint.Empty;
        private SKPoint _raidStatsOffset = SKPoint.Empty;
        private SKPoint _closestPlayerOffset = SKPoint.Empty;

        private Rectangle _lastViewport = Rectangle.Empty;
        private Size _lastControlSize = Size.Empty;

        private float ScaledHitTestPadding => 3f * Config.ESP.FontScale;

        private readonly Dictionary<UIElement, CachedBounds> _boundsCache = new();
        private readonly Dictionary<UIElement, UIElementInfo> _uiElements = new();
        private int _lastFrameBounds = 0;

        private readonly DragState _dragState = new();

        private const float RADAR_PLAYER_SIZE = 4f;
        private const float RADAR_LOOT_SIZE = 3f;
        private const float RADAR_AIMLINE_LENGTH = 12f;
        private const float RADAR_AIMLINE_WIDTH = 2f;

        private volatile bool _espIsRendering = false;

        private SKGLControl skglControl_ESP;

        private readonly ConcurrentBag<SKPath> _pathPool = new ConcurrentBag<SKPath>();

        /// <summary>
        /// Singleton Instance of EspForm.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal static ESPForm Window { get; private set; }

        /// <summary>
        ///  App Config.
        /// </summary>
        private static Config Config => Program.Config;

        /// <summary>
        ///  App Config.
        /// </summary>
        public static ESPConfig ESPConfig { get; } = Config.ESP;

        /// <summary>
        /// True if ESP Window is Fullscreen.
        /// </summary>
        public bool IsFullscreen =>
            FormBorderStyle is FormBorderStyle.None;

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory.MapID;
                id ??= "MAPDEFAULT";
                return id;
            }
        }

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;

        /// <summary>
        /// Contains all 'Hot' grenades in Local Game World, and their position(s).
        /// </summary>
        private static IReadOnlyCollection<Grenade> Grenades => Memory.Grenades;

        /// <summary>
        /// Contains all Refill Containers in Local Game World, and their position(s).
        /// </summary>
        private static IReadOnlyCollection<ArenaPresetRefillContainer> RefillContainers => Memory.Interactive?.RefillContainers;

        /// <summary>
        /// Contains all filtered loot in Local Game World.
        /// </summary>
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// Contains all static containers in Local Game World.
        /// </summary>
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        public ESPForm()
        {
            InitializeComponent();

            skglControl_ESP = new SKGLControl();
            skglControl_ESP.Name = "skglControl_ESP";
            skglControl_ESP.BackColor = Color.Black;
            skglControl_ESP.Dock = DockStyle.Fill;
            skglControl_ESP.Location = new Point(0, 0);
            skglControl_ESP.Margin = new Padding(4, 3, 4, 3);
            skglControl_ESP.Size = new Size(624, 441);
            skglControl_ESP.TabIndex = 0;
            skglControl_ESP.VSync = false;

            skglControl_ESP.MouseDown += ESPForm_MouseDown;
            skglControl_ESP.MouseMove += ESPForm_MouseMove;
            skglControl_ESP.MouseUp += ESPForm_MouseUp;

            this.Controls.Add(skglControl_ESP);

            CenterToScreen();
            skglControl_ESP.DoubleClick += ESPForm_DoubleClick;
            _fpsSw.Start();

            var allScreens = Screen.AllScreens;
            if (ESPConfig.AutoFullscreen && ESPConfig.SelectedScreen < allScreens.Length)
            {
                var screen = allScreens[ESPConfig.SelectedScreen];
                var bounds = screen.Bounds;
                FormBorderStyle = FormBorderStyle.None;
                Location = new Point(bounds.Left, bounds.Top);
                Size = CameraManagerBase.Viewport.Size;
            }

            LoadUIPositions();
            InitializeUIElements();

            var interval = ESPConfig.FPSCap == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1000d / ESPConfig.FPSCap);

            _renderTimer = new PrecisionTimer(interval);

            this.Shown += ESPForm_Shown;
        }

        private async void ESPForm_Shown(object sender, EventArgs e)
        {
            while (!this.IsHandleCreated)
                await Task.Delay(25);

            Window ??= this;
            CameraManagerBase.EspRunning = true;

            _renderTimer.Start();

            skglControl_ESP.PaintSurface += ESPForm_PaintSurface;
            _renderTimer.Elapsed += RenderTimer_Elapsed;
        }

        private void RenderTimer_Elapsed(object sender, EventArgs e)
        {
            if (_espIsRendering || this.IsDisposed) return;

            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (_espIsRendering || this.IsDisposed) return;

                    _espIsRendering = true;
                    try
                    {
                        skglControl_ESP.Invalidate();
                    }
                    finally
                    {
                        _espIsRendering = false;
                    }
                }));
            }
            catch
            {
                _espIsRendering = false;
            }
        }

        #endregion

        #region Resource Management

        private SKPath GetPath()
        {
            if (_pathPool.TryTake(out var path))
            {
                path.Reset();
                return path;
            }
            return new SKPath();
        }

        private void ReturnPath(SKPath path)
        {
            if (path != null)
            {
                path.Reset();
                _pathPool.Add(path);
            }
        }

        #endregion

        #region Form Methods

        private void LoadUIPositions()
        {
            _radarRect = new SKRect(ESPConfig.RadarRect.Left, ESPConfig.RadarRect.Top,
                                   ESPConfig.RadarRect.Right, ESPConfig.RadarRect.Bottom);
            _magazineOffset = new SKPoint(ESPConfig.MagazineOffset.X, ESPConfig.MagazineOffset.Y);
            _statusTextOffset = new SKPoint(ESPConfig.StatusTextOffset.X, ESPConfig.StatusTextOffset.Y);
            _raidStatsOffset = new SKPoint(ESPConfig.RaidStatsOffset.X, ESPConfig.RaidStatsOffset.Y);
            _fpsOffset = new SKPoint(ESPConfig.FPSOffset.X, ESPConfig.FPSOffset.Y);
            _closestPlayerOffset = new SKPoint(ESPConfig.ClosestPlayerOffset.X, ESPConfig.ClosestPlayerOffset.Y);
        }

        private void SaveUIPositions()
        {
            ESPConfig.RadarRect = new RectFSer(_radarRect.Left, _radarRect.Top, _radarRect.Right, _radarRect.Bottom);

            ESPConfig.MagazineOffset = new PointFSer(_uiElements[UIElement.Magazine].Offset.X, _uiElements[UIElement.Magazine].Offset.Y);
            ESPConfig.StatusTextOffset = new PointFSer(_uiElements[UIElement.StatusText].Offset.X, _uiElements[UIElement.StatusText].Offset.Y);
            ESPConfig.RaidStatsOffset = new PointFSer(_uiElements[UIElement.RaidStats].Offset.X, _uiElements[UIElement.RaidStats].Offset.Y);
            ESPConfig.FPSOffset = new PointFSer(_uiElements[UIElement.FPS].Offset.X, _uiElements[UIElement.FPS].Offset.Y);
            ESPConfig.ClosestPlayerOffset = new PointFSer(_uiElements[UIElement.ClosestPlayer].Offset.X, _uiElements[UIElement.ClosestPlayer].Offset.Y);

            Config.SaveAsync();
        }

        public void UpdateRenderTimerInterval(int targetFPS)
        {
            var interval = TimeSpan.FromMilliseconds(1000d / targetFPS);
            _renderTimer.Interval = interval;
        }

        /// <summary>
        /// Purge SkiaSharp Resources.
        /// </summary>
        public void PurgeSKResources()
        {
            if (this.IsDisposed) return;

            this.Invoke(() =>
            {
                skglControl_ESP?.GRContext?.PurgeResources();
            });
        }

        /// <summary>
        /// Toggles Full Screen mode for ESP Window.
        /// </summary>
        private void SetFullscreen(bool toFullscreen)
        {
            const int minWidth = 640;
            const int minHeight = 480;
            var screen = Screen.FromControl(this);
            Rectangle view;

            if (toFullscreen)
            {
                FormBorderStyle = FormBorderStyle.None;
                view = CameraManagerBase.Viewport;

                if (view.Width < minWidth)
                    view.Width = minWidth;
                if (view.Height < minHeight)
                    view.Height = minHeight;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                view = new Rectangle(screen.Bounds.X, screen.Bounds.Y, minWidth, minHeight);
            }

            WindowState = FormWindowState.Normal;
            Location = new Point(screen.Bounds.Left, screen.Bounds.Top);
            Width = view.Width;
            Height = view.Height;

            if (!toFullscreen)
                CenterToScreen();

            InvalidateBoundsCache();
        }

        /// <summary>
        /// Record the Rendering FPS.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFPS()
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _fps = Interlocked.Exchange(ref _fpsCounter, 0); // Get FPS -> Reset FPS counter
                _fpsSw.Restart();
            }
            else
            {
                _fpsCounter++;
            }
        }

        /// <summary>
        /// Handle double click even on ESP Window (toggles fullscreen).
        /// </summary>
        private void ESPForm_DoubleClick(object sender, EventArgs e) => SetFullscreen(!IsFullscreen);

        private void ESPForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            CheckRenderContextChanges();
            var point = new SKPoint(e.X, e.Y);
            _dragState.Reset();
            _dragState.StartPoint = point;

            if (IsElementVisible(UIElement.Radar) && IsNearCorner(point, _radarRect))
            {
                _dragState.Target = DragTarget.RadarResize;
                _dragState.OriginalRect = _radarRect;
            }
            else if (IsElementVisible(UIElement.Radar) && _radarRect.Contains(point))
            {
                _dragState.Target = DragTarget.RadarMove;
                _dragState.OriginalRect = _radarRect;
            }
            else
            {
                foreach (var kvp in _uiElements)
                {
                    if (IsElementVisible(kvp.Key) && IsNearElement(point, kvp.Key))
                    {
                        _dragState.Target = (DragTarget)kvp.Key;
                        _dragState.OriginalOffset = kvp.Value.Offset;
                        break;
                    }
                }
            }

            UpdateCursor(point);
        }

        private void ESPForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (_dragState.Target != DragTarget.None && _dragState.Target != DragTarget.RadarMove && _dragState.Target != DragTarget.RadarResize)
            {
                var element = (UIElement)_dragState.Target;
                InvalidateElementCache(element);
            }
            else if (_dragState.Target == DragTarget.RadarMove || _dragState.Target == DragTarget.RadarResize)
            {
                InvalidateElementCache(UIElement.Radar);
            }

            _dragState.Reset();
            UpdateCursor(new SKPoint(e.X, e.Y));
        }

        private void ESPForm_MouseMove(object sender, MouseEventArgs e)
        {
            var point = new SKPoint(e.X, e.Y);

            if (!_dragState.IsActive)
            {
                CheckRenderContextChanges();
                UpdateCursor(point);
                return;
            }

            var delta = point - _dragState.StartPoint;
            ApplyDragMovement(delta);
            Invalidate();
        }

        /// <summary>
        /// Main ESP Render Event.
        /// </summary>
        private void ESPForm_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            SetFPS();
            SkiaResourceTracker.TrackESPFrame();
            canvas.Clear(InterfaceColorOptions.FuserBackgroundColor);
            try
            {
                var localPlayer = LocalPlayer;
                var allPlayers = AllPlayers;
                if (localPlayer is not null && allPlayers is not null)
                {
                    if (!ShowESP)
                    {
                        DrawNotShown(canvas);
                    }
                    else
                    {
                        var battleMode = Config.BattleMode;

                        if (!battleMode && LootItem.LootESPSettings.Enabled)
                            DrawLoot(canvas, localPlayer);
                        if (!battleMode && StaticLootContainer.ESPSettings.Enabled)
                            DrawContainers(canvas, localPlayer);
                        if (!battleMode && ArenaPresetRefillContainer.ESPSettings.Enabled)
                            DrawRefillContainers(canvas, localPlayer);
                        if (Grenade.ESPSettings.Enabled)
                            DrawExplosives(canvas, localPlayer);
                        foreach (var player in allPlayers)
                            player.DrawESP(canvas, localPlayer);
                        if (ESPConfig.ShowRaidStats)
                            DrawRaidStats(canvas, allPlayers);
                        if (ESPConfig.ShowAimFOV && MemWriteFeature<Aimbot>.Instance.Enabled)
                            DrawAimFOV(canvas);
                        if (ESPConfig.ShowFPS)
                            DrawFPS(canvas);
                        if (ESPConfig.ShowMagazine)
                            DrawMagazine(canvas, localPlayer);
                        if (ESPConfig.ShowFireportAim &&
                            !CameraManagerBase.IsADS &&
                            !(ESP.Config.ShowAimLock && MemWriteFeature<Aimbot>.Instance.Cache?.AimbotLockedPlayer is not null))
                            DrawFireportAim(canvas, localPlayer);
                        if (ESPConfig.ShowStatusText)
                            DrawStatusText(canvas);
                        if (ESPConfig.Crosshair.Enabled)
                            DrawCrosshair(canvas);
                        if (ESPConfig.ShowClosestPlayer)
                            DrawClosestPlayer(canvas, localPlayer);
                        if (ESPConfig.MiniRadar.Enabled)
                            DrawRadar(canvas, localPlayer);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ESP RENDER CRITICAL ERROR: {ex}");
            }
            canvas.Flush();
        }

        /// <summary>
        /// Draws a crosshair at the center of the screen based on selected style.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawCrosshair(SKCanvas canvas)
        {
            if (skglControl_ESP.Width <= 0 || skglControl_ESP.Height <= 0 || !ESPConfig.Crosshair.Enabled)
                return;

            var centerX = skglControl_ESP.Width / 2f;
            var centerY = skglControl_ESP.Height / 2f;
            var size = 10 * ESPConfig.Crosshair.Scale;
            var thickness = 2 * ESPConfig.Crosshair.Scale;
            var dotSize = 3 * ESPConfig.Crosshair.Scale;

            switch (ESPConfig.Crosshair.Type)
            {
                case 0: // Plus (+)
                    canvas.DrawLine(centerX - size, centerY, centerX + size, centerY, SKPaints.PaintCrosshairESP);
                    canvas.DrawLine(centerX, centerY - size, centerX, centerY + size, SKPaints.PaintCrosshairESP);
                    break;
                case 1: // Cross (X)
                    canvas.DrawLine(centerX - size, centerY - size, centerX + size, centerY + size, SKPaints.PaintCrosshairESP);
                    canvas.DrawLine(centerX + size, centerY - size, centerX - size, centerY + size, SKPaints.PaintCrosshairESP);
                    break;
                case 2: // Circle
                    canvas.DrawCircle(centerX, centerY, size, SKPaints.PaintCrosshairESP);
                    break;
                case 3: // Dot
                    canvas.DrawCircle(centerX, centerY, dotSize, SKPaints.PaintCrosshairESPDot);
                    break;
                case 4: // Square
                    var rect = new SKRect(centerX - size, centerY - size, centerX + size, centerY + size);
                    canvas.DrawRect(rect, SKPaints.PaintCrosshairESP);
                    break;
                case 5: // Diamond
                    var path = GetPath();
                    path.MoveTo(centerX, centerY - size);
                    path.LineTo(centerX + size, centerY);
                    path.LineTo(centerX, centerY + size);
                    path.LineTo(centerX - size, centerY);
                    path.Close();
                    canvas.DrawPath(path, SKPaints.PaintCrosshairESP);
                    ReturnPath(path);
                    break;
            }
        }

        /// <summary>
        /// Draw status text on ESP Window (top middle of screen).
        /// </summary>
        /// <param name="canvas"></param>
        private void DrawStatusText(SKCanvas canvas)
        {
            try
            {
                var currentStatusText = GenerateCurrentStatusText();

                if (currentStatusText != _lastStatusText)
                {
                    _lastStatusText = currentStatusText;
                    InvalidateElementCache(UIElement.StatusText);
                }

                if (string.IsNullOrEmpty(_lastStatusText))
                    return;

                var clientArea = skglControl_ESP.ClientRectangle;
                var labelWidth = SKPaints.TextESPStatusText.MeasureText(_lastStatusText);
                var spacing = 1f * ESPConfig.FontScale;
                var top = clientArea.Top + spacing;
                var labelHeight = SKPaints.TextESPStatusText.FontSpacing;

                var anchorX = clientArea.Width / 2 + _statusTextOffset.X;
                var anchorY = top + _statusTextOffset.Y;

                var bgRect = new SKRect(
                    anchorX - labelWidth / 2,
                    anchorY,
                    anchorX + labelWidth / 2,
                    anchorY + labelHeight + spacing);

                canvas.DrawRect(bgRect, SKPaints.PaintTransparentBacker);

                var textLoc = new SKPoint(anchorX, anchorY + labelHeight);
                canvas.DrawText(_lastStatusText, textLoc, SKPaints.TextESPStatusText);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR Setting ESP Status Text: {ex}");
            }
        }

        /// <summary>
        /// Draw fireport aim in front of player.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawFireportAim(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (localPlayer.Firearm.FireportPosition is not Vector3 fireportPos)
                return;
            if (localPlayer.Firearm.FireportRotation is not Quaternion fireportRot)
                return;
            if (!CameraManagerBase.WorldToScreen(ref fireportPos, out var fireportPosScr))
                return;
            var forward = fireportRot.Down();
            var targetPos = fireportPos += forward * 1000f;
            if (!CameraManagerBase.WorldToScreen(ref targetPos, out var targetScr))
                return;

            canvas.DrawLine(fireportPosScr, targetScr, SKPaints.PaintFireportAimESP);
        }

        /// <summary>
        /// Draw player's Magazine/Ammo Count on ESP.
        /// </summary>
        private void DrawMagazine(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var mag = localPlayer.Firearm.Magazine;
            string counter = mag.IsValid ? $"{mag.Count} / {mag.MaxCount}" : "-- / --";
            var wepInfo = mag.WeaponInfo;

            string magazineText = counter;
            if (wepInfo is not null)
                magazineText = wepInfo + "\n" + counter;

            if (magazineText != _lastMagazineText)
            {
                InvalidateElementCache(UIElement.Magazine);
                _lastMagazineText = magazineText;
            }

            var counterWidth = SKPaints.TextMagazineESP.MeasureText(counter);
            var wepInfoWidth = wepInfo is not null ? SKPaints.TextMagazineInfoESP.MeasureText(wepInfo) : 0f;
            var maxWidth = Math.Max(counterWidth, wepInfoWidth);

            var textHeight = SKPaints.TextMagazineESP.FontSpacing + SKPaints.TextMagazineInfoESP.FontSpacing;
            var anchorX = CameraManagerBase.Viewport.Width - 15f * ESPConfig.FontScale + _magazineOffset.X;
            var anchorY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.10f - textHeight + 4f * ESPConfig.FontScale + _magazineOffset.Y;

            if (wepInfo is not null)
            {
                var wepInfoX = anchorX - wepInfoWidth / 2;
                canvas.DrawText(wepInfo, wepInfoX, anchorY, SKPaints.TextMagazineInfoESP);
            }

            var counterX = anchorX - counterWidth / 2;
            var counterY = anchorY + (SKPaints.TextMagazineESP.FontSpacing - SKPaints.TextMagazineInfoESP.FontSpacing + 6f * ESPConfig.FontScale);
            canvas.DrawText(counter, counterX, counterY, SKPaints.TextMagazineESP);
        }

        /// <summary>
        /// Draw 'ESP Hidden' notification.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(SKCanvas canvas)
        {
            var textPt = new SKPoint(CameraManagerBase.Viewport.Left + 4.5f * ESPConfig.FontScale,
                CameraManagerBase.Viewport.Top + 14f * ESPConfig.FontScale);
            canvas.DrawText("ESP Hidden", textPt, SKPaints.TextBasicESPLeftAligned);
        }

        /// <summary>
        /// Draw FPS Counter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawFPS(SKCanvas canvas)
        {
            var fpsText = $"{_fps}fps";

            if (fpsText != _lastFPSText)
            {
                InvalidateElementCache(UIElement.FPS);
                _lastFPSText = fpsText;
            }

            var textWidth = SKPaints.TextESPFPS.MeasureText(fpsText);
            var anchorX = CameraManagerBase.Viewport.Left + 25f * ESPConfig.FontScale + _fpsOffset.X;
            var anchorY = CameraManagerBase.Viewport.Top + 14f * ESPConfig.FontScale + _fpsOffset.Y;

            var textPt = new SKPoint(anchorX - textWidth / 2, anchorY);
            canvas.DrawText(fpsText, textPt, SKPaints.TextESPFPS);
        }

        /// <summary>
        /// Draw the Aim FOV Circle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawAimFOV(SKCanvas canvas) =>
            canvas.DrawCircle(CameraManagerBase.ViewportCenter, Aimbot.Config.FOV, SKPaints.PaintBasicESP);

        /// <summary>
        /// Draw all grenades within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawExplosives(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var grenades = Grenades;
            if (grenades is not null)
                foreach (var grenade in grenades)
                    grenade.DrawESP(canvas, localPlayer);
        }

        /// <summary>
        /// Draw the closest player information.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawClosestPlayer(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (localPlayer == null)
                return;

            var allPlayers = AllPlayers?.Where(x => x != localPlayer && x.IsHostileActive);
            var closestPlayer = allPlayers
                .OrderBy(p => Vector3.Distance(localPlayer.Position, p.Position))
                .FirstOrDefault();

            if (closestPlayer == null)
                return;

            var observedPlayer = closestPlayer as ArenaObservedPlayer;
            if (observedPlayer == null)
                return;

            var distance = Vector3.Distance(localPlayer.Position, observedPlayer.Position);
            var closestText = $"{observedPlayer.PlayerSide.GetDescription()[0]}:{observedPlayer.Name} ({distance:F0}m)";

            if (observedPlayer.Profile?.Level is int levelResult)
                closestText += $", L:{levelResult}";
            if (observedPlayer.Profile?.Overall_KD is float kdResult)
                closestText += $", KD:{kdResult.ToString("n2")}";
            if (observedPlayer.Profile?.RaidCount is int raidCountResult)
                closestText += $", R:{raidCountResult}";
            if (observedPlayer.Profile?.SurvivedRate is float survivedResult)
                closestText += $", SR:{survivedResult.ToString("n1")}";

            if (closestText != _lastClosestPlayerText)
            {
                InvalidateElementCache(UIElement.ClosestPlayer);
                _lastClosestPlayerText = closestText;
            }

            var textWidth = SKPaints.TextESPClosestPlayer.MeasureText(closestText);
            var anchorX = CameraManagerBase.ViewportCenter.X + _closestPlayerOffset.X;
            var anchorY = CameraManagerBase.ViewportCenter.Y + Aimbot.Config.FOV + 15f * ESPConfig.FontScale + _closestPlayerOffset.Y;

            var textPt = new SKPoint(anchorX - textWidth / 2, anchorY);
            canvas.DrawText(closestText, textPt, SKPaints.TextESPClosestPlayer);
        }

        /// <summary>
        /// Draw all grenades within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawRefillContainers(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var refillContainers = RefillContainers;
            if (refillContainers is not null)
                foreach (var refillContainer in refillContainers)
                    refillContainer.DrawESP(canvas, localPlayer);
        }

        /// <summary>
        /// Draw all filtered Loot Items within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLoot(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var loot = Loot;
            if (loot is not null)
            {
                foreach (var item in loot)
                {
                    item.DrawESP(canvas, localPlayer);
                }
            }
        }

        /// <summary>
        /// Draw all filtered Loot Items within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawContainers(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var containers = Containers;
            if (containers is not null)
            {
                foreach (var container in containers)
                {
                    container.DrawESP(canvas, localPlayer);
                }
            }
        }

        /// <summary>
        /// Draw Raid Stats in top right corner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawRaidStats(SKCanvas canvas, IReadOnlyCollection<Player> players)
        {
            var hostiles = players
                .Where(x => x.IsHostileActive)
                .ToArray();

            var pmcCount = hostiles.Count(x => x.IsPmc);
            var aiCount = hostiles.Count(x => x.Type is Player.PlayerType.AI);

            var statsData = new[]
            {
                new { Type = "PMC", Count = pmcCount },
                new { Type = "AI", Count = aiCount }
            };

            var typeColumnWidth = statsData.Max(x => SKPaints.TextESPRaidStats.MeasureText(x.Type));
            var countColumnWidth = statsData.Max(x => SKPaints.TextESPRaidStats.MeasureText(x.Count.ToString()));

            var columnPadding = 12f * ESPConfig.FontScale;
            var totalWidth = typeColumnWidth + countColumnWidth + columnPadding;

            var lineHeight = SKPaints.TextESPRaidStats.FontSpacing;
            var anchorX = CameraManagerBase.Viewport.Right - 3f * ESPConfig.FontScale + _raidStatsOffset.X;
            var anchorY = CameraManagerBase.Viewport.Top + SKPaints.TextESPRaidStats.TextSize +
                         CameraManagerBase.Viewport.Height * 0.0575f * ESPConfig.FontScale + _raidStatsOffset.Y;

            for (int i = 0; i < statsData.Length; i++)
            {
                var data = statsData[i];
                var rowY = anchorY + (i * lineHeight);

                var typeX = anchorX - totalWidth;
                canvas.DrawText(data.Type, typeX, rowY, SKPaints.TextESPRaidStats);

                var countX = anchorX - totalWidth + typeColumnWidth + columnPadding;
                canvas.DrawText(data.Count.ToString(), countX, rowY, SKPaints.TextESPRaidStats);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                SetFullscreen(!IsFullscreen);
                return true;
            }

            if (keyData == Keys.Escape && IsFullscreen)
            {
                SetFullscreen(false);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            if (WindowState is FormWindowState.Maximized)
                SetFullscreen(true);
            else
                base.OnSizeChanged(e);

            InvalidateBoundsCache();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                SaveUIPositions();
                CameraManagerBase.EspRunning = false;
                Window = null;
                _renderTimer.Dispose();

                // Clean up object pools
                foreach (var path in _pathPool)
                    path.Dispose();

                _pathPool.Clear();
            }
            finally
            {
                base.OnFormClosing(e);
            }
        }

        /// <summary>
        /// Zooms the bitmap 'in'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        public void ZoomIn(int amt)
        {
            var oldZoom = Config.ESP.RadarZoom;
            var newZoom = Config.ESP.RadarZoom - amt;

            if (newZoom >= 1)
                Config.ESP.RadarZoom = newZoom;
            else
                Config.ESP.RadarZoom = 1;
        }

        /// <summary>
        /// Zooms the bitmap 'out'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        public void ZoomOut(int amt)
        {
            var newZoom = Config.ESP.RadarZoom + amt;
            if (newZoom <= 70)
                Config.ESP.RadarZoom = newZoom;
            else
                Config.ESP.RadarZoom = 70;
        }

        #endregion

        #region Mini Radar

        private SKRect _radarRect = new SKRect(20, 20, 220, 220);
        private float _radarZoom;
        private bool _radarFreeMode = false;
        private Vector2 _radarPanPosition = SKPoint.Empty;
        private const float MinRadarSize = 100f;
        private const float MaxRadarSize = 400f;
        private const float HandleSize = 10f;

        private void ClampRadarRect()
        {
            var formBounds = new SKRect(0, 0, Math.Max(Width, 100), Math.Max(Height, 100));
            var clampedWidth = Math.Clamp(_radarRect.Width, MinRadarSize, Math.Min(MaxRadarSize, formBounds.Width));
            var clampedHeight = Math.Clamp(_radarRect.Height, MinRadarSize, Math.Min(MaxRadarSize, formBounds.Height));

            var clampedLeft = Math.Clamp(_radarRect.Left, formBounds.Left, formBounds.Right - clampedWidth);
            var clampedTop = Math.Clamp(_radarRect.Top, formBounds.Top, formBounds.Bottom - clampedHeight);

            _radarRect = new SKRect(clampedLeft, clampedTop, clampedLeft + clampedWidth, clampedTop + clampedHeight);
        }

        private void DrawRadar(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (localPlayer == null || LoneMapManager.Map == null)
                return;

            canvas.Save();
            canvas.ClipRect(_radarRect);
            _radarZoom = ESPConfig.RadarZoom;

            using (var bgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 180) })
            {
                canvas.DrawRect(_radarRect, bgPaint);
            }

            var map = LoneMapManager.Map;
            var playerPos = localPlayer.Position;
            var playerMapPos = playerPos.ToMapPos(map.Config);

            var radarSize = new SKSize(_radarRect.Width, _radarRect.Height);
            LoneMapParams mapParams;

            if (_radarFreeMode)
                mapParams = map.GetParametersE(radarSize, _radarZoom, ref _radarPanPosition);
            else
                mapParams = map.GetParametersE(radarSize, _radarZoom, ref playerMapPos);

            var radarBounds = new SKRect(
                _radarRect.Left,
                _radarRect.Top,
                _radarRect.Right,
                _radarRect.Bottom
            );

            map.Draw(canvas, playerPos.Y, mapParams.Bounds, radarBounds);

            if (ESPConfig.MiniRadar.ShowLoot)
                DrawRadarLoot(canvas, mapParams);

            DrawRadarPlayers(canvas, localPlayer, mapParams, radarBounds);
            DrawLocalPlayerIndicator(canvas, localPlayer);
            DrawRadarBorder(canvas);
            DrawRadarResizeHandle(canvas);
            DrawRadarInfo(canvas);

            canvas.Restore();
        }

        private void DrawRadarPlayers(SKCanvas canvas, LocalPlayer localPlayer, LoneMapParams mapParams, SKRect radarBounds)
        {
            var allPlayers = AllPlayers?.Where(x => x.IsHostileActive || x.IsFriendlyActive);
            if (allPlayers == null)
                return;

            foreach (var player in allPlayers)
            {
                if (player == localPlayer)
                    continue;

                var entityMapPos = player.Position.ToMapPos(LoneMapManager.Map.Config);
                if (!mapParams.Bounds.Contains(entityMapPos.X, entityMapPos.Y))
                    continue;

                var screenX = _radarRect.Left + _radarRect.Width * (entityMapPos.X - mapParams.Bounds.Left) / mapParams.Bounds.Width;
                var screenY = _radarRect.Top + _radarRect.Height * (entityMapPos.Y - mapParams.Bounds.Top) / mapParams.Bounds.Height;

                var paint = player.GetMiniRadarPaint();

                canvas.DrawCircle(screenX, screenY, (RADAR_PLAYER_SIZE * ESPConfig.MiniRadar.Scale), paint);

                if (player.MapRotation != 0)
                {
                    paint.StrokeWidth = (RADAR_AIMLINE_WIDTH * ESPConfig.MiniRadar.Scale);
                    var radians = player.MapRotation.ToRadians();
                    canvas.DrawLine(
                        screenX, screenY,
                        screenX + (RADAR_AIMLINE_LENGTH * ESPConfig.MiniRadar.Scale) * MathF.Cos(radians),
                        screenY + (RADAR_AIMLINE_LENGTH * ESPConfig.MiniRadar.Scale) * MathF.Sin(radians),
                        paint);
                }
            }
        }

        private void DrawLocalPlayerIndicator(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var paint = localPlayer.GetMiniRadarPaint();
            canvas.DrawCircle(_radarRect.MidX, _radarRect.MidY, (RADAR_PLAYER_SIZE * ESPConfig.MiniRadar.Scale), paint);

            if (localPlayer.MapRotation != 0)
            {
                paint.StrokeWidth = (RADAR_AIMLINE_WIDTH * ESPConfig.MiniRadar.Scale);
                var radians = localPlayer.MapRotation.ToRadians();
                canvas.DrawLine(
                    _radarRect.MidX, _radarRect.MidY,
                    _radarRect.MidX + (RADAR_AIMLINE_LENGTH * ESPConfig.MiniRadar.Scale) * MathF.Cos(radians),
                    _radarRect.MidY + (RADAR_AIMLINE_LENGTH * ESPConfig.MiniRadar.Scale) * MathF.Sin(radians),
                    paint);
            }
        }

        private void DrawRadarLoot(SKCanvas canvas, LoneMapParams mapParams)
        {
            if (Config.BattleMode)
                return;

            if (LootItem.LootSettings.Enabled)
            {
                var loot = Loot.Reverse();

                if (loot != null)
                {
                    foreach (var item in loot)
                    {
                        var entityMapPos = item.Position.ToMapPos(LoneMapManager.Map.Config);
                        var screenX = _radarRect.Left + _radarRect.Width * (entityMapPos.X - mapParams.Bounds.Left) / mapParams.Bounds.Width;
                        var screenY = _radarRect.Top + _radarRect.Height * (entityMapPos.Y - mapParams.Bounds.Top) / mapParams.Bounds.Height;

                        if (_radarRect.Contains(screenX, screenY))
                        {
                            var paint = item.GetMiniRadarPaint();
                            canvas.DrawRect(new SKRect(
                                screenX - (RADAR_LOOT_SIZE * ESPConfig.MiniRadar.Scale),
                                screenY - (RADAR_LOOT_SIZE * ESPConfig.MiniRadar.Scale),
                                screenX + (RADAR_LOOT_SIZE * ESPConfig.MiniRadar.Scale),
                                screenY + (RADAR_LOOT_SIZE * ESPConfig.MiniRadar.Scale)),
                                paint
                            );
                        }
                    }
                }
            }
        }

        private void DrawRadarBorder(SKCanvas canvas)
        {
            canvas.DrawRect(_radarRect, SKPaints.PaintMiniRadarOutlineESP);
        }

        private void DrawRadarResizeHandle(SKCanvas canvas)
        {
            var trianglePath = GetPath();

            trianglePath.MoveTo(_radarRect.Right, _radarRect.Bottom - HandleSize);
            trianglePath.LineTo(_radarRect.Right, _radarRect.Bottom);
            trianglePath.LineTo(_radarRect.Right - HandleSize, _radarRect.Bottom);
            trianglePath.Close();

            canvas.DrawPath(trianglePath, SKPaints.PaintMiniRadarResizeHandleESP);
            ReturnPath(trianglePath);
        }

        private void DrawRadarInfo(SKCanvas canvas)
        {
            using (var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 12,
                IsAntialias = true
            })
            {
                string mode = _radarFreeMode ? "FREE" : "LOCKED";
                canvas.DrawText($"RADAR [{mode}] Zoom: {_radarZoom:F1}x",
                    _radarRect.Left + 5, _radarRect.Top + 15, textPaint);
            }
        }

        #endregion

        #region Draggable UI

        private void InitializeUIElements()
        {
            _uiElements[UIElement.Magazine] = new UIElementInfo
            {
                Offset = _magazineOffset,
                GetCurrentText = GetCurrentMagazineText,
                CalculateBounds = CalculateMagazineBounds,
                CalculateBaseBounds = CalculateMagazineBaseBounds,
                SetOffset = offset => {
                    _magazineOffset = offset;
                    var info = _uiElements[UIElement.Magazine];
                    info.Offset = offset;
                    _uiElements[UIElement.Magazine] = info;
                }
            };

            _uiElements[UIElement.RaidStats] = new UIElementInfo
            {
                Offset = _raidStatsOffset,
                GetCurrentText = () => "RaidStats",
                CalculateBounds = CalculateRaidStatsBounds,
                CalculateBaseBounds = CalculateRaidStatsBaseBounds,
                SetOffset = offset => {
                    _raidStatsOffset = offset;
                    var info = _uiElements[UIElement.RaidStats];
                    info.Offset = offset;
                    _uiElements[UIElement.RaidStats] = info;
                }
            };

            _uiElements[UIElement.StatusText] = new UIElementInfo
            {
                Offset = _statusTextOffset,
                GetCurrentText = GetCurrentStatusText,
                CalculateBounds = CalculateStatusTextBounds,
                CalculateBaseBounds = CalculateStatusTextBaseBounds,
                SetOffset = offset => {
                    _statusTextOffset = offset;
                    var info = _uiElements[UIElement.StatusText];
                    info.Offset = offset;
                    _uiElements[UIElement.StatusText] = info;
                }
            };

            _uiElements[UIElement.FPS] = new UIElementInfo
            {
                Offset = _fpsOffset,
                GetCurrentText = GetCurrentFPSText,
                CalculateBounds = CalculateFPSBounds,
                CalculateBaseBounds = CalculateFPSBaseBounds,
                SetOffset = offset => {
                    _fpsOffset = offset;
                    var info = _uiElements[UIElement.FPS];
                    info.Offset = offset;
                    _uiElements[UIElement.FPS] = info;
                }
            };

            _uiElements[UIElement.ClosestPlayer] = new UIElementInfo
            {
                Offset = _closestPlayerOffset,
                GetCurrentText = GetCurrentClosestPlayerText,
                CalculateBounds = CalculateClosestPlayerBounds,
                CalculateBaseBounds = CalculateClosestPlayerBaseBounds,
                SetOffset = offset => {
                    _closestPlayerOffset = offset;
                    var info = _uiElements[UIElement.ClosestPlayer];
                    info.Offset = offset;
                    _uiElements[UIElement.ClosestPlayer] = info;
                }
            };
        }

        private void InvalidateBoundsCache()
        {
            _lastFrameBounds++;
            if (_lastFrameBounds == int.MaxValue)
            {
                _lastFrameBounds = 0;
                _boundsCache.Clear();
            }
        }

        private void InvalidateElementCache(UIElement element)
        {
            if (_boundsCache.ContainsKey(element))
                _boundsCache.Remove(element);
        }

        private bool IsNearElement(SKPoint point, UIElement element)
        {
            if (!IsElementVisible(element))
                return false;

            try
            {
                var bounds = GetElementBounds(element);
                if (bounds.IsEmpty)
                    return false;

                var scaledPadding = ScaledHitTestPadding;
                var inflatedBounds = bounds;
                inflatedBounds.Inflate(scaledPadding, scaledPadding);
                return inflatedBounds.Contains(point);
            }
            catch
            {
                return false;
            }
        }

        private bool IsNearCorner(SKPoint point, SKRect rect)
        {
            var dx = point.X - rect.Right;
            var dy = point.Y - rect.Bottom;

            return dx >= -HandleSize && dx <= 0 &&
                   dy >= -HandleSize && dy <= 0 &&
                   (dx + dy) >= -HandleSize;
        }

        private SKRect CalculateMagazineBounds()
        {
            var currentMagazineText = GetCurrentMagazineText();
            if (string.IsNullOrEmpty(currentMagazineText))
                return SKRect.Empty;

            var lines = currentMagazineText.Split('\n');
            var counter = lines.Last();
            var wepInfo = lines.Length > 1 ? lines.First() : null;

            var counterWidth = SKPaints.TextMagazineESP.MeasureText(counter);
            var wepInfoWidth = wepInfo is not null ? SKPaints.TextMagazineInfoESP.MeasureText(wepInfo) : 0f;
            var maxWidth = Math.Max(counterWidth, wepInfoWidth);

            var textHeight = SKPaints.TextMagazineESP.FontSpacing + SKPaints.TextMagazineInfoESP.FontSpacing;

            var anchorX = CameraManagerBase.Viewport.Width - 15f * ESPConfig.FontScale + _magazineOffset.X;
            var anchorY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.10f - textHeight + 4f * ESPConfig.FontScale + _magazineOffset.Y;

            if (wepInfo is not null)
            {
                var wepInfoY = anchorY;
                var counterSpacing = SKPaints.TextMagazineESP.FontSpacing - SKPaints.TextMagazineInfoESP.FontSpacing + 6f * ESPConfig.FontScale;
                var counterY = anchorY + counterSpacing;
                var topY = wepInfoY - SKPaints.TextMagazineInfoESP.TextSize;
                var bottomY = counterY;

                return new SKRect(anchorX - maxWidth / 2, topY, anchorX + maxWidth / 2, bottomY);
            }
            else
            {
                var counterSpacing = SKPaints.TextMagazineESP.FontSpacing - SKPaints.TextMagazineInfoESP.FontSpacing + 6f * ESPConfig.FontScale;
                var counterY = anchorY + counterSpacing;

                var topY = counterY - SKPaints.TextMagazineESP.TextSize;
                var bottomY = counterY;

                return new SKRect(anchorX - counterWidth / 2, topY, anchorX + counterWidth / 2, bottomY);
            }
        }

        private SKRect CalculateMagazineBaseBounds()
        {
            var sampleCounter = "30 / 30";
            var sampleWepInfo = "Single: M61";

            var counterWidth = SKPaints.TextMagazineESP.MeasureText(sampleCounter);
            var wepInfoWidth = SKPaints.TextMagazineInfoESP.MeasureText(sampleWepInfo);
            var maxWidth = Math.Max(counterWidth, wepInfoWidth);

            var textHeight = SKPaints.TextMagazineESP.FontSpacing + SKPaints.TextMagazineInfoESP.FontSpacing;
            var anchorX = CameraManagerBase.Viewport.Width - 15f * ESPConfig.FontScale;
            var anchorY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.10f - textHeight + 4f * ESPConfig.FontScale;
            var wepInfoY = anchorY;
            var counterSpacing = SKPaints.TextMagazineESP.FontSpacing - SKPaints.TextMagazineInfoESP.FontSpacing + 6f * ESPConfig.FontScale;
            var counterY = anchorY + counterSpacing;

            var topY = wepInfoY - SKPaints.TextMagazineInfoESP.TextSize;
            var bottomY = counterY;

            return new SKRect(anchorX - maxWidth / 2, topY, anchorX + maxWidth / 2, bottomY);
        }

        private string GetCurrentMagazineText()
        {
            var mag = LocalPlayer?.Firearm?.Magazine;
            if (mag == null)
                return null;

            string counter = mag.IsValid ? $"{mag.Count} / {mag.MaxCount}" : "-- / --";
            var wepInfo = mag.WeaponInfo;

            string magazineText = counter;
            if (wepInfo is not null)
                magazineText = wepInfo + "\n" + counter;

            return magazineText;
        }

        private SKRect CalculateRaidStatsBounds()
        {
            var sampleData = new[]
            {
                new { Type = "PMC", Count = 12 },
                new { Type = "AI", Count = 24 }
            };

            var typeColumnWidth = sampleData.Max(x => SKPaints.TextESPRaidStats.MeasureText(x.Type));
            var countColumnWidth = sampleData.Max(x => SKPaints.TextESPRaidStats.MeasureText(x.Count.ToString()));

            var columnPadding = 12f * ESPConfig.FontScale;
            var totalWidth = typeColumnWidth + countColumnWidth + columnPadding;

            var lineHeight = SKPaints.TextESPRaidStats.FontSpacing;
            var totalHeight = lineHeight * sampleData.Length;

            var scale = ESPConfig.FontScale;
            var anchorX = CameraManagerBase.Viewport.Right - 3f * scale + _raidStatsOffset.X;
            var startY = CameraManagerBase.Viewport.Top + SKPaints.TextESPRaidStats.TextSize +
                         CameraManagerBase.Viewport.Height * 0.0575f * scale + _raidStatsOffset.Y;

            return new SKRect(
                anchorX - totalWidth,
                startY - SKPaints.TextESPRaidStats.TextSize,
                anchorX,
                startY + totalHeight - SKPaints.TextESPRaidStats.TextSize
            );
        }

        private SKRect CalculateRaidStatsBaseBounds()
        {
            var sampleData = new[]
            {
                new { Type = "PMC", Count = 12 },
                new { Type = "AI", Count = 24 }
            };

            var typeColumnWidth = sampleData.Max(x => SKPaints.TextESPRaidStats.MeasureText(x.Type));
            var countColumnWidth = sampleData.Max(x => SKPaints.TextESPRaidStats.MeasureText(x.Count.ToString()));

            var columnPadding = 12f * ESPConfig.FontScale;
            var totalWidth = typeColumnWidth + countColumnWidth + columnPadding;

            var lineHeight = SKPaints.TextESPRaidStats.FontSpacing;
            var totalHeight = lineHeight * sampleData.Length;

            var scale = ESPConfig.FontScale;
            var anchorX = CameraManagerBase.Viewport.Right - 3f * scale;
            var startY = CameraManagerBase.Viewport.Top + SKPaints.TextESPRaidStats.TextSize +
                         CameraManagerBase.Viewport.Height * 0.0575f * scale;

            return new SKRect(
                anchorX - totalWidth,
                startY - SKPaints.TextESPRaidStats.TextSize,
                anchorX,
                startY + totalHeight - SKPaints.TextESPRaidStats.TextSize
            );
        }

        private SKRect CalculateFPSBounds()
        {
            var currentFPSText = GetCurrentFPSText();
            if (string.IsNullOrEmpty(currentFPSText))
                return SKRect.Empty;

            var textWidth = SKPaints.TextESPFPS.MeasureText(currentFPSText);
            var textHeight = SKPaints.TextESPFPS.TextSize;

            var anchorX = CameraManagerBase.Viewport.Left + 25f * ESPConfig.FontScale + _fpsOffset.X;
            var anchorY = CameraManagerBase.Viewport.Top + 14f * ESPConfig.FontScale + _fpsOffset.Y;

            var x = anchorX - textWidth / 2;

            return new SKRect(x, anchorY - textHeight, x + textWidth, anchorY);
        }

        private SKRect CalculateFPSBaseBounds()
        {
            var sampleFpsText = "9999fps";
            var textWidth = SKPaints.TextESPFPS.MeasureText(sampleFpsText);
            var textHeight = SKPaints.TextESPFPS.TextSize;

            var anchorX = CameraManagerBase.Viewport.Left + 25f * ESPConfig.FontScale;
            var anchorY = CameraManagerBase.Viewport.Top + 14f * ESPConfig.FontScale;

            var x = anchorX - textWidth / 2;

            return new SKRect(x, anchorY - textHeight, x + textWidth, anchorY);
        }

        private string GetCurrentFPSText()
        {
            return $"{_fps}fps";
        }

        private SKRect CalculateClosestPlayerBounds()
        {
            var currentClosestPlayerText = GetCurrentClosestPlayerText();
            if (string.IsNullOrEmpty(currentClosestPlayerText))
                return SKRect.Empty;

            var textWidth = SKPaints.TextESPClosestPlayer.MeasureText(currentClosestPlayerText);
            var textHeight = SKPaints.TextESPClosestPlayer.TextSize;

            var anchorX = CameraManagerBase.ViewportCenter.X + _closestPlayerOffset.X;
            var anchorY = CameraManagerBase.ViewportCenter.Y + Aimbot.Config.FOV + 15f * ESPConfig.FontScale + _closestPlayerOffset.Y;

            var x = anchorX - textWidth / 2;

            return new SKRect(x, anchorY - textHeight, x + textWidth, anchorY);
        }

        private SKRect CalculateClosestPlayerBaseBounds()
        {
            var currentClosestPlayerText = GetCurrentClosestPlayerText();
            if (string.IsNullOrEmpty(currentClosestPlayerText))
            {
                var sampleText = "B:VeryLongPlayerName123 (999m), L:99, KD:99.99, R:9999, SR:99.9";
                var sampleWidth = SKPaints.TextESPClosestPlayer.MeasureText(sampleText);
                var sampleHeight = SKPaints.TextESPClosestPlayer.TextSize;

                var sampleAnchorX = CameraManagerBase.ViewportCenter.X;
                var sampleAnchorY = CameraManagerBase.ViewportCenter.Y + Aimbot.Config.FOV + 15f * ESPConfig.FontScale;

                var sampleX = sampleAnchorX - sampleWidth / 2;

                return new SKRect(sampleX, sampleAnchorY - sampleHeight, sampleX + sampleWidth, sampleAnchorY);
            }

            var textWidth = SKPaints.TextESPClosestPlayer.MeasureText(currentClosestPlayerText);
            var textHeight = SKPaints.TextESPClosestPlayer.TextSize;

            var anchorX = CameraManagerBase.ViewportCenter.X;
            var anchorY = CameraManagerBase.ViewportCenter.Y + Aimbot.Config.FOV + 15f * ESPConfig.FontScale;

            var x = anchorX - textWidth / 2;

            return new SKRect(x, anchorY - textHeight, x + textWidth, anchorY);
        }

        private string GetCurrentClosestPlayerText()
        {
            var localPlayer = LocalPlayer;
            if (localPlayer == null)
                return null;

            var allPlayers = AllPlayers?.Where(x => x != localPlayer && x.IsHostileActive);

            var closestPlayer = allPlayers
                .OrderBy(p => Vector3.Distance(localPlayer.Position, p.Position))
                .FirstOrDefault();

            if (closestPlayer == null)
                return null;

            var observedPlayer = closestPlayer as ArenaObservedPlayer;
            if (observedPlayer == null)
                return null;

            var distance = Vector3.Distance(localPlayer.Position, observedPlayer.Position);
            var closestText = $"{observedPlayer.PlayerSide.GetDescription()[0]}:{observedPlayer.Name} ({distance:F0}m)";

            if (observedPlayer.Profile?.Level is int levelResult)
                closestText += $", L:{levelResult}";
            if (observedPlayer.Profile?.Overall_KD is float kdResult)
                closestText += $", KD:{kdResult.ToString("n2")}";
            if (observedPlayer.Profile?.RaidCount is int raidCountResult)
                closestText += $", R:{raidCountResult}";
            if (observedPlayer.Profile?.SurvivedRate is float survivedResult)
                closestText += $", SR:{survivedResult.ToString("n1")}";

            return closestText;
        }

        private SKRect CalculateStatusTextBounds()
        {
            var currentText = GetCurrentStatusText();
            if (string.IsNullOrEmpty(currentText))
                return SKRect.Empty;

            var labelWidth = SKPaints.TextESPStatusText.MeasureText(currentText);
            var spacing = 1f * ESPConfig.FontScale;
            var labelHeight = SKPaints.TextESPStatusText.FontSpacing;

            var clientArea = skglControl_ESP.ClientRectangle;
            var anchorX = clientArea.Width / 2 + _statusTextOffset.X;
            var anchorY = clientArea.Top + spacing + _statusTextOffset.Y;

            var bgRect = new SKRect(
                anchorX - labelWidth / 2,
                anchorY,
                anchorX + labelWidth / 2,
                anchorY + labelHeight + spacing);

            return bgRect;
        }

        private SKRect CalculateStatusTextBaseBounds()
        {
            var sampleText = "AIMBOT: HEAD (MOVE) (LTW)";
            var labelWidth = SKPaints.TextESPStatusText.MeasureText(sampleText);
            var spacing = 1f * ESPConfig.FontScale;
            var labelHeight = SKPaints.TextESPStatusText.FontSpacing;

            var clientArea = skglControl_ESP.ClientRectangle;
            var anchorX = clientArea.Width / 2;
            var anchorY = clientArea.Top + spacing;

            var bgRect = new SKRect(
                anchorX - labelWidth / 2,
                anchorY,
                anchorX + labelWidth / 2,
                anchorY + labelHeight + spacing);

            return bgRect;
        }

        private string GenerateCurrentStatusText()
        {
            var aimEnabled = MemWriteFeature<Aimbot>.Instance.Enabled;
            var rageMode = Config.MemWritesEnabled && Config.MemWrites.RageMode;
            var wideLeanEnabled = MemWrites.Enabled && MemWriteFeature<WideLean>.Instance.Enabled;
            var moveSpeedEnabled = MemWrites.Enabled && MemWriteFeature<MoveSpeed>.Instance.Enabled;

            string label = null;

            if (rageMode)
                label = aimEnabled ? $"{Aimbot.Config.TargetingMode.GetDescription()}: RAGE MODE" : "RAGE MODE";
            else if (aimEnabled)
            {
                var mode = Aimbot.Config.TargetingMode.GetDescription();
                if (Aimbot.Config.RandomBone.Enabled)
                    label = $"{mode}: Random Bone";
                else if (Aimbot.Config.SilentAim.AutoBone)
                    label = $"{mode}: Auto Bone";
                else
                    label = $"{mode}: {Aimbot.Config.Bone.GetDescription()}";
            }

            var secondaryFeatures = new List<string>();

            if (wideLeanEnabled)
                secondaryFeatures.Add("Lean");
            if (moveSpeedEnabled)
                secondaryFeatures.Add("MOVE");

            if (secondaryFeatures.Any())
            {
                var secondaryText = $"({string.Join(") (", secondaryFeatures)})";
                label = label is null ? secondaryText : $"{label} {secondaryText}";
            }

            return label ?? "";
        }

        private string GetCurrentStatusText()
        {
            return _lastStatusText ?? "";
        }

        private bool HasStatusText()
        {
            var currentText = GenerateCurrentStatusText();
            return !string.IsNullOrEmpty(currentText);
        }

        private void UpdateCursor(SKPoint point)
        {
            if (_dragState.IsActive)
                return;

            skglControl_ESP.Cursor = GetCursorForPoint(point);
        }

        public void OnRenderContextChanged()
        {
            InvalidateBoundsCache();
        }

        private void CheckRenderContextChanges()
        {
            var currentViewport = CameraManagerBase.Viewport;
            var currentControlSize = skglControl_ESP.ClientSize;

            if (_lastViewport != currentViewport || _lastControlSize != currentControlSize)
            {
                _lastViewport = currentViewport;
                _lastControlSize = currentControlSize;
                InvalidateBoundsCache();
            }
        }

        private bool IsElementVisible(UIElement element)
        {
            switch (element)
            {
                case UIElement.Magazine:
                    return ESPConfig.ShowMagazine;

                case UIElement.RaidStats:
                    return ESPConfig.ShowRaidStats;

                case UIElement.StatusText:
                    return ESPConfig.ShowStatusText && HasStatusText();

                case UIElement.FPS:
                    return ESPConfig.ShowFPS;

                case UIElement.ClosestPlayer:
                    return ESPConfig.ShowClosestPlayer;

                case UIElement.Radar:
                    return ESPConfig.MiniRadar.Enabled;

                default:
                    return false;
            }
        }

        private void ApplyDragMovement(SKPoint delta)
        {
            switch (_dragState.Target)
            {
                case DragTarget.RadarMove:
                    _radarRect = new SKRect(
                        _dragState.OriginalRect.Left + delta.X,
                        _dragState.OriginalRect.Top + delta.Y,
                        _dragState.OriginalRect.Right + delta.X,
                        _dragState.OriginalRect.Bottom + delta.Y);
                    ClampRadarRect();
                    break;

                case DragTarget.RadarResize:
                    var newWidth = Math.Max(_dragState.OriginalRect.Width + delta.X, MinRadarSize);
                    var newHeight = Math.Max(_dragState.OriginalRect.Height + delta.Y, MinRadarSize);
                    _radarRect = new SKRect(
                        _dragState.OriginalRect.Left,
                        _dragState.OriginalRect.Top,
                        _dragState.OriginalRect.Left + newWidth,
                        _dragState.OriginalRect.Top + newHeight);
                    ClampRadarRect();
                    break;

                default:
                    var element = (UIElement)_dragState.Target;
                    if (_uiElements.TryGetValue(element, out var elementInfo))
                    {
                        var newOffset = ClampUIElementPosition(element, _dragState.OriginalOffset + delta);

                        elementInfo.SetOffset(newOffset);

                        var updatedInfo = new UIElementInfo
                        {
                            Offset = newOffset,
                            GetCurrentText = elementInfo.GetCurrentText,
                            CalculateBounds = elementInfo.CalculateBounds,
                            CalculateBaseBounds = elementInfo.CalculateBaseBounds,
                            SetOffset = elementInfo.SetOffset
                        };
                        _uiElements[element] = updatedInfo;
                    }
                    break;
            }
        }

        private SKPoint ClampPositionToForm(SKPoint offset, SKRect elementBounds, SKRect baseBounds)
        {
            var finalBounds = new SKRect(
                baseBounds.Left + offset.X,
                baseBounds.Top + offset.Y,
                baseBounds.Right + offset.X,
                baseBounds.Bottom + offset.Y);

            var formSize = ClientSize;
            var formWidth = Math.Max(formSize.Width, 100);
            var formHeight = Math.Max(formSize.Height, 100);

            var elementWidth = Math.Max(elementBounds.Width, 1);
            var elementHeight = Math.Max(elementBounds.Height, 1);

            var clampedLeft = Math.Clamp(finalBounds.Left, 0, formWidth - elementWidth);
            var clampedTop = Math.Clamp(finalBounds.Top, 0, formHeight - elementHeight);

            return new SKPoint(clampedLeft - baseBounds.Left, clampedTop - baseBounds.Top);
        }

        private SKPoint ClampUIElementPosition(UIElement element, SKPoint offset)
        {
            if (!_uiElements.TryGetValue(element, out var elementInfo))
                return offset;

            try
            {
                var bounds = GetElementBounds(element);
                if (bounds.IsEmpty)
                    return offset;

                var baseBounds = _boundsCache[element].BaseBounds;
                var clampedOffset = ClampPositionToForm(offset, bounds, baseBounds);

                if (clampedOffset != elementInfo.Offset)
                    InvalidateElementCache(element);

                return clampedOffset;
            }
            catch
            {
                return new SKPoint(0, 0);
            }
        }

        private SKRect GetElementBounds(UIElement element, bool forceRecalculate = false)
        {
            CheckRenderContextChanges();

            if (!forceRecalculate && _boundsCache.TryGetValue(element, out var cached) && cached.IsValid(_lastFrameBounds))
                return cached.Bounds;

            SKRect bounds, baseBounds;

            if (element == UIElement.Radar)
            {
                bounds = baseBounds = _radarRect;
            }
            else if (_uiElements.TryGetValue(element, out var elementInfo))
            {
                bounds = elementInfo.CalculateBounds();
                baseBounds = elementInfo.CalculateBaseBounds();
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(element));
            }

            _boundsCache[element] = new CachedBounds
            {
                Bounds = bounds,
                BaseBounds = baseBounds,
                FrameCalculated = _lastFrameBounds
            };

            return bounds;
        }

        private Cursor GetCursorForPoint(SKPoint point)
        {
            if (IsElementVisible(UIElement.Radar))
            {
                if (IsNearCorner(point, _radarRect))
                    return Cursors.SizeNWSE;

                if (_radarRect.Contains(point))
                    return Cursors.SizeAll;
            }

            foreach (var element in _uiElements.Keys)
            {
                if (IsElementVisible(element) && IsNearElement(point, element))
                    return Cursors.SizeAll;
            }

            return Cursors.Default;
        }

        private class DragState
        {
            public DragTarget Target { get; set; } = DragTarget.None;
            public SKPoint StartPoint { get; set; }
            public SKPoint OriginalOffset { get; set; }
            public SKRect OriginalRect { get; set; }
            public bool IsActive => Target != DragTarget.None;

            public void Reset()
            {
                Target = DragTarget.None;
                StartPoint = SKPoint.Empty;
                OriginalOffset = SKPoint.Empty;
                OriginalRect = SKRect.Empty;
            }
        }

        private struct CachedBounds
        {
            public SKRect Bounds { get; set; }
            public SKRect BaseBounds { get; set; }
            public int FrameCalculated { get; set; }
            public bool IsValid(int currentFrame) => FrameCalculated == currentFrame;
        }

        private struct UIElementInfo
        {
            public SKPoint Offset;
            public Func<string> GetCurrentText;
            public Func<SKRect> CalculateBounds;
            public Func<SKRect> CalculateBaseBounds;
            public Action<SKPoint> SetOffset;
        }

        private enum UIElement
        {
            Magazine = 0,
            RaidStats = 1,
            StatusText = 2,
            FPS = 3,
            ClosestPlayer = 4,
            Radar = 5
        }

        private enum DragTarget
        {
            None = -1,
            Magazine = 0,
            RaidStats = 1,
            StatusText = 2,
            FPS = 3,
            ClosestPlayer = 4,
            RadarMove = 100,
            RadarResize = 101
        }

        #endregion
    }
}