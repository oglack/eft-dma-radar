using arena_dma_radar.Arena;
using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.Arena.ArenaPlayer.Plugins;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.Features.MemoryWrites;
using arena_dma_radar.Arena.Features.MemoryWrites.Patches;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.Arena.GameWorld.Interactive;
using arena_dma_radar.Arena.Loot;
using arena_dma_radar.UI;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.Radar;
using arena_dma_radar.UI.SKWidgetControl;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using HandyControl.Controls;
using HandyControl.Themes;
using HandyControl.Tools;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Linq;
using System.Security.Authentication.ExtendedProtection;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Timer = System.Timers.Timer;
using UserControl = System.Windows.Controls.UserControl;

namespace arena_dma_radar
{
    public partial class MainWindow
    {
        #region Fields / Properties
        private DispatcherTimer _sizeChangeTimer;
        private readonly Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;

        private IMouseoverEntity _mouseOverItem;
        private bool _mouseDown;
        private Point _lastMousePosition;
        private Vector2 _mapPanPosition;

        private Dictionary<string, PanelInfo> _panels;

        private int _fps;
        private int _zoom = 100;
        public int _rotationDegrees = 0;
        private bool _freeMode = false;
        private bool _isDraggingToolbar = false;
        private Point _toolbarDragStartPoint;

        private float _targetZoom = 100f;
        private float _currentZoom = 100f;
        private Vector2 _targetPanPosition = Vector2.Zero;
        private Vector2 _currentPanPosition = Vector2.Zero;
        private DateTime _lastFrameTime = DateTime.UtcNow;

        private const float ZOOM_PAN_STRENGTH = 4f;
        private const float ZOOM_SMOOTH_FACTOR = 8.0f;
        private const float PAN_SMOOTH_FACTOR = 12.0f;
        private const float MIN_ZOOM_DELTA = 0.1f;
        private const float MIN_PAN_DELTA = 0.5f;

        private const int MIN_WATCHLIST_PANEL_WIDTH = 200;
        private const int MIN_WATCHLIST_PANEL_HEIGHT = 200;
        private const int MIN_PLAYERHISTORY_PANEL_WIDTH = 350;
        private const int MIN_PLAYERHISTORY_PANEL_HEIGHT = 130;
        private const int MIN_ESP_PANEL_WIDTH = 200;
        private const int MIN_ESP_PANEL_HEIGHT = 200;
        private const int MIN_MEMORY_WRITING_PANEL_WIDTH = 200;
        private const int MIN_MEMORY_WRITING_PANEL_HEIGHT = 200;
        private const int MIN_SETTINGS_PANEL_WIDTH = 200;
        private const int MIN_SETTINGS_PANEL_HEIGHT = 200;

        private readonly object _renderLock = new object();
        private volatile bool _isRendering = false;
        private volatile bool _uiInteractionActive = false;
        private DispatcherTimer _uiActivityTimer;

        private readonly Stopwatch _statusSw = Stopwatch.StartNew();
        private int _statusOrder = 1;

        private AimviewWidget _aimview;
        public AimviewWidget AimView { get => _aimview; private set => _aimview = value; }

        private PlayerInfoWidget _playerInfo;
        public PlayerInfoWidget PlayerInfo { get => _playerInfo; private set => _playerInfo = value; }

        private DebugInfoWidget _debugInfo;
        public DebugInfoWidget DebugInfo { get => _debugInfo; private set => _debugInfo = value; }

        /// <summary>
        /// Determines if MainWindow is ready or not
        /// </summary>
        public static bool Initialized = false;

        /// <summary>
        /// Main UI/Application Config.
        /// </summary>
        public static Config Config => Program.Config;

        /// <summary>
        /// Singleton Instance of MainWindow.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal static MainWindow Window { get; private set; }

        /// <summary>
        /// Current UI Scale Value for Primary Application Window.
        /// </summary>
        public static float UIScale => Config.UIScale;

        /// <summary>
        /// Currently 'Moused Over' Group.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static int? MouseoverGroup { get; private set; }

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory?.MapID ?? "null";
                return id;
            }
        }

        /// <summary>
        /// Game has started and Radar is starting up...
        /// </summary>
        private static bool Starting => Memory?.Starting ?? false;

        /// <summary>
        /// Radar has found Escape From Tarkov process and is ready.
        /// </summary>
        private static bool Ready => Memory?.Ready ?? false;

        /// <summary>
        /// Radar has found Local Game World, and a Raid Instance is active.
        /// </summary>
        private static bool InRaid => Memory?.InRaid ?? false;

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// Returns the player the Current Window belongs to.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory?.LocalPlayer ?? null;

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
        /// All Filtered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// All Unfiltered Loot on the map.
        /// </summary>
        private static IEnumerable<LootItem> UnfilteredLoot => Memory.Loot?.UnfilteredLoot;

        /// <summary>
        /// All Static Containers on the map.
        /// </summary>
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        /// <summary>
        /// Contains all 'mouse-overable' items.
        /// </summary>
        private IEnumerable<IMouseoverEntity> MouseOverItems
        {
            get
            {
                var loot = Loot ?? Enumerable.Empty<IMouseoverEntity>();
                var containers = Containers ?? Enumerable.Empty<IMouseoverEntity>();
                var refillContainers = RefillContainers ?? Enumerable.Empty<IMouseoverEntity>();

                var players = AllPlayers?
                  .Where(x => x is not Arena.ArenaPlayer.LocalPlayer)
                  ?? Enumerable.Empty<Player>();

                var result = loot.Concat(containers).Concat(refillContainers).Concat(players);

                return result.Any() ? result : null;
            }
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            Window = this;

            this.SizeChanged += MainWindow_SizeChanged;

            if (Config.WindowMaximized)
                this.WindowState = WindowState.Maximized;

            if (Config.WindowSize.Width > 0 && Config.WindowSize.Height > 0)
            {
                this.Width = Config.WindowSize.Width;
                this.Height = Config.WindowSize.Height;
            }

            EspColorOptions.LoadColors(Config);
            CameraManagerBase.UpdateViewportRes();

            var interval = TimeSpan.FromMilliseconds(1000d / Config.RadarTargetFPS);
            _renderTimer = new(interval);

            this.MouseDoubleClick += MainWindow_MouseDoubleClick;
            this.Closing += MainWindow_Closing;
            this.Loaded += (s, e) =>
            {
                Growl.Register("MainGrowl", GrowlPanel);

                RadarColorOptions.LoadColors(Config);
                EspColorOptions.LoadColors(Config);
                InterfaceColorOptions.LoadColors(Config);

                InitializeCanvas();

                _currentZoom = _zoom;
                _targetZoom = _zoom;
                _lastFrameTime = DateTime.UtcNow;

                _currentPanPosition = _mapPanPosition;
                _targetPanPosition = _mapPanPosition;
            };

            Initialized = true;
            InitializePanels();
            InitializeUIActivityMonitoring();
        }

        private void btnDebug_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // debug code
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #region Rendering
        /// <summary>
        /// Main Render Event.
        /// </summary>
        private void SkCanvas_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            var isStarting = Starting;
            var isReady = Ready; // cache bool
            var inRaid = InRaid; // cache bool
            var localPlayer = LocalPlayer; // cache ref to current player
            var canvas = e.Surface.Canvas; // get Canvas reference to draw on

            try
            {
                SkiaResourceTracker.TrackMainWindowFrame();
                UpdateSmoothValues();

                SetFPS(inRaid, canvas);
                // Check for map switch
                var mapID = MapID;
                //LoneLogging.WriteLine($"[DEBUG] MapID = {mapID}");

                if (string.IsNullOrWhiteSpace(mapID))
                    return;

                if (!mapID.Equals(LoneMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                    LoneMapManager.LoadMap(mapID);

                canvas.Clear(InterfaceColorOptions.RadarBackgroundColor); // Clear canvas

                if (inRaid && localPlayer is not null) // LocalPlayer is in a raid -> Begin Drawing...
                {
                    //LoneLogging.WriteLine($"[DEBUG] InRaid = {inRaid}, LocalPlayer = {(localPlayer != null)}");
                    var map = LoneMapManager.Map; // Cache ref
                    ArgumentNullException.ThrowIfNull(map, nameof(map));
                    var closestToMouse = _mouseOverItem; // cache ref
                    var mouseOverGrp = MouseoverGroup; // cache value for entire render
                                                       // Get LocalPlayer location
                    var localPlayerPos = localPlayer.Position;
                    var localPlayerMapPos = localPlayerPos.ToMapPos(map.Config);

                    // Prepare to draw Game Map - use smooth zoom/pan values
                    LoneMapParams mapParams; // Drawing Source
                    if (_freeMode)
                        mapParams = map.GetParameters(skCanvas, (int)_currentZoom, ref _currentPanPosition);
                    else
                        mapParams = map.GetParameters(skCanvas, (int)_currentZoom, ref localPlayerMapPos);

                    if (GeneralSettingsControl.chkMapSetup.IsChecked == true)
                        MapSetupControl.UpdatePlayerPosition(localPlayer);

                    var mapCanvasBounds = new SKRect() // Drawing Destination
                    {
                        Left = 0,
                        Right = (float)skCanvas.ActualWidth,
                        Top = 0,
                        Bottom = (float)skCanvas.ActualHeight
                    };

                    // Get the center of the canvas
                    var centerX = (mapCanvasBounds.Left + mapCanvasBounds.Right) / 2;
                    var centerY = (mapCanvasBounds.Top + mapCanvasBounds.Bottom) / 2;

                    // Apply a rotation transformation to the canvas
                    canvas.RotateDegrees(_rotationDegrees, centerX, centerY);

                    // Draw Map
                    map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);

                    // Draw LocalPlayer
                    localPlayer.Draw(canvas, mapParams, localPlayer);

                    // Draw other players
                    var allPlayers = AllPlayers?
                        .Where(x => !x.HasExfild);

                    var battleMode = Config.BattleMode;

                    if (!battleMode && StaticLootContainer.Settings.Enabled)
                    {
                        var containers = Containers;
                        if (containers is not null)
                        {
                            foreach (var container in containers)
                            {
                                container.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (!battleMode && LootItem.LootSettings.Enabled)
                    {
                        var loot = Loot?.Reverse();
                        if (loot is not null)
                        {
                            foreach (var item in loot)
                            {
                                item.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (!battleMode && ArenaPresetRefillContainer.Settings.Enabled)
                    {
                        var refillContainers = RefillContainers;
                        if (refillContainers is not null)
                        {
                            foreach (var refillContainer in refillContainers)
                            {
                                refillContainer.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (Grenade.Settings.Enabled)
                    {
                        var grenades = Grenades;
                        if (grenades is not null)
                        {
                            foreach (var grenade in grenades)
                            {
                                grenade.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    if (allPlayers is not null)
                        foreach (var player in allPlayers)
                        {
                            if (player == localPlayer)
                                continue;

                            player.Draw(canvas, mapParams, localPlayer);
                        }

                    closestToMouse?.DrawMouseover(canvas, mapParams, localPlayer); // draw tooltip for object the mouse is closest to

                    if (allPlayers is not null && Config.ShowInfoTab) // Players Overlay
                        _playerInfo?.Draw(canvas, localPlayer, allPlayers);

                    if (Config.AimviewWidgetEnabled)
                        _aimview?.Draw(canvas);

                    if (Config.ShowDebugWidget)
                        _debugInfo?.Draw(canvas);
                }
                else // LocalPlayer is *not* in a Raid -> Display Reason
                {
                    if (!isStarting)
                        GameNotRunningStatus(canvas);
                    else if (isStarting && !isReady)
                        StartingUpStatus(canvas);
                    else if (!inRaid)
                        WaitingForRaidStatus(canvas);
                }

                SetStatusText(canvas);
                canvas.Flush(); // commit frame to GPU
            }
            catch (Exception ex) // Log rendering errors
            {
                LoneLogging.WriteLine($"CRITICAL RENDER ERROR: {ex}");
            }
        }

        private void SkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            NotifyUIActivity();

            if (!InRaid)
                return;

            _mouseDown = true;
            _lastMousePosition = e.GetPosition(skCanvas);

            var shouldCheckMouseover = e.RightButton != MouseButtonState.Pressed;

            if (shouldCheckMouseover)
                CheckMouseoverItems(e.GetPosition(skCanvas));

            if (e.RightButton == MouseButtonState.Pressed &&
                _mouseOverItem is Player player &&
                player.IsHostileActive)
            {
                player.ToggleFocus();
            }
        }

        private void SkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                NotifyUIActivity();

            var currentPos = e.GetPosition(skCanvas);

            if (_mouseDown && _freeMode && e.LeftButton == MouseButtonState.Pressed)
            {
                var deltaX = (float)(currentPos.X - _lastMousePosition.X);
                var deltaY = (float)(currentPos.Y - _lastMousePosition.Y);

                var zoomSensitivity = _currentZoom / 100f;
                var minSensitivity = 0.1f;
                zoomSensitivity = Math.Max(zoomSensitivity, minSensitivity);

                var adjustedDeltaX = deltaX * zoomSensitivity;
                var adjustedDeltaY = deltaY * zoomSensitivity;

                _targetPanPosition.X -= adjustedDeltaX;
                _targetPanPosition.Y -= adjustedDeltaY;
                _lastMousePosition = currentPos;

                skCanvas.InvalidateVisual();
                return;
            }

            if (!InRaid)
            {
                ClearRefs();
                return;
            }

            var items = MouseOverItems;
            if (items?.Any() != true)
            {
                ClearRefs();
                return;
            }

            var mouse = new Vector2((float)currentPos.X, (float)currentPos.Y);
            var closest = items.Aggregate(
                (x1, x2) => Vector2.Distance(x1.MouseoverPosition, mouse)
                            < Vector2.Distance(x2.MouseoverPosition, mouse)
                        ? x1
                        : x2); // Get object 'closest' to mouse position

            if (Vector2.Distance(closest.MouseoverPosition, mouse) >= 12)
            {
                ClearRefs();
                return;
            }

            switch (closest)
            {
                case Player player:
                    _mouseOverItem = player;
                    if (player.IsHumanHostile
                        && player.TeamID != -1)
                        MouseoverGroup = player.TeamID; // Set Team ID for closest player(s)
                    else
                        MouseoverGroup = null; // Clear Group ID
                    break;
                case LootContainer ctr:
                    _mouseOverItem = ctr;
                    break;
                case LootItem ctr:
                    _mouseOverItem = ctr;
                    break;
                case ArenaPresetRefillContainer ctr:
                    _mouseOverItem = ctr;
                    break;
                default:
                    ClearRefs();
                    break;
            }
        }

        private void SkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = false;

            if (_freeMode)
                skCanvas.InvalidateVisual();
        }

        private void SkCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var mousePosition = e.GetPosition(skCanvas);

            if (e.Delta > 0)
            {
                int amt = e.Delta / 120 * 5;
                ZoomIn(amt, mousePosition);
            }
            else if (e.Delta < 0)
            {
                int amt = -e.Delta / 120 * 5;
                ZoomOut(amt);
            }

            skCanvas.InvalidateVisual();
        }

        private void ClearRefs()
        {
            _mouseOverItem = null;
            MouseoverGroup = null;
        }

        private void CheckMouseoverItems(Point mousePosition)
        {
            var mousePos = new Vector2((float)mousePosition.X, (float)mousePosition.Y);
            IMouseoverEntity closest = null;
            var closestDist = float.MaxValue;
            int? mouseoverGroup = null;

            var items = MouseOverItems;
            if (items != null)
            {
                foreach (var item in items)
                {
                    float dist = Vector2.Distance(mousePos, item.MouseoverPosition);
                    if (dist < closestDist && dist < 10f * UIScale)
                    {
                        closestDist = dist;
                        closest = item;

                        if (item is Player player)
                            mouseoverGroup = player.TeamID;
                    }
                }
            }

            _mouseOverItem = closest;
            MouseoverGroup = mouseoverGroup;
            skCanvas.InvalidateVisual();
        }

        private void IncrementStatus()
        {
            if (_statusSw.Elapsed.TotalSeconds >= 1d)
            {
                if (_statusOrder == 3)
                    _statusOrder = 1;
                else
                    _statusOrder++;
                _statusSw.Restart();
            }
        }

        private void GameNotRunningStatus(SKCanvas canvas)
        {
            const string notRunning = "Game Process Not Running!";
            float textWidth = SKPaints.TextRadarStatus.MeasureText(notRunning);
            canvas.DrawText(notRunning, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void StartingUpStatus(SKCanvas canvas)
        {
            const string startingUp1 = "Starting Up.";
            const string startingUp2 = "Starting Up..";
            const string startingUp3 = "Starting Up...";
            string status = _statusOrder == 1 ?
                startingUp1 : _statusOrder == 2 ?
                startingUp2 : startingUp3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(startingUp1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void WaitingForRaidStatus(SKCanvas canvas)
        {
            const string waitingFor1 = "Waiting for Raid Start.";
            const string waitingFor2 = "Waiting for Raid Start..";
            const string waitingFor3 = "Waiting for Raid Start...";
            string status = _statusOrder == 1 ?
                waitingFor1 : _statusOrder == 2 ?
                waitingFor2 : waitingFor3;
            float textWidth = SKPaints.TextRadarStatus.MeasureText(waitingFor1);
            canvas.DrawText(status, ((float)skCanvas.ActualWidth / 2) - textWidth / 2f, (float)skCanvas.ActualHeight / 2,
                SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void SetFPS(bool inRaid, SKCanvas canvas)
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                if (Config.ShowDebugWidget)
                    _debugInfo?.UpdateFps(_fps);

                var fps = Interlocked.Exchange(ref _fps, 0); // Get FPS -> Reset FPS counter
                _fpsSw.Restart();
            }
            else
            {
                _fps++; // Increment FPS counter
            }
        }

        /// <summary>
        /// Set the status text in the top middle of the radar window.
        /// </summary>
        /// <param name="canvas"></param>
        private void SetStatusText(SKCanvas canvas)
        {
            try
            {
                var memWritesEnabled = MemWrites.Enabled;
                var aimEnabled = Aimbot.Config.Enabled;
                var mode = Aimbot.Config.TargetingMode;
                string label = null;

                if (memWritesEnabled && Config.MemWrites.RageMode)
                    label = MemWriteFeature<Aimbot>.Instance.Enabled ? $"{mode.GetDescription()}: RAGE MODE" : "RAGE MODE";

                else if (memWritesEnabled && aimEnabled)
                {
                    if (Aimbot.Config.RandomBone.Enabled)
                        label = $"{mode.GetDescription()}: Random Bone";
                    else if (Aimbot.Config.SilentAim.AutoBone)
                        label = $"{mode.GetDescription()}: Auto Bone";
                    else
                    {
                        var defaultBone = MemoryWritingControl.cboTargetBone.Text;
                        label = $"{mode.GetDescription()}: {defaultBone}";
                    }
                }

                if (memWritesEnabled)
                {
                    if (MemWriteFeature<WideLean>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "Lean";
                        else
                            label += " (Lean)";
                    }
                    
                    if (MemWriteFeature<MoveSpeed>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "MOVE";
                        else
                            label += " (MOVE)";
                    }
                }

                if (label is null)
                    return;

                var width = (float)skCanvas.CanvasSize.Width;
                var height = (float)skCanvas.CanvasSize.Height;
                var labelWidth = SKPaints.TextStatusSmall.MeasureText(label);
                var spacing = 1f * UIScale;
                var top = spacing; // Start from top of the canvas
                var labelHeight = SKPaints.TextStatusSmall.FontSpacing;
                var bgRect = new SKRect(
                    width / 2 - labelWidth / 2,
                    top,
                    width / 2 + labelWidth / 2,
                    top + labelHeight + spacing);
                canvas.DrawRect(bgRect, SKPaints.PaintTransparentBacker);
                var textLoc = new SKPoint(width / 2, top + labelHeight);
                canvas.DrawText(label, textLoc, SKPaints.TextStatusSmall);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR Setting Aim UI Text: {ex}");
            }
        }

        public void PurgeSKResources()
        {
            Dispatcher.Invoke(() =>
            {
                skCanvas?.GRContext?.PurgeResources();
            });
        }

        private void RenderTimer_Elapsed(object sender, EventArgs e)
        {
            if (_isRendering) return;

            try
            {
                var priority = _uiInteractionActive ?
                    DispatcherPriority.Background :
                    DispatcherPriority.Render;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_renderLock)
                    {
                        if (_isRendering) return;
                        _isRendering = true;
                    }

                    try
                    {
                        skCanvas.InvalidateVisual();
                    }
                    finally
                    {
                        _isRendering = false;
                    }
                }), priority);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Render timer error: {ex.Message}");
                _isRendering = false;
            }
        }

        private async void InitializeCanvas()
        {
            _renderTimer.Start();
            _fpsSw.Start();

            while (skCanvas.GRContext is null)
                await Task.Delay(25);

            skCanvas.GRContext.SetResourceCacheLimit(536870912); // 512 MB

            SetupWidgets();

            // Setup the canvas and event handlers
            skCanvas.PaintSurface += SkCanvas_PaintSurface;
            skCanvas.MouseDown += SkCanvas_MouseDown;
            skCanvas.MouseMove += SkCanvas_MouseMove;
            skCanvas.MouseUp += SkCanvas_MouseUp;
            skCanvas.MouseWheel += SkCanvas_MouseWheel;

            _renderTimer.Elapsed += RenderTimer_Elapsed;
        }

        /// <summary>
        /// Setup Widgets after SKElement is fully loaded and window sized properly.
        /// </summary>
        private void SetupWidgets()
        {
            var left = 2;
            var top = 0;
            var right = (float)skCanvas.ActualWidth;
            var bottom = (float)skCanvas.ActualHeight;

            if (Config.Widgets.AimviewLocation == default)
            {
                Config.Widgets.AimviewLocation = new SKRect(left, bottom - 200, left + 200, bottom);
            }

            if (Config.Widgets.PlayerInfoLocation == default)
            {
                Config.Widgets.PlayerInfoLocation = new SKRect(right - 1, top + 45, right, top + 1);
            }

            if (Config.Widgets.DebugInfoLocation == default)
            {
                Config.Widgets.DebugInfoLocation = new SKRect(left, top, left, top);
            }

            _aimview = new AimviewWidget(skCanvas, Config.Widgets.AimviewLocation, Config.Widgets.AimviewMinimized, UIScale);
            _playerInfo = new PlayerInfoWidget(skCanvas, Config.Widgets.PlayerInfoLocation, Config.Widgets.PlayerInfoMinimized, UIScale);
            _debugInfo = new DebugInfoWidget(skCanvas, Config.Widgets.DebugInfoLocation, Config.Widgets.DebugInfoMinimized, UIScale);
        }

        public void UpdateRenderTimerInterval(int targetFPS)
        {
            var interval = TimeSpan.FromMilliseconds(1000d / targetFPS);
            _renderTimer.Interval = interval;
        }
        #endregion

        #region Panel Events
        #region General Settings
        /// <summary>
        /// Handles opening general settings panel
        /// </summary>
        private void btnGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("GeneralSettings");
        }

        /// <summary>
        /// Handle close request from settings panel
        /// </summary>
        private void GeneralSettingsControl_CloseRequested(object sender, EventArgs e)
        {
            GeneralSettingsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from settings panel
        /// </summary>
        private void GeneralSettingsControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(GeneralSettingsPanel) + e.OffsetX;
            var top = Canvas.GetTop(GeneralSettingsPanel) + e.OffsetY;

            Canvas.SetLeft(GeneralSettingsPanel, left);
            Canvas.SetTop(GeneralSettingsPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from settings panel
        /// </summary>
        private void GeneralSettingsControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = GeneralSettingsPanel.Width + e.DeltaWidth;
            var height = GeneralSettingsPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_SETTINGS_PANEL_WIDTH);
            height = Math.Max(height, MIN_SETTINGS_PANEL_HEIGHT);

            GeneralSettingsPanel.Width = width;
            GeneralSettingsPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Memory Writing Settings
        /// <summary>
        /// Handles setting memory writing panel visibility
        /// </summary>
        private void btnMemoryWritingSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MemoryWriting");
        }

        /// <summary>
        /// Handle close request from memory writing control
        /// </summary>
        private void MemoryWritingControl_CloseRequested(object sender, EventArgs e)
        {
            MemoryWritingPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from memory writing control
        /// </summary>
        private void MemoryWritingControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(MemoryWritingPanel) + e.OffsetX;
            var top = Canvas.GetTop(MemoryWritingPanel) + e.OffsetY;

            Canvas.SetLeft(MemoryWritingPanel, left);
            Canvas.SetTop(MemoryWritingPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from memory writing control
        /// </summary>
        private void MemoryWritingControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = MemoryWritingPanel.Width + e.DeltaWidth;
            var height = MemoryWritingPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_MEMORY_WRITING_PANEL_WIDTH);
            height = Math.Max(height, MIN_MEMORY_WRITING_PANEL_HEIGHT);

            MemoryWritingPanel.Width = width;
            MemoryWritingPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region ESP Settings
        /// <summary>
        /// Handles setting ESP panel visibility
        /// </summary>
        private void btnESPSettings_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("ESP");
        }

        /// <summary>
        /// Handle close request from ESP settings control
        /// </summary>
        private void ESPControl_CloseRequested(object sender, EventArgs e)
        {
            ESPPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from ESP settings control
        /// </summary>
        private void ESPControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(ESPPanel) + e.OffsetX;
            var top = Canvas.GetTop(ESPPanel) + e.OffsetY;

            Canvas.SetLeft(ESPPanel, left);
            Canvas.SetTop(ESPPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from ESP settings control
        /// </summary>
        private void ESPControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = ESPPanel.Width + e.DeltaWidth;
            var height = ESPPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_ESP_PANEL_WIDTH);
            height = Math.Max(height, MIN_ESP_PANEL_HEIGHT);

            ESPPanel.Width = width;
            ESPPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Watchlist
        /// <summary>
        /// Handles setting Watchlist panel visibility
        /// </summary>
        private void btnWatchlist_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("Watchlist");
        }

        /// <summary>
        /// Handle close request from Watchlist control
        /// </summary>
        private void WatchlistControl_CloseRequested(object sender, EventArgs e)
        {
            WatchlistPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from Watchlist control
        /// </summary>
        private void WatchlistControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(WatchlistPanel) + e.OffsetX;
            var top = Canvas.GetTop(WatchlistPanel) + e.OffsetY;

            Canvas.SetLeft(WatchlistPanel, left);
            Canvas.SetTop(WatchlistPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from Watchlist control
        /// </summary>
        private void WatchlistControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = WatchlistPanel.Width + e.DeltaWidth;
            var height = WatchlistPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_WATCHLIST_PANEL_WIDTH);
            height = Math.Max(height, MIN_WATCHLIST_PANEL_HEIGHT);

            WatchlistPanel.Width = width;
            WatchlistPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Player History
        /// <summary>
        /// Handles setting Player History panel visibility
        /// </summary>
        private void btnPlayerHistory_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("PlayerHistory");
        }

        /// <summary>
        /// Handle close request from Player History control
        /// </summary>
        private void PlayerHistoryControl_CloseRequested(object sender, EventArgs e)
        {
            PlayerHistoryPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle drag request from Player History control
        /// </summary>
        private void PlayerHistoryControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(PlayerHistoryPanel) + e.OffsetX;
            var top = Canvas.GetTop(PlayerHistoryPanel) + e.OffsetY;

            Canvas.SetLeft(PlayerHistoryPanel, left);
            Canvas.SetTop(PlayerHistoryPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from Player History control
        /// </summary>
        private void PlayerHistoryControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = PlayerHistoryPanel.Width + e.DeltaWidth;
            var height = PlayerHistoryPanel.Height + e.DeltaHeight;

            width = Math.Max(width, MIN_PLAYERHISTORY_PANEL_WIDTH);
            height = Math.Max(height, MIN_PLAYERHISTORY_PANEL_HEIGHT);

            PlayerHistoryPanel.Width = width;
            PlayerHistoryPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #region Map Setup Panel
        /// <summary>
        /// Handles setting map setup panel visibility
        /// </summary>
        private void btnMapSetup_Click(object sender, RoutedEventArgs e)
        {
            NotifyUIActivity();
            TogglePanelVisibility("MapSetup");

            if (LoneMapManager.Map?.Config != null)
            {
                var config = LoneMapManager.Map.Config;
                MapSetupControl.UpdateMapConfiguration(config.X, config.Y, config.Scale);
            }
            else
            {
                MapSetupControl.UpdateMapConfiguration(0, 0, 1);
            }
        }

        /// <summary>
        /// Handle close request from map setup control
        /// </summary>
        private void MapSetupControl_CloseRequested(object sender, EventArgs e)
        {
            GeneralSettingsControl.chkMapSetup.IsChecked = false;
        }

        /// <summary>
        /// Handle drag request from map setup control
        /// </summary>
        private void MapSetupControl_DragRequested(object sender, PanelDragEventArgs e)
        {
            var left = Canvas.GetLeft(MapSetupPanel) + e.OffsetX;
            var top = Canvas.GetTop(MapSetupPanel) + e.OffsetY;

            Canvas.SetLeft(MapSetupPanel, left);
            Canvas.SetTop(MapSetupPanel, top);

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }

        /// <summary>
        /// Handle resize request from map setup control
        /// </summary>
        private void MapSetupControl_ResizeRequested(object sender, PanelResizeEventArgs e)
        {
            var width = MapSetupPanel.Width + e.DeltaWidth;
            var height = MapSetupPanel.Height + e.DeltaHeight;

            width = Math.Max(width, 300);
            height = Math.Max(height, 300);

            MapSetupPanel.Width = width;
            MapSetupPanel.Height = height;

            EnsurePanelInBounds(GeneralSettingsPanel, mainContentGrid, adjustSize: false);
        }
        #endregion

        #endregion

        #region Toolbar Events
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void CustomToolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _isDraggingToolbar = true;
                _toolbarDragStartPoint = e.GetPosition(customToolbar);
                customToolbar.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingToolbar && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(ToolbarCanvas);
                var offsetX = currentPosition.X - _toolbarDragStartPoint.X;
                var offsetY = currentPosition.Y - _toolbarDragStartPoint.Y;

                Canvas.SetLeft(customToolbar, offsetX);
                Canvas.SetTop(customToolbar, offsetY);

                EnsurePanelInBounds(customToolbar, mainContentGrid, adjustSize: false);

                e.Handled = true;
            }
        }

        private void CustomToolbar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingToolbar)
            {
                _isDraggingToolbar = false;
                customToolbar.ReleaseMouseCapture();

                e.Handled = true;
            }
        }

        private void btnRestart_Click(object sender, RoutedEventArgs e)
        {
            Memory.RestartRadar = true;
        }

        private void btnFreeMode_Click(object sender, RoutedEventArgs e)
        {
            _freeMode = !_freeMode;
            if (_freeMode)
            {
                var localPlayer = LocalPlayer;
                if (localPlayer is not null && LoneMapManager.Map?.Config is not null)
                {
                    var localPlayerMapPos = localPlayer.Position.ToMapPos(LoneMapManager.Map.Config);
                    _mapPanPosition = new Vector2
                    {
                        X = localPlayerMapPos.X,
                        Y = localPlayerMapPos.Y
                    };
                }

                if (Application.Current.Resources["RegionBrush"] is SolidColorBrush regionBrush)
                {
                    var regionColor = regionBrush.Color;
                    var newR = (byte)Math.Max(0, regionColor.R > 50 ? regionColor.R - 30 : regionColor.R - 15);
                    var newG = (byte)Math.Max(0, regionColor.G > 50 ? regionColor.G - 30 : regionColor.G - 15);
                    var newB = (byte)Math.Max(0, regionColor.B > 50 ? regionColor.B - 30 : regionColor.B - 15);
                    var darkerColor = Color.FromArgb(regionColor.A, newR, newG, newB);

                    btnFreeMode.Background = new SolidColorBrush(darkerColor);
                }
                else
                {
                    btnFreeMode.Background = new SolidColorBrush(Colors.DarkRed);
                }

                btnFreeMode.ToolTip = "Free Mode (ON) - Click and drag to pan";
            }
            else
            {
                btnFreeMode.Background = new SolidColorBrush(Colors.Transparent);
                btnFreeMode.ToolTip = "Free Mode (OFF) - Map follows player";
            }

            skCanvas.InvalidateVisual();
        }
        #endregion

        #region Window Events
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Growl.ClearGlobal();

                SaveToolbarPosition();
                SavePanelPositions();

                Config.WindowMaximized = (WindowState == WindowState.Maximized);

                if (!Config.WindowMaximized)
                    Config.WindowSize = new Size(ActualWidth, ActualHeight);

                Config.Widgets.AimviewLocation = _aimview.ClientRect;
                Config.Widgets.AimviewMinimized = _aimview.Minimized;
                Config.Widgets.PlayerInfoLocation = _playerInfo.ClientRect;
                Config.Widgets.PlayerInfoMinimized = _playerInfo.Minimized;
                Config.Widgets.DebugInfoLocation = _debugInfo.ClientRect;
                Config.Widgets.DebugInfoMinimized = _debugInfo.Minimized;
                Config.Zoom = _zoom;

                if (ESPForm.Window != null)
                {
                    if (ESPForm.Window.InvokeRequired)
                    {
                        ESPForm.Window.Invoke(new Action(() =>
                        {
                            ESPForm.Window.Close();
                        }));
                    }
                    else
                    {
                        ESPForm.Window.Close();
                    }
                }

                _renderTimer.Dispose();

                Window = null;

                Memory.CloseFPGA(); // Close FPGA
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Error during application shutdown: {ex}");
            }
        }

        private void MainWindow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (InRaid && _mouseOverItem is Player player && player.IsStreaming)
                try
                {
                    Process.Start(new ProcessStartInfo(player.StreamingURL) { UseShellExecute = true });
                }
                catch
                {
                    NotificationsShared.Error("Unable to open this player's Twitch. Do you have a default browser set?");
                }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsLoaded && _panels != null)
            {
                if (_sizeChangeTimer == null)
                {
                    _sizeChangeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _sizeChangeTimer.Tick += (s, args) =>
                    {
                        _sizeChangeTimer.Stop();
                        EnsureAllPanelsInBounds();
                    };
                }

                _sizeChangeTimer.Stop();
                _sizeChangeTimer.Start();
            }
        }
        #endregion

        #region Helper Functions
        private void InitializeUIActivityMonitoring()
        {
            _uiActivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };

            _uiActivityTimer.Tick += (s, e) =>
            {
                _uiInteractionActive = false;
                _uiActivityTimer.Stop();
            };
        }

        private void NotifyUIActivity()
        {
            _uiInteractionActive = true;
            _uiActivityTimer.Stop();
            _uiActivityTimer.Start();
        }

        private void UpdateSmoothValues()
        {
            var currentTime = DateTime.UtcNow;
            var deltaTime = (float)(currentTime - _lastFrameTime).TotalSeconds;
            _lastFrameTime = currentTime;
            deltaTime = Math.Min(deltaTime, 1.0f / 30.0f);

            var needsUpdate = false;
            var zoomDelta = Math.Abs(_targetZoom - _currentZoom);

            if (zoomDelta > MIN_ZOOM_DELTA)
            {
                var t = Math.Min(ZOOM_SMOOTH_FACTOR * deltaTime, 1.0f);
                _currentZoom = _currentZoom + (_targetZoom - _currentZoom) * t;
                needsUpdate = true;
            }
            else
            {
                _currentZoom = _targetZoom;
            }

            var panDelta = Vector2.Distance(_targetPanPosition, _currentPanPosition);

            if (panDelta > MIN_PAN_DELTA)
            {
                var t = Math.Min(PAN_SMOOTH_FACTOR * deltaTime, 1.0f);
                _currentPanPosition = Vector2.Lerp(_currentPanPosition, _targetPanPosition, t);
                needsUpdate = true;
            }
            else
            {
                _currentPanPosition = _targetPanPosition;
            }

            if (needsUpdate)
                skCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Zooms the bitmap 'in'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        /// <param name="mousePosition">Optional mouse position to zoom towards. If null, zooms to center.</param>
        public void ZoomIn(int amt, Point? mousePosition = null)
        {
            var oldZoom = _targetZoom;
            var newZoom = _targetZoom - amt;

            if (newZoom >= 1)
                _targetZoom = newZoom;
            else
                _targetZoom = 1;

            if (mousePosition.HasValue && _freeMode && Math.Abs(_targetZoom - oldZoom) > 0.1f)
                ZoomTowardsPoint(mousePosition.Value, oldZoom, _targetZoom);
        }

        /// <summary>
        /// Zooms the bitmap 'out'.
        /// </summary>
        /// <param name="amt">Amount to zoom in</param>
        public void ZoomOut(int amt)
        {
            var newZoom = _targetZoom + amt;
            if (newZoom <= 200)
                _targetZoom = newZoom;
            else
                _targetZoom = 200;
        }

        private void ZoomTowardsPoint(Point screenPoint, float oldZoom, float newZoom)
        {
            try
            {
                var canvasWidth = (float)skCanvas.ActualWidth;
                var canvasHeight = (float)skCanvas.ActualHeight;
                var mouseOffsetX = (float)screenPoint.X - (canvasWidth / 2f);
                var mouseOffsetY = (float)screenPoint.Y - (canvasHeight / 2f);
                var oldScale = oldZoom / 100f;
                var newScale = newZoom / 100f;
                var scaleDelta = newScale - oldScale;
                var panAdjustmentX = mouseOffsetX * scaleDelta * ZOOM_PAN_STRENGTH;
                var panAdjustmentY = mouseOffsetY * scaleDelta * ZOOM_PAN_STRENGTH;
                var zoomCompensation = Math.Max(0.3f, newZoom / 100f);
                var smoothingFactor = 0.7f * zoomCompensation;

                _targetPanPosition.X -= panAdjustmentX * smoothingFactor;
                _targetPanPosition.Y -= panAdjustmentY * smoothingFactor;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Error in ZoomTowardsPoint: {ex.Message}");
            }
        }

        private void InitializeToolbar()
        {
            RestoreToolbarPosition();

            customToolbar.MouseLeftButtonDown += CustomToolbar_MouseLeftButtonDown;
            customToolbar.MouseMove += CustomToolbar_MouseMove;
            customToolbar.MouseLeftButtonUp += CustomToolbar_MouseLeftButtonUp;
        }

        private void InitializePanels()
        {
            var coordinator = PanelCoordinator.Instance;
            coordinator.RegisterRequiredPanel("GeneralSettings");
            coordinator.RegisterRequiredPanel("MemoryWriting");
            coordinator.RegisterRequiredPanel("ESP");
            coordinator.RegisterRequiredPanel("Watchlist");
            coordinator.RegisterRequiredPanel("PlayerHistory");
            coordinator.AllPanelsReady += OnAllPanelsReady;
        }

        private void OnAllPanelsReady(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                InitializeToolbar();
                InitializePanelsCollection();

                ESPControl.BringToFrontRequested += (s, args) => BringPanelToFront(ESPCanvas);
                GeneralSettingsControl.BringToFrontRequested += (s, args) => BringPanelToFront(GeneralSettingsCanvas);
                MemoryWritingControl.BringToFrontRequested += (s, args) => BringPanelToFront(MemoryWritingCanvas);
                WatchlistControl.BringToFrontRequested += (s, args) => BringPanelToFront(WatchlistCanvas);
                PlayerHistoryControl.BringToFrontRequested += (s, args) => BringPanelToFront(PlayerHistoryCanvas);
                MapSetupControl.BringToFrontRequested += (s, args) => BringPanelToFront(MapSetupCanvas);

                AttachPanelClickHandlers();
                RestorePanelPositions();
                AttachPanelEvents();

                Dispatcher.BeginInvoke(new Action(() => {
                    ValidateAndFixImportedToolbarPosition();
                    ValidateAndFixImportedPanelPositions();
                    EnsureAllPanelsInBounds();
                }), DispatcherPriority.Loaded);
            });

            LoneLogging.WriteLine("[PANELS] All panels are ready!");
        }

        public void EnsureAllPanelsInBounds()
        {
            try
            {
                if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
                    return;

                foreach (var panel in _panels.Values)
                {
                    EnsurePanelInBounds(panel.Panel, mainContentGrid);
                }

                if (customToolbar != null)
                    EnsurePanelInBounds(customToolbar, mainContentGrid);

                LoneLogging.WriteLine("[PANELS] Ensured all panels are within window bounds");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error ensuring panels in bounds: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedPanelPositions()
        {
            try
            {
                if (Config.PanelPositions == null)
                {
                    LoneLogging.WriteLine("[PANELS] No panel positions in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

                if (windowWidth <= 0) windowWidth = 1200;
                if (windowHeight <= 0) windowHeight = 800;

                bool needsSave = false;

                foreach (var panelKey in _panels.Keys)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo?.GetValue(Config.PanelPositions) is PanelPositionConfig posConfig)
                    {
                        var originalLeft = posConfig.Left;
                        var originalTop = posConfig.Top;
                        var originalWidth = posConfig.Width;
                        var originalHeight = posConfig.Height;

                        var minWidth = GetMinimumPanelWidth(_panels[panelKey].Panel);
                        var minHeight = GetMinimumPanelHeight(_panels[panelKey].Panel);

                        if (posConfig.Width < minWidth)
                        {
                            posConfig.Width = minWidth;
                            needsSave = true;
                        }

                        if (posConfig.Height < minHeight)
                        {
                            posConfig.Height = minHeight;
                            needsSave = true;
                        }

                        var maxLeft = windowWidth - posConfig.Width - 10;
                        var maxTop = windowHeight - posConfig.Height - 10;

                        if (posConfig.Left < 0 || posConfig.Left > maxLeft)
                        {
                            posConfig.Left = Math.Max(10, Math.Min(posConfig.Left, maxLeft));
                            needsSave = true;
                        }

                        if (posConfig.Top < 0 || posConfig.Top > maxTop)
                        {
                            posConfig.Top = Math.Max(10, Math.Min(posConfig.Top, maxTop));
                            needsSave = true;
                        }

                        if (needsSave)
                        {
                            LoneLogging.WriteLine($"[PANELS] Fixed imported position for {panelKey}: " +
                                $"({originalLeft},{originalTop},{originalWidth},{originalHeight}) -> " +
                                $"({posConfig.Left},{posConfig.Top},{posConfig.Width},{posConfig.Height})");
                        }
                    }
                }

                if (needsSave)
                {
                    Config.Save();
                    LoneLogging.WriteLine("[PANELS] Saved corrected panel positions");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error validating imported panel positions: {ex.Message}");
            }
        }

        public void ValidateAndFixImportedToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition == null)
                {
                    LoneLogging.WriteLine("[TOOLBAR] No toolbar position in imported config");
                    return;
                }

                var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
                var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

                if (windowWidth <= 0) windowWidth = 1200;
                if (windowHeight <= 0) windowHeight = 800;

                var toolbarConfig = Config.ToolbarPosition;
                var originalLeft = toolbarConfig.Left;
                var originalTop = toolbarConfig.Top;

                var toolbarWidth = customToolbar?.ActualWidth > 0 ? customToolbar.ActualWidth : 200;
                var toolbarHeight = customToolbar?.ActualHeight > 0 ? customToolbar.ActualHeight : 40;

                bool needsSave = false;
                const double minGap = 0;

                var maxLeft = windowWidth - toolbarWidth - minGap;
                var maxTop = windowHeight - toolbarHeight - minGap;

                if (toolbarConfig.Left < 0 || toolbarConfig.Left > maxLeft)
                {
                    toolbarConfig.Left = Math.Max(0, Math.Min(toolbarConfig.Left, maxLeft));
                    needsSave = true;
                }

                if (toolbarConfig.Top < 0 || toolbarConfig.Top > maxTop)
                {
                    toolbarConfig.Top = Math.Max(0, Math.Min(toolbarConfig.Top, maxTop));
                    needsSave = true;
                }

                if (needsSave)
                {
                    Config.Save();
                    LoneLogging.WriteLine($"[TOOLBAR] Fixed imported toolbar position: ({originalLeft},{originalTop}) -> ({toolbarConfig.Left},{toolbarConfig.Top})");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TOOLBAR] Error validating imported toolbar position: {ex.Message}");
            }
        }

        public void EnsurePanelInBounds(FrameworkElement panel, FrameworkElement container, bool adjustSize = true)
        {
            if (panel == null || container == null)
                return;

            try
            {
                var left = Canvas.GetLeft(panel);
                var top = Canvas.GetTop(panel);

                if (double.IsNaN(left)) left = 5;
                if (double.IsNaN(top)) top = 5;

                var containerWidth = container.ActualWidth;
                var containerHeight = container.ActualHeight;

                if (containerWidth <= 0) containerWidth = 1200;
                if (containerHeight <= 0) containerHeight = 800;

                var panelWidth = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
                var panelHeight = panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height;

                if (adjustSize)
                {
                    if (panelWidth <= 0 || double.IsNaN(panelWidth))
                        panelWidth = GetMinimumPanelWidth(panel);
                    if (panelHeight <= 0 || double.IsNaN(panelHeight))
                        panelHeight = GetMinimumPanelHeight(panel);

                    panelWidth = Math.Min(panelWidth, containerWidth * 0.9);
                    panelHeight = Math.Min(panelHeight, containerHeight * 0.9);
                }

                const double padding = 0;
                var maxLeft = containerWidth - panelWidth - padding;
                var maxTop = containerHeight - panelHeight - padding;

                left = Math.Max(padding, Math.Min(left, maxLeft));
                top = Math.Max(padding, Math.Min(top, maxTop));

                Canvas.SetLeft(panel, left);
                Canvas.SetTop(panel, top);

                if (adjustSize)
                {
                    if (panel.Width != panelWidth) panel.Width = panelWidth;
                    if (panel.Height != panelHeight) panel.Height = panelHeight;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error in EnsurePanelInBounds for {panel?.Name}: {ex.Message}");

                Canvas.SetLeft(panel, 0);
                Canvas.SetTop(panel, 0);
            }
        }

        private double GetMinimumPanelWidth(FrameworkElement panel)
        {
            return panel?.Name switch
            {
                "GeneralSettingsPanel" => MIN_SETTINGS_PANEL_WIDTH,
                "MemoryWritingPanel" => MIN_MEMORY_WRITING_PANEL_WIDTH,
                "ESPPanel" => MIN_ESP_PANEL_WIDTH,
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_WIDTH,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_WIDTH,
                "MapSetupPanel" => 300,
                _ => 200
            };
        }

        private double GetMinimumPanelHeight(FrameworkElement panel)
        {
            return panel?.Name switch
            {
                "GeneralSettingsPanel" => MIN_SETTINGS_PANEL_HEIGHT,
                "MemoryWritingPanel" => MIN_MEMORY_WRITING_PANEL_HEIGHT,
                "ESPPanel" => MIN_ESP_PANEL_HEIGHT,
                "WatchlistPanel" => MIN_WATCHLIST_PANEL_HEIGHT,
                "PlayerHistoryPanel" => MIN_PLAYERHISTORY_PANEL_HEIGHT,
                "MapSetupPanel" => 300,
                _ => 200
            };
        }

        private void BringPanelToFront(Canvas panelCanvas)
        {
            var canvases = new List<Canvas>
            {
                GeneralSettingsCanvas,
                MemoryWritingCanvas,
                ESPCanvas,
                WatchlistCanvas,
                PlayerHistoryCanvas,
                MapSetupCanvas
            };

            foreach (var canvas in canvases)
            {
                Canvas.SetZIndex(canvas, 1000);
            }

            Canvas.SetZIndex(panelCanvas, 1001);
        }

        private void AttachPreviewMouseDown(FrameworkElement panel, Canvas canvas)
        {
            panel.PreviewMouseDown += (s, e) => {
                BringPanelToFront(canvas);
            };
        }

        private void AttachPanelClickHandlers()
        {
            AttachPreviewMouseDown(GeneralSettingsPanel, GeneralSettingsCanvas);
            AttachPreviewMouseDown(MemoryWritingPanel, MemoryWritingCanvas);
            AttachPreviewMouseDown(ESPPanel, ESPCanvas);
            AttachPreviewMouseDown(WatchlistPanel, WatchlistCanvas);
            AttachPreviewMouseDown(PlayerHistoryPanel, PlayerHistoryCanvas);
            AttachPreviewMouseDown(MapSetupPanel, MapSetupCanvas);

            ESPCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(ESPCanvas);
            GeneralSettingsCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(GeneralSettingsCanvas);
            MemoryWritingCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MemoryWritingCanvas);
            WatchlistCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(WatchlistCanvas);
            PlayerHistoryCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(PlayerHistoryCanvas);
            MapSetupCanvas.PreviewMouseDown += (s, e) => BringPanelToFront(MapSetupCanvas);
        }

        private void TogglePanelVisibility(string panelKey)
        {
            if (_panels.TryGetValue(panelKey, out var panelInfo))
            {
                if (panelInfo.Panel.Visibility == Visibility.Visible)
                {
                    panelInfo.Panel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panelInfo.Panel, panelInfo.Canvas);
                        }
                        else
                        {
                            Canvas.SetLeft(panelInfo.Panel, mainContentGrid.ActualWidth - panelInfo.Panel.Width - 20);
                            Canvas.SetTop(panelInfo.Panel, 20);
                        }
                    }

                    panelInfo.Panel.Visibility = Visibility.Visible;
                    BringPanelToFront(panelInfo.Canvas);
                }

                SaveSinglePanelPosition(panelKey);
            }
        }

        private void AttachPanelEvents()
        {
            EventHandler<PanelDragEventArgs> sharedDragHandler = (s, e) => {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
                    {
                        var left = Canvas.GetLeft(panelInfo.Panel) + e.OffsetX;
                        var top = Canvas.GetTop(panelInfo.Panel) + e.OffsetY;

                        Canvas.SetLeft(panelInfo.Panel, left);
                        Canvas.SetTop(panelInfo.Panel, top);

                        EnsurePanelInBounds(panelInfo.Panel, mainContentGrid, adjustSize: false);
                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            EventHandler<PanelResizeEventArgs> sharedResizeHandler = (s, e) => {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
                    {
                        var width = panelInfo.Panel.Width + e.DeltaWidth;
                        var height = panelInfo.Panel.Height + e.DeltaHeight;

                        width = Math.Max(width, panelInfo.MinWidth);
                        height = Math.Max(height, panelInfo.MinHeight);

                        var currentLeft = Canvas.GetLeft(panelInfo.Panel);
                        var currentTop = Canvas.GetTop(panelInfo.Panel);

                        var maxWidth = mainContentGrid.ActualWidth - currentLeft;
                        var maxHeight = mainContentGrid.ActualHeight - currentTop;

                        width = Math.Min(width, Math.Max(panelInfo.MinWidth, maxWidth));
                        height = Math.Min(height, Math.Max(panelInfo.MinHeight, maxHeight));

                        panelInfo.Panel.Width = width;
                        panelInfo.Panel.Height = height;

                        EnsurePanelInBounds(panelInfo.Panel, mainContentGrid, adjustSize: false);

                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            EventHandler sharedCloseHandler = (s, e) => {
                NotifyUIActivity();
                var controlName = (s as UserControl)?.Name;
                if (controlName != null && controlName.EndsWith("Control") && controlName.Length > "Control".Length)
                {
                    string panelKey = controlName.Substring(0, controlName.Length - "Control".Length);
                    if (_panels.TryGetValue(panelKey, out var panelInfo))
                    {
                        panelInfo.Panel.Visibility = Visibility.Collapsed;
                        SaveSinglePanelPosition(panelKey);
                    }
                }
            };

            GeneralSettingsControl.DragRequested += sharedDragHandler;
            GeneralSettingsControl.ResizeRequested += sharedResizeHandler;
            GeneralSettingsControl.CloseRequested += sharedCloseHandler;

            MemoryWritingControl.DragRequested += sharedDragHandler;
            MemoryWritingControl.ResizeRequested += sharedResizeHandler;
            MemoryWritingControl.CloseRequested += sharedCloseHandler;

            ESPControl.DragRequested += sharedDragHandler;
            ESPControl.ResizeRequested += sharedResizeHandler;
            ESPControl.CloseRequested += sharedCloseHandler;

            WatchlistControl.DragRequested += sharedDragHandler;
            WatchlistControl.ResizeRequested += sharedResizeHandler;
            WatchlistControl.CloseRequested += sharedCloseHandler;

            PlayerHistoryControl.DragRequested += sharedDragHandler;
            PlayerHistoryControl.ResizeRequested += sharedResizeHandler;
            PlayerHistoryControl.CloseRequested += sharedCloseHandler;

            MapSetupControl.DragRequested += sharedDragHandler;
            MapSetupControl.CloseRequested += sharedCloseHandler;
        }

        private void InitializePanelsCollection()
        {
            _panels = new Dictionary<string, PanelInfo>
            {
                ["GeneralSettings"] = new PanelInfo(GeneralSettingsPanel, GeneralSettingsCanvas, "GeneralSettings", MIN_SETTINGS_PANEL_WIDTH, MIN_SETTINGS_PANEL_HEIGHT),
                ["MemoryWriting"] = new PanelInfo(MemoryWritingPanel, MemoryWritingCanvas, "MemoryWriting", MIN_MEMORY_WRITING_PANEL_WIDTH, MIN_MEMORY_WRITING_PANEL_HEIGHT),
                ["ESP"] = new PanelInfo(ESPPanel, ESPCanvas, "ESP", MIN_ESP_PANEL_WIDTH, MIN_ESP_PANEL_HEIGHT),
                ["Watchlist"] = new PanelInfo(WatchlistPanel, WatchlistCanvas, "Watchlist", MIN_WATCHLIST_PANEL_WIDTH, MIN_WATCHLIST_PANEL_HEIGHT),
                ["PlayerHistory"] = new PanelInfo(PlayerHistoryPanel, PlayerHistoryCanvas, "PlayerHistory", MIN_PLAYERHISTORY_PANEL_WIDTH, MIN_PLAYERHISTORY_PANEL_HEIGHT),
                ["MapSetup"] = new PanelInfo(MapSetupPanel, MapSetupCanvas, "MapSetup", 300, 300)
            };
        }

        private void SavePanelPositions()
        {
            try
            {
                foreach (var panel in _panels)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panel.Value.Panel, panel.Value.Canvas);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }

                Config.Save();
                LoneLogging.WriteLine("[PANELS] Saved panel positions to config");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error saving panel positions: {ex.Message}");
            }
        }

        private void SaveSinglePanelPosition(string panelKey)
        {
            try
            {
                if (_panels.TryGetValue(panelKey, out var panelInfo))
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panelKey);
                    if (propInfo != null)
                    {
                        var posConfig = PanelPositionConfig.FromPanel(panelInfo.Panel, panelInfo.Canvas);
                        propInfo.SetValue(Config.PanelPositions, posConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error saving panel position for {panelKey}: {ex.Message}");
            }
        }

        public void RestorePanelPositions()
        {
            try
            {
                foreach (var panel in _panels)
                {
                    var propInfo = typeof(PanelPositionsConfig).GetProperty(panel.Key);

                    if (propInfo != null)
                    {
                        var posConfig = propInfo.GetValue(Config.PanelPositions) as PanelPositionConfig;

                        if (posConfig != null)
                        {
                            posConfig.ApplyToPanel(panel.Value.Panel, panel.Value.Canvas);
                            EnsurePanelInBounds(panel.Value.Panel, mainContentGrid, adjustSize: false);
                        }
                        else
                        {
                            Canvas.SetLeft(panel.Value.Panel, 20);
                            Canvas.SetTop(panel.Value.Panel, 20);
                        }
                    }
                }

                LoneLogging.WriteLine("[PANELS] Restored panel positions from config with bounds checking");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PANELS] Error restoring panel positions: {ex.Message}");
            }
        }

        private void SaveToolbarPosition()
        {
            try
            {
                Config.ToolbarPosition = ToolbarPositionConfig.FromToolbar(customToolbar);
                LoneLogging.WriteLine("[TOOLBAR] Saved toolbar position to config");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TOOLBAR] Error saving toolbar position: {ex.Message}");
            }
        }

        public void RestoreToolbarPosition()
        {
            try
            {
                if (Config.ToolbarPosition != null)
                {
                    Config.ToolbarPosition.ApplyToToolbar(customToolbar);
                    LoneLogging.WriteLine("[TOOLBAR] Restored toolbar position from config");
                }
                else
                {
                    Canvas.SetLeft(customToolbar, 900);
                    Canvas.SetTop(customToolbar, 5);
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[TOOLBAR] Error restoring toolbar position: {ex.Message}");
                Canvas.SetLeft(customToolbar, 900);
                Canvas.SetTop(customToolbar, 5);
            }
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public IAsyncResult BeginInvoke(Action method)
        {
            return (IAsyncResult)Dispatcher.BeginInvoke(method);
        }
        #endregion

        private class PanelInfo
        {
            public Border Panel { get; set; }
            public Canvas Canvas { get; set; }
            public string ConfigName { get; set; }
            public int MinWidth { get; set; }
            public int MinHeight { get; set; }

            public PanelInfo(Border panel, Canvas canvas, string configName, int minWidth, int minHeight)
            {
                Panel = panel;
                Canvas = canvas;
                ConfigName = configName;
                MinWidth = minWidth;
                MinHeight = minHeight;
            }
        }

        private class PingEffect
        {
            public Vector3 Position;
            public DateTime StartTime;
            public float DurationSeconds = 2f;
        }
    }
}