using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.SKWidgetControl;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity;
using RectFSer = eft_dma_radar.UI.Misc.RectFSer;
using Switch = eft_dma_radar.Tarkov.GameWorld.Exits.Switch;

namespace eft_dma_radar.UI.ESP
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
        private string _lastTopLootText = "";
        private string _lastFPSText = "";

        private SKPoint _fpsOffset = SKPoint.Empty;
        private SKPoint _statusTextOffset = SKPoint.Empty;
        private SKPoint _statusBarOffset = SKPoint.Empty;
        private SKPoint _magazineOffset = SKPoint.Empty;
        private SKPoint _raidStatsOffset = SKPoint.Empty;
        private SKPoint _closestPlayerOffset = SKPoint.Empty;
        private SKPoint _topLootOffset = SKPoint.Empty;

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

        private ESPQuestInfoWidget _espQuestInfo;
        public ESPQuestInfoWidget ESPQuestInfo { get => _espQuestInfo; private set => _espQuestInfo = value; }

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
        public bool IsFullscreen => FormBorderStyle is FormBorderStyle.None;

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
        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;

        /// <summary>
        /// Contains all 'Exfils' in Local Game World, and their status/position(s).
        /// </summary>
        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        /// <summary>
        /// Contains all filtered loot in Local Game World.
        /// </summary>
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// Contains all static containers in Local Game World.
        /// </summary>
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        private static EntityTypeSettingsESP MineSettings = ESP.Config.EntityTypeESPSettings.GetSettings("Mine");

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
            SetupESPWidgets();
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
            _statusBarOffset = new SKPoint(ESPConfig.StatusBarOffset.X, ESPConfig.StatusBarOffset.Y);
            _fpsOffset = new SKPoint(ESPConfig.FPSOffset.X, ESPConfig.FPSOffset.Y);
            _closestPlayerOffset = new SKPoint(ESPConfig.ClosestPlayerOffset.X, ESPConfig.ClosestPlayerOffset.Y);
            _topLootOffset = new SKPoint(ESPConfig.TopLootOffset.X, ESPConfig.TopLootOffset.Y);
        }

        private void SaveUIPositions()
        {
            ESPConfig.RadarRect = new RectFSer(_radarRect.Left, _radarRect.Top, _radarRect.Right, _radarRect.Bottom);

            ESPConfig.MagazineOffset = new PointFSer(_uiElements[UIElement.Magazine].Offset.X, _uiElements[UIElement.Magazine].Offset.Y);
            ESPConfig.StatusTextOffset = new PointFSer(_uiElements[UIElement.StatusText].Offset.X, _uiElements[UIElement.StatusText].Offset.Y);
            ESPConfig.RaidStatsOffset = new PointFSer(_uiElements[UIElement.RaidStats].Offset.X, _uiElements[UIElement.RaidStats].Offset.Y);
            ESPConfig.StatusBarOffset = new PointFSer(_uiElements[UIElement.StatusBars].Offset.X, _uiElements[UIElement.StatusBars].Offset.Y);
            ESPConfig.FPSOffset = new PointFSer(_uiElements[UIElement.FPS].Offset.X, _uiElements[UIElement.FPS].Offset.Y);
            ESPConfig.ClosestPlayerOffset = new PointFSer(_uiElements[UIElement.ClosestPlayer].Offset.X, _uiElements[UIElement.ClosestPlayer].Offset.Y);
            ESPConfig.TopLootOffset = new PointFSer(_uiElements[UIElement.TopLoot].Offset.X, _uiElements[UIElement.TopLoot].Offset.Y);

            Config.SaveAsync();
        }

        /// <summary>
        /// Setup Widgets after SKElement is fully loaded and window sized properly.
        /// </summary>
        private void SetupESPWidgets()
        {
            var left = 2;
            var top = 0;
            var right = (float)skglControl_ESP.Width;
            var bottom = (float)skglControl_ESP.Height;

            if (Config.ESPWidgets.QuestInfoLocation == default)
                Config.ESPWidgets.QuestInfoLocation = new SKRect(left + 50, top + 50, left + 450, top + 400);

            _espQuestInfo = new ESPQuestInfoWidget(skglControl_ESP, new SKPoint(Config.ESPWidgets.QuestInfoLocation.Left, Config.Widgets.QuestInfoLocation.Top), Config.ESPWidgets.QuestInfoMinimized, Config.ESP.FontScale);
            _espQuestInfo.Size = new SKSize(Config.ESPWidgets.QuestInfoLocation.Width, Config.ESPWidgets.QuestInfoLocation.Height);
        }

        private void SaveWidgetPositions()
        {
            if (_espQuestInfo != null)
            {
                Config.ESPWidgets.QuestInfoLocation = _espQuestInfo.ClientRect;
                Config.ESPWidgets.QuestInfoMinimized = _espQuestInfo.Minimized;
            }

            _espQuestInfo?.Dispose();
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

                        if (!battleMode && (Config.ProcessLoot &&
                            (LootItem.CorpseESPSettings.Enabled ||
                            LootItem.LootESPSettings.Enabled ||
                            LootItem.ImportantLootESPSettings.Enabled ||
                            LootItem.QuestItemESPSettings.Enabled)))
                            DrawLoot(canvas, localPlayer);
                        if (!battleMode && Config.Containers.Show && StaticLootContainer.ESPSettings.Enabled)
                            DrawContainers(canvas, localPlayer);
                        if (!battleMode && (Config.QuestHelper.Enabled &&
                            (QuestManager.ESPSettings.Enabled) ||
                            LootItem.QuestItemESPSettings.Enabled))
                            DrawQuests(canvas, localPlayer);
                        if (MineSettings.Enabled && GameData.Mines.TryGetValue(MapID, out var mines))
                            DrawMines(canvas, localPlayer, mines);
                        if (!battleMode && (Exfil.ESPSettings.Enabled ||
                            TransitPoint.ESPSettings.Enabled))
                            DrawExfils(canvas, localPlayer);
                        if (!battleMode && Switch.ESPSettings.Enabled)
                            DrawSwitches(canvas, localPlayer);
                        if (!battleMode && Door.ESPSettings.Enabled)
                            DrawDoors(canvas, localPlayer);
                        if (Grenade.ESPSettings.Enabled ||
                            Tripwire.ESPSettings.Enabled ||
                            MortarProjectile.ESPSettings.Enabled)
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
                        if (ESPConfig.EnergyHydrationBar)
                            DrawEnergyHydration(canvas, localPlayer);
                        if (ESPConfig.ShowClosestPlayer)
                            DrawClosestPlayer(canvas, localPlayer);
                        if (ESPConfig.ShowTopLoot)
                            DrawTopLoot(canvas, localPlayer);
                        if (ESPConfig.ShowQuestInfoWidget)
                            _espQuestInfo?.Draw(canvas);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        /// Draw Mines/Claymores on ESP.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawMines(SKCanvas canvas, LocalPlayer localPlayer, Memory<Vector3> mines)
        {
            foreach (ref var mine in mines.Span)
            {
                var dist = Vector3.Distance(localPlayer.Position, mine);
                if (dist > MineSettings.RenderDistance)
                    continue;

                if (!CameraManagerBase.WorldToScreen(ref mine, out var scrPos))
                    continue;

                var scale = ESP.Config.FontScale;

                switch (MineSettings.RenderMode)
                {
                    case EntityRenderMode.None:
                        break;

                    case EntityRenderMode.Dot:
                        var dotSize = 3f * scale;
                        canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintExplosiveESP);
                        break;

                    case EntityRenderMode.Cross:
                        var crossSize = 5f * scale;
                        using (var thickPaint = new SKPaint
                        {
                            Color = SKPaints.PaintExplosiveESP.Color,
                            StrokeWidth = 1.5f * scale,
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke
                        })
                        {
                            canvas.DrawLine(
                                scrPos.X - crossSize, scrPos.Y - crossSize,
                                scrPos.X + crossSize, scrPos.Y + crossSize,
                                thickPaint);
                            canvas.DrawLine(
                                scrPos.X - crossSize, scrPos.Y + crossSize,
                                scrPos.X + crossSize, scrPos.Y - crossSize,
                                thickPaint);
                        }
                        break;

                    case EntityRenderMode.Plus:
                        var plusSize = 5f * scale;
                        using (var thickPaint = new SKPaint
                        {
                            Color = SKPaints.PaintExplosiveESP.Color,
                            StrokeWidth = 1.5f * scale,
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke
                        })
                        {
                            canvas.DrawLine(
                                scrPos.X, scrPos.Y - plusSize,
                                scrPos.X, scrPos.Y + plusSize,
                                thickPaint);
                            canvas.DrawLine(
                                scrPos.X - plusSize, scrPos.Y,
                                scrPos.X + plusSize, scrPos.Y,
                                thickPaint);
                        }
                        break;

                    case EntityRenderMode.Square:
                        var boxHalf = 3f * scale;
                        var boxPt = new SKRect(
                            scrPos.X - boxHalf, scrPos.Y - boxHalf,
                            scrPos.X + boxHalf, scrPos.Y + boxHalf);
                        canvas.DrawRect(boxPt, SKPaints.PaintExplosiveESP);
                        break;

                    case EntityRenderMode.Diamond:
                    default:
                        var diamondSize = 3.5f * scale;
                        using (var diamondPath = new SKPath())
                        {
                            diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                            diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                            diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                            diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                            diamondPath.Close();
                            canvas.DrawPath(diamondPath, SKPaints.PaintExplosiveESP);
                        }
                        break;
                }

                if (MineSettings.ShowName || MineSettings.ShowDistance)
                {
                    var textY = scrPos.Y + 16f * scale;
                    var textPt = new SKPoint(scrPos.X, textY);
                    var label = MineSettings.ShowName ? "*DANGER* Mine" : null;

                    textPt.DrawESPText(
                        canvas,
                        null,
                        localPlayer,
                        MineSettings.ShowDistance,
                        SKPaints.TextExplosiveESP,
                        label,
                        dist
                    );
                }
            }
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
            canvas.DrawCircle(CameraManagerBase.ViewportCenter, Aimbot.Config.FOV, SKPaints.PaintAimbotFOVESP);

        /// <summary>
        /// Draw all filtered Loot Items within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLoot(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var loot = Loot?.Where(x => x is not QuestItem);
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
                    if (LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL"))
                    {
                        if (Config.Containers.HideSearched && container.Searched)
                            continue;

                        container.DrawESP(canvas, localPlayer);
                    }
                }
            }
        }

        /// <summary>
        /// Draw all Open/Pending exfils.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawExfils(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var exits = Exits;
            if (exits is not null)
                foreach (var exit in exits)
                    exit.DrawESP(canvas, localPlayer);
        }

        /// <summary>
        /// Draw switches for the map.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawSwitches(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (GameData.Switches.TryGetValue(MapID, out var switches))
            {
                foreach (var switchEntry in switches)
                {
                    var switchObj = new Tarkov.GameWorld.Exits.Switch(switchEntry.Value, switchEntry.Key);
                    switchObj.DrawESP(canvas, localPlayer);
                }
            }
        }

        /// <summary>
        /// Draw all grenades within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawExplosives(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var explosives = Explosives;
            if (explosives is not null)
                foreach (var explosive in explosives)
                    explosive.DrawESP(canvas, localPlayer);
        }

        /// <summary>
        /// Draw all grenades within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDoors(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var doors = Memory.Game?.Interactables._Doors;
            if (doors is null || doors.Count == 0)
                return;

            foreach (var door in doors)
            {
                door.DrawESP(canvas, localPlayer);
            }
        }

        /// <summary>
        /// Draw all quest locations within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawQuests(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (LootItem.QuestItemESPSettings.Enabled && !localPlayer.IsScav)
            {
                var questItems = Loot?.Where(x => x is QuestItem);
                if (questItems is not null)
                    foreach (var item in questItems)
                        item.DrawESP(canvas, localPlayer);
            }

            if (QuestManager.ESPSettings.Enabled && !localPlayer.IsScav)
            {
                var questLocations = Memory.QuestManager?.LocationConditions;
                if (questLocations is not null)
                    foreach (var loc in questLocations)
                        loc.DrawESP(canvas, localPlayer);
            }
        }

        /// <summary>
        /// Draw the closest player information.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawClosestPlayer(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (localPlayer == null)
                return;

            var allPlayers = AllPlayers?.Where(x => x != localPlayer && x.IsHumanHostileActive);
            if (allPlayers == null || !allPlayers.Any())
            {
                allPlayers = AllPlayers?.Where(x => x != localPlayer && x.IsAIActive);
                if (allPlayers == null || !allPlayers.Any())
                {
                    if (!string.IsNullOrEmpty(_lastClosestPlayerText))
                    {
                        _lastClosestPlayerText = "";
                        InvalidateElementCache(UIElement.ClosestPlayer);
                    }
                    return;
                }
            }

            var closestPlayer = allPlayers
                .OrderBy(p => Vector3.Distance(localPlayer.Position, p.Position))
                .FirstOrDefault();

            if (closestPlayer == null)
                return;

            var observedPlayer = closestPlayer as ObservedPlayer;
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
            if (observedPlayer.Profile?.Hours is int hoursResult)
                closestText += $", HR:{hoursResult}";

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
        /// Draw the top loot items
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawTopLoot(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (localPlayer == null)
                return;

            var loot = Loot?.Where(x => !(x is QuestItem));
            if (loot == null || !loot.Any())
            {
                if (!string.IsNullOrEmpty(_lastTopLootText))
                {
                    _lastTopLootText = "";
                    InvalidateElementCache(UIElement.TopLoot);
                }
                return;
            }

            var groupedLoot = loot
                .GroupBy(item => item.Name)
                .Select(group => new
                {
                    Name = group.Key,
                    Count = group.Count(),
                    ClosestDistance = group.Min(item => Vector3.Distance(localPlayer.Position, item.Position)),
                    Value = group.Max(item => item.Price)
                })
                .OrderByDescending(item => item.Value)
                .Take(5)
                .ToList();

            if (!groupedLoot.Any())
            {
                if (!string.IsNullOrEmpty(_lastTopLootText))
                {
                    _lastTopLootText = "";
                    InvalidateElementCache(UIElement.TopLoot);
                }
                return;
            }

            var lootData = groupedLoot.Select(item => new
            {
                NameText = item.Count > 1 ? $"{item.Name} (x{item.Count})" : item.Name,
                ValueText = TarkovMarketItem.FormatPrice(item.Value),
                DistanceText = $"{item.ClosestDistance:F0}m",
                Item = item
            }).ToList();

            var nameColumnWidth = lootData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.NameText));
            var valueColumnWidth = lootData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.ValueText));
            var distanceColumnWidth = lootData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.DistanceText));

            var columnPadding = 12f * ESPConfig.FontScale;
            var totalWidth = nameColumnWidth + valueColumnWidth + distanceColumnWidth + (columnPadding * 2);

            var lineHeight = SKPaints.TextESPTopLoot.FontSpacing;
            var totalHeight = lineHeight * lootData.Count;

            var anchorX = CameraManagerBase.Viewport.Right - totalWidth - 3f * ESPConfig.FontScale + _topLootOffset.X;
            var anchorY = CameraManagerBase.Viewport.Top + SKPaints.TextESPRaidStats.TextSize +
                          CameraManagerBase.Viewport.Height * 0.0575f * ESPConfig.FontScale +
                          (SKPaints.TextESPRaidStats.FontSpacing * 4) +
                          10f * ESPConfig.FontScale +
                          _topLootOffset.Y;

            var topLootText = string.Join("\n", lootData.Select(x => $"{x.NameText} | {x.ValueText} | {x.DistanceText}"));

            if (topLootText != _lastTopLootText)
            {
                InvalidateElementCache(UIElement.TopLoot);
                _lastTopLootText = topLootText;
            }

            for (int i = 0; i < lootData.Count; i++)
            {
                var data = lootData[i];
                var rowY = anchorY + (i * lineHeight);

                var nameX = anchorX;
                canvas.DrawText(data.NameText, nameX, rowY, SKPaints.TextESPTopLoot);

                var valueX = anchorX + nameColumnWidth + columnPadding;
                canvas.DrawText(data.ValueText, valueX, rowY, SKPaints.TextESPTopLoot);

                var distanceX = anchorX + nameColumnWidth + valueColumnWidth + (columnPadding * 2);
                canvas.DrawText(data.DistanceText, distanceX, rowY, SKPaints.TextESPTopLoot);
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
            var pscavCount = hostiles.Count(x => x.Type is Player.PlayerType.PScav);
            var aiCount = hostiles.Count(x => x.IsAI);
            var bossCount = hostiles.Count(x => x.Type is Player.PlayerType.AIBoss);

            var statsData = new[]
            {
                new { Type = "PMC", Count = pmcCount },
                new { Type = "PScav", Count = pscavCount },
                new { Type = "AI", Count = aiCount },
                new { Type = "Boss", Count = bossCount }
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

        /// <summary>
        /// Draw player's Energy/Hydration bars on ESP (bottom left).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawEnergyHydration(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var energy = localPlayer.GetEnergy();
            var hydration = localPlayer.GetHydration();
            var scale = ESPConfig.FontScale;
            var barHeight = 12f * scale;
            var spacing = 6f * scale;
            var margin = 15f * scale;
            var barWidth = 150f * scale;

            var anchorX = CameraManagerBase.Viewport.Width - margin + _statusBarOffset.X;
            var baseY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.20f - (barHeight * 2 + spacing);
            var energyY = baseY;
            var hydrationY = energyY + barHeight + spacing;

            energyY += _statusBarOffset.Y;
            hydrationY += _statusBarOffset.Y;

            var barX = anchorX - barWidth / 2;

            DrawStatusBar(canvas, barX, energyY, barWidth, barHeight, energy, 100f, true);
            DrawStatusBar(canvas, barX, hydrationY, barWidth, barHeight, hydration, 100f, false);

            DrawCenteredBarText(canvas, barX, energyY, barWidth, barHeight, energy.ToString("F1"), scale);
            DrawCenteredBarText(canvas, barX, hydrationY, barWidth, barHeight, hydration.ToString("F1"), scale);
        }

        /// <summary>
        /// Helper method to draw a status bar with background, fill, and optional label.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawStatusBar(SKCanvas canvas, float x, float y, float width, float height, float current, float max, bool isEnergy)
        {
            var scale = ESPConfig.FontScale;

            var bgRect = new SKRect(x, y, x + width, y + height);
            var bgPaint = SKPaints.PaintEnergyHydrationBackgroundESP;
            canvas.DrawRect(bgRect, bgPaint);

            var percentage = Math.Max(0f, Math.Min(1f, current / max));
            var fillWidth = width * percentage;
            var fillRect = new SKRect(x, y, x + fillWidth, y + height);

            var fillPaint = isEnergy ? SKPaints.PaintEnergyFillESP : SKPaints.PaintHydrationFillESP;
            canvas.DrawRect(fillRect, fillPaint);

            using var borderPaint = new SKPaint
            {
                Color = fillPaint.Color.WithAlpha(200),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f * scale,
                IsAntialias = true
            };
            canvas.DrawRect(bgRect, borderPaint);
        }

        /// <summary>
        /// Helper method to draw centered text inside a bar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawCenteredBarText(SKCanvas canvas, float barX, float barY, float barWidth, float barHeight, string text, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var textPaint = SKPaints.TextEnergyHydrationBarESP;
            var outlineTextPaint = SKPaints.TextEnergyHydrationBarOutlineESP;

            var textWidth = textPaint.MeasureText(text);
            var centerX = barX + (barWidth / 2f) - (textWidth / 2f);
            var centerY = barY + (barHeight / 2f) + (textPaint.TextSize / 3f);

            canvas.DrawText(text, centerX, centerY, outlineTextPaint);
            canvas.DrawText(text, centerX, centerY, textPaint);
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
                SaveWidgetPositions();
                CameraManagerBase.EspRunning = false;
                Window = null;
                _renderTimer.Dispose();

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
            if (Config.BattleMode || !Config.ProcessLoot)
                return;

            if (LootItem.CorpseSettings.Enabled || LootItem.LootSettings.Enabled || LootItem.ImportantLootSettings.Enabled)
            {
                var loot = Loot?.Where(x =>
                    !(x is QuestItem) &&
                    (LootItem.CorpseSettings.Enabled || !(x is LootCorpse))
                ).Reverse();

                if (loot != null)
                {
                    foreach (var item in loot)
                    {
                        if (!LootItem.CorpseSettings.Enabled && item is LootCorpse)
                            continue;

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

            if (LootItem.QuestItemSettings.Enabled)
            {
                var questItems = Loot?.Where(x => x is QuestItem);
                if (questItems != null)
                {
                    foreach (var item in questItems)
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

            _uiElements[UIElement.StatusBars] = new UIElementInfo
            {
                Offset = _statusBarOffset,
                GetCurrentText = () => "StatusBars",
                CalculateBounds = CalculateStatusBarsBounds,
                CalculateBaseBounds = CalculateStatusBarsBaseBounds,
                SetOffset = offset => {
                    _statusBarOffset = offset;
                    var info = _uiElements[UIElement.StatusBars];
                    info.Offset = offset;
                    _uiElements[UIElement.StatusBars] = info;
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

            _uiElements[UIElement.TopLoot] = new UIElementInfo
            {
                Offset = _topLootOffset,
                GetCurrentText = GetCurrentTopLootText,
                CalculateBounds = CalculateTopLootBounds,
                CalculateBaseBounds = CalculateTopLootBaseBounds,
                SetOffset = offset => {
                    _topLootOffset = offset;
                    var info = _uiElements[UIElement.TopLoot];
                    info.Offset = offset;
                    _uiElements[UIElement.TopLoot] = info;
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

        private SKRect CalculateStatusBarsBounds()
        {
            var scale = ESPConfig.FontScale;
            var barHeight = 12f * scale;
            var spacing = 6f * scale;
            var barWidth = 150f * scale;
            var margin = 15f * scale;

            var anchorX = CameraManagerBase.Viewport.Width - margin + _statusBarOffset.X;
            var baseY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.20f - (barHeight * 2 + spacing) + _statusBarOffset.Y;
            var totalHeight = barHeight * 2 + spacing;

            var x = anchorX - barWidth / 2;

            return new SKRect(x, baseY, x + barWidth, baseY + totalHeight);
        }

        private SKRect CalculateStatusBarsBaseBounds()
        {
            var scale = ESPConfig.FontScale;
            var barHeight = 12f * scale;
            var spacing = 6f * scale;
            var barWidth = 150f * scale;
            var margin = 15f * scale;

            var anchorX = CameraManagerBase.Viewport.Width - margin;
            var baseY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.20f - (barHeight * 2 + spacing);
            var totalHeight = barHeight * 2 + spacing;

            var x = anchorX - barWidth / 2;

            return new SKRect(x, baseY, x + barWidth, baseY + totalHeight);
        }

        private SKRect CalculateRaidStatsBounds()
        {
            var sampleData = new[]
            {
                new { Type = "PMC", Count = 12 },
                new { Type = "PScav", Count = 14 },
                new { Type = "AI", Count = 24 },
                new { Type = "Boss", Count = 4 }
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
                new { Type = "PScav", Count = 14 },
                new { Type = "AI", Count = 24 },
                new { Type = "Boss", Count = 4 }
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
                var sampleText = "B:VeryLongPlayerName123 (999m), L:99, KD:99.99, R:9999, SR:99.9, HR: 9999";
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

            var allPlayers = AllPlayers?.Where(x => x != localPlayer && x.IsHumanHostileActive);
            if (allPlayers == null || !allPlayers.Any())
            {
                allPlayers = AllPlayers?.Where(x => x != localPlayer && x.IsAIActive);
                if (allPlayers == null || !allPlayers.Any())
                    return null;
            }

            var closestPlayer = allPlayers
                .OrderBy(p => Vector3.Distance(localPlayer.Position, p.Position))
                .FirstOrDefault();

            if (closestPlayer == null)
                return null;

            var observedPlayer = closestPlayer as ObservedPlayer;
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
            if (observedPlayer.Profile?.Hours is int hoursResult)
                closestText += $", HR:{hoursResult}";

            return closestText;
        }

        private SKRect CalculateTopLootBounds()
        {
            var currentTopLootText = GetCurrentTopLootText();
            if (string.IsNullOrEmpty(currentTopLootText))
                return SKRect.Empty;

            var loot = Loot?.Where(x => !(x is QuestItem));
            if (loot == null || !loot.Any())
                return SKRect.Empty;

            var groupedLoot = loot
                .GroupBy(item => item.Name)
                .Select(group => new
                {
                    Name = group.Key,
                    Count = group.Count(),
                    ClosestDistance = group.Min(item => Vector3.Distance(LocalPlayer.Position, item.Position)),
                    Value = group.Max(item => item.Price)
                })
                .OrderByDescending(item => item.Value)
                .Take(5)
                .ToList();

            if (!groupedLoot.Any())
                return SKRect.Empty;

            var lootData = groupedLoot.Select(item => new
            {
                NameText = item.Count > 1 ? $"{item.Name} (x{item.Count})" : item.Name,
                ValueText = TarkovMarketItem.FormatPrice(item.Value),
                DistanceText = $"{item.ClosestDistance:F0}m"
            }).ToList();

            var nameColumnWidth = lootData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.NameText));
            var valueColumnWidth = lootData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.ValueText));
            var distanceColumnWidth = lootData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.DistanceText));

            var columnPadding = 12f * ESPConfig.FontScale;
            var totalWidth = nameColumnWidth + valueColumnWidth + distanceColumnWidth + (columnPadding * 2);

            var lineHeight = SKPaints.TextESPTopLoot.FontSpacing;
            var totalHeight = lineHeight * lootData.Count;

            var anchorX = CameraManagerBase.Viewport.Right - totalWidth - 3f * ESPConfig.FontScale + _topLootOffset.X;
            var anchorY = CameraManagerBase.Viewport.Top + SKPaints.TextESPRaidStats.TextSize +
                          CameraManagerBase.Viewport.Height * 0.0575f * ESPConfig.FontScale +
                          (SKPaints.TextESPRaidStats.FontSpacing * 4) +
                          10f * ESPConfig.FontScale +
                          _topLootOffset.Y;

            return new SKRect(
                anchorX,
                anchorY - SKPaints.TextESPTopLoot.TextSize,
                anchorX + totalWidth,
                anchorY + totalHeight - SKPaints.TextESPTopLoot.TextSize
            );
        }

        private SKRect CalculateTopLootBaseBounds()
        {
            var sampleData = new[]
            {
                new { NameText = "Very Long Item Name (x99)", ValueText = "999M", DistanceText = "999m" },
                new { NameText = "Another Long Item Name (x88)", ValueText = "888K", DistanceText = "888m" },
                new { NameText = "Third Long Item Name (x77)", ValueText = "777K", DistanceText = "777m" },
                new { NameText = "Fourth Long Item Name (x66)", ValueText = "666K", DistanceText = "666m" },
                new { NameText = "Fifth Long Item Name (x55)", ValueText = "555K", DistanceText = "555m" }
            };

            var nameColumnWidth = sampleData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.NameText));
            var valueColumnWidth = sampleData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.ValueText));
            var distanceColumnWidth = sampleData.Max(x => SKPaints.TextESPTopLoot.MeasureText(x.DistanceText));

            var columnPadding = 12f * ESPConfig.FontScale;
            var totalWidth = nameColumnWidth + valueColumnWidth + distanceColumnWidth + (columnPadding * 2);

            var lineHeight = SKPaints.TextESPTopLoot.FontSpacing;
            var totalHeight = lineHeight * sampleData.Length;

            var anchorX = CameraManagerBase.Viewport.Right - totalWidth - 3f * ESPConfig.FontScale;
            var anchorY = CameraManagerBase.Viewport.Top + SKPaints.TextESPRaidStats.TextSize +
                          CameraManagerBase.Viewport.Height * 0.0575f * ESPConfig.FontScale +
                          (SKPaints.TextESPRaidStats.FontSpacing * 4) +
                          10f * ESPConfig.FontScale;

            return new SKRect(
                anchorX,
                anchorY - SKPaints.TextESPTopLoot.TextSize,
                anchorX + totalWidth,
                anchorY + totalHeight - SKPaints.TextESPTopLoot.TextSize
            );
        }

        private string GetCurrentTopLootText()
        {
            var localPlayer = LocalPlayer;
            if (localPlayer == null)
                return null;

            var loot = Loot?.Where(x => !(x is QuestItem));
            if (loot == null || !loot.Any())
                return null;

            var groupedLoot = loot
                .GroupBy(item => item.Name)
                .Select(group => new
                {
                    Name = group.Key,
                    Count = group.Count(),
                    ClosestDistance = group.Min(item => Vector3.Distance(localPlayer.Position, item.Position)),
                    Value = group.Max(item => item.Price)
                })
                .OrderByDescending(item => item.Value)
                .Take(5)
                .ToList();

            var topLootLines = groupedLoot.Select(item =>
            {
                var countText = item.Count > 1 ? $" (x{item.Count})" : "";
                return $"{item.Name}{countText} ({item.ClosestDistance:F0}m)";
            }).ToList();

            return string.Join("\n", topLootLines);
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

        private string GetCurrentStatusText()
        {
            return _lastStatusText ?? "";
        }

        private bool HasStatusText()
        {
            var currentText = GenerateCurrentStatusText();
            return !string.IsNullOrEmpty(currentText);
        }

        private string GenerateCurrentStatusText()
        {
            var aimEnabled = MemWriteFeature<Aimbot>.Instance.Enabled;
            var rageMode = Config.MemWritesEnabled && Config.MemWrites.RageMode;
            var wideLeanEnabled = MemWrites.Enabled && MemWriteFeature<WideLean>.Instance.Enabled;
            var lootThroughWallsZoomed = MemWrites.Enabled && MemWriteFeature<LootThroughWalls>.Instance.Enabled && LootThroughWalls.ZoomEngaged;
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
            if (lootThroughWallsZoomed)
                secondaryFeatures.Add("LTW");
            else if (moveSpeedEnabled)
                secondaryFeatures.Add("MOVE");

            if (secondaryFeatures.Any())
            {
                var secondaryText = $"({string.Join(") (", secondaryFeatures)})";
                label = label is null ? secondaryText : $"{label} {secondaryText}";
            }

            return label ?? "";
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

                case UIElement.StatusBars:
                    return ESPConfig.EnergyHydrationBar;

                case UIElement.RaidStats:
                    return ESPConfig.ShowRaidStats;

                case UIElement.StatusText:
                    return ESPConfig.ShowStatusText && HasStatusText();

                case UIElement.FPS:
                    return ESPConfig.ShowFPS;

                case UIElement.ClosestPlayer:
                    return ESPConfig.ShowClosestPlayer;

                case UIElement.TopLoot:
                    return ESPConfig.ShowTopLoot;

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
            StatusBars = 1,
            RaidStats = 2,
            StatusText = 3,
            FPS = 4,
            ClosestPlayer = 5,
            TopLoot = 6,
            Radar = 7
        }

        private enum DragTarget
        {
            None = -1,
            Magazine = 0,
            StatusBars = 1,
            RaidStats = 2,
            StatusText = 3,
            FPS = 4,
            ClosestPlayer = 5,
            TopLoot = 6,
            RadarMove = 100,
            RadarResize = 101
        }

        #endregion
    }
}