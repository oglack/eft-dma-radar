﻿﻿using arena_dma_radar.Arena.ArenaPlayer.Plugins;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.Features.MemoryWrites;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.UI.Controls;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_shared.Common.Unity.LowLevel.Chams;
using eft_dma_shared.Common.Unity.LowLevel.Chams.Arena;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using eft_dma_shared.Common.Unity.LowLevel.Types;
using HandyControl.Controls;
using HandyControl.Data;
using HandyControl.Themes;
using Microsoft.AspNetCore.Identity;
using System;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static arena_dma_radar.Arena.ArenaPlayer.Player;
using static SDK.Offsets;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = eft_dma_shared.Common.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using Window = System.Windows.Window;

namespace arena_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for ESPSettingsControl.xaml
    /// </summary>
    public partial class ESPControl : UserControl
    {
        #region Fields and Properties
        private const int INTERVAL = 100; // 0.1 second
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        private PopupWindow _openColorPicker;

        private static Config Config => Program.Config;

        private static readonly object _chamsColorLock = new object();
        private static bool _isApplyingChamsColors = false;
        private bool _isRefreshingChamsMaterials = false;
        private DispatcherTimer _chamsMaterialStatusTimer;
        private ChamsMode _selectedColorMaterialType = ChamsMode.WireFrame;
        private ChamsEntityType _selectedEntityType = ChamsEntityType.PMC;
        private static readonly Dictionary<string, ChamsMode> _materialTypeMapping = new()
        {
            { "Basic", ChamsMode.Basic },
            { "Visible", ChamsMode.Visible },
            { "WireFrame", ChamsMode.WireFrame },
            { "VisCheckGlow", ChamsMode.VisCheckGlow },
            { "VisCheckFlat", ChamsMode.VisCheckFlat }
        };

        private bool _isImporting = false;

        private string _currentFuserPlayerType;
        private string _currentFuserEntityType;
        private bool _isLoadingFuserOptionSettings = false;
        private bool _isLoadingFuserPlayerSettings = false;
        private bool _isLoadingFuserEntitySettings = false;

        private readonly string[] _availableFuserOptions = new string[]
        {
            "Fireport Aim",
            "Aimbot FOV",
            "Raid Stats",
            "Aimbot Lock",
            "Status Text",
            "FPS",
            "Magazine Info"
        };

        private readonly string[] _availableFuserPlayerInformation = new string[]
        {
            "Bomb",
            "ADS",
            "Ammo Type",
            "Distance",
            "Health",
            "Name",
            "Weapon"
        };

        private readonly string[] _availableFuserEntityInformation = new string[]
        {
            "Name",
            "Distance"
        };
        #endregion

        public ESPControl()
        {
            InitializeComponent();
            TooltipManager.AssignESPTips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("ESP");
                ExpanderManager.Instance.RegisterExpanders(this, "ESPSettings",
                    expChamsGeneralSettings,
                    expFuserGeneralSettings,
                    expFuserCrosshairSettings,
                    expFuserPlayerInformation,
                    expFuserEntityInformation);

                try
                {
                    await PanelCoordinator.Instance.WaitForAllPanelsAsync();

                    InitializeControlEvents();
                    LoadSettings();
                }
                catch (TimeoutException ex)
                {
                    LoneLogging.WriteLine($"[PANELS] {ex.Message}");
                }
            };
        }

        #region ESP Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterChamsEvents();
                RegisterFuserEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
        }

        public async void LoadSettings()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                LoadChamsSettings();
                LoadFuserSettings();
            });

            if (Config.ChamsConfig.Enabled && MemWrites.Enabled && !_isImporting)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ApplyAllChamsColorsAsync();
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[ESP Control] Error applying chams colors in background: {ex.Message}");
                    }
                });
            }
        }
        #endregion

        #region Events
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
            _openColorPicker?.Close();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BringToFrontRequested?.Invoke(this, EventArgs.Empty);

            DragHandle.CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            DragHandle.MouseMove += DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _dragStartPoint;

                DragRequested?.Invoke(this, new PanelDragEventArgs(offset.X, offset.Y));
            }
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DragHandle.ReleaseMouseCapture();
            DragHandle.MouseMove -= DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
        }

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            ((UIElement)sender).MouseMove += ResizeHandle_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var sizeDelta = currentPosition - _dragStartPoint;

                ResizeRequested?.Invoke(this, new PanelResizeEventArgs(sizeDelta.X, sizeDelta.Y));
                _dragStartPoint = currentPosition;
            }
        }

        private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).ReleaseMouseCapture();
            ((UIElement)sender).MouseMove -= ResizeHandle_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp -= ResizeHandle_MouseLeftButtonUp;
        }
        #endregion
        #endregion

        #region Chams Tab
        #region Functions/Methods
        private void LoadChamsSettings()
        {
            var cfg = Config.ChamsConfig;
            chkEnableChams.IsChecked = cfg.Enabled;

            cfg.InitializeDefaults();
            ToggleChamsControls();
            FeatureInstanceCheck();

            cboChamsEntityType.SelectedIndex = 0;
            SetSelectedEntityType();
            UpdateEntityChamsSettings();
            UpdateChamsMaterialStatus();
        }

        private void RegisterChamsEvents()
        {
            // Main controls
            chkEnableChams.Checked += ChamsCheckbox_Checked;
            chkEnableChams.Unchecked += ChamsCheckbox_Checked;

            // Entity controls
            chkEntityEnabled.Checked += ChamsCheckbox_Checked;
            chkEntityEnabled.Unchecked += ChamsCheckbox_Checked;
            cboChamsEntityType.SelectionChanged += cboChamsEntityType_SelectionChanged;

            // Player-specific controls
            RegisterPlayerChamsEvents();

            // Loot-specific controls
            cboMaterialType.SelectionChanged += cboMaterialType_SelectionChanged;

            // Color material type selection
            cboColorMaterialType.SelectionChanged += cboColorMaterialType_SelectionChanged;

            // Color controls
            btnChamsVisibleColor.Click += ChamsColorButton_Clicked;
            btnChamsInvisibleColor.Click += ChamsColorButton_Clicked;

            // Material management
            btnRefreshChamsMaterials.Click += btnRefreshChamsMaterials_Click;
            btnClearChamsCache.Click += btnClearChamsCache_Click;

            InitializeChamsMaterialStatusTimer();
        }

        private void RegisterPlayerChamsEvents()
        {
            var playerCheckboxes = new[] { chkClothingChamsEnabled, chkGearChamsEnabled, chkDeathMaterialEnabled };
            var playerCombos = new[] { cboClothingMaterialType, cboGearMaterialType, cboDeathMaterialType };

            foreach (var cb in playerCheckboxes)
            {
                cb.Checked += ChamsCheckbox_Checked;
                cb.Unchecked += ChamsCheckbox_Checked;
            }

            cboClothingMaterialType.SelectionChanged += cboClothingMaterialType_SelectionChanged;
            cboGearMaterialType.SelectionChanged += cboGearMaterialType_SelectionChanged;
            cboDeathMaterialType.SelectionChanged += cboDeathMaterialType_SelectionChanged;
        }

        private void SetSelectedEntityType()
        {
            if (cboChamsEntityType.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<ChamsEntityType>(tag, out var type))
            {
                _selectedEntityType = type;
            }
        }

        private bool IsPlayerEntityType() => ChamsManager.IsPlayerEntityType(_selectedEntityType);

        private void UpdateEntityChamsSettings()
        {
            var entityType = _selectedEntityType;
            var chams = Config.ChamsConfig.GetEntitySettings(entityType);

            try
            {
                UpdateVisibilityGroups();
                UpdateEntityEnabledState(chams);

                if (IsPlayerEntityType())
                    UpdatePlayerEntitySettings(chams);
                else
                    UpdateLootEntitySettings(chams);

                UpdateColorMaterialTypeSelection();
                UpdateColorSettings();
                ValidateAdvancedModes(chams);
                RefreshControlStates();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ERROR] UpdateEntityChamsSettings failed for {entityType}: {ex}");
            }
        }

        private void UpdateVisibilityGroups()
        {
            var isPlayer = IsPlayerEntityType();
            playerEntityGroup.Visibility = isPlayer ? Visibility.Visible : Visibility.Collapsed;
            materialGroup.Visibility = isPlayer ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateColorMaterialTypeSelection()
        {
            SetComboSelection(cboColorMaterialType, _selectedColorMaterialType);
        }

        private void UpdateEntityEnabledState(ChamsConfig.EntityChamsSettings chams)
        {
            chkEntityEnabled.IsChecked = chams.Enabled;
        }

        private void UpdatePlayerEntitySettings(ChamsConfig.EntityChamsSettings chams)
        {
            chkClothingChamsEnabled.IsChecked = chams.ClothingChamsEnabled;
            chkGearChamsEnabled.IsChecked = chams.GearChamsEnabled;
            chkDeathMaterialEnabled.IsChecked = chams.DeathMaterialEnabled;

            SetComboSelection(cboClothingMaterialType, chams.ClothingChamsMode);
            SetComboSelection(cboGearMaterialType, chams.GearChamsMode);
            SetComboSelection(cboDeathMaterialType, chams.DeathMaterialMode);
        }

        private void UpdateLootEntitySettings(ChamsConfig.EntityChamsSettings chams)
        {
            SetComboSelection(cboMaterialType, chams.Mode);
        }

        private void UpdateColorSettings()
        {
            var materialColorSettings = Config.ChamsConfig.GetMaterialColorSettings(_selectedEntityType, _selectedColorMaterialType);

            if (ColorConverter.ConvertFromString(materialColorSettings.VisibleColor) is Color visible)
                btnChamsVisibleColor.Background = new SolidColorBrush(visible);

            if (ColorConverter.ConvertFromString(materialColorSettings.InvisibleColor) is Color invisible)
                btnChamsInvisibleColor.Background = new SolidColorBrush(invisible);
        }

        private void SetComboSelection(ComboBox combo, ChamsMode targetMode)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag is string tag && _materialTypeMapping.TryGetValue(tag, out var mode) && mode == targetMode)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        private void RefreshControlStates()
        {
            ToggleChamsControls();
            ToggleEntityChamsControls();
            ToggleMaterialTypeControls();
        }

        private void ToggleChamsControls()
        {
            var memWrites = MemWrites.Enabled;
            var advMemWrites = MemWrites.Config.AdvancedMemWrites;
            var controlEnabled = memWrites && Config.ChamsConfig.Enabled;

            chkEnableChams.IsEnabled = memWrites;
            chkEntityEnabled.IsEnabled = controlEnabled;
            cboChamsEntityType.IsEnabled = controlEnabled;

            btnRefreshChamsMaterials.IsEnabled = controlEnabled && advMemWrites && !_isRefreshingChamsMaterials;
            btnClearChamsCache.IsEnabled = controlEnabled && advMemWrites && !_isRefreshingChamsMaterials;

            ToggleChamsColorControls();
        }

        private void ToggleChamsColorControls()
        {
            var entityEnabled = chkEntityEnabled.IsChecked == true;
            var baseEnabled = MemWrites.Enabled && MemWrites.Config.AdvancedMemWrites && Config.ChamsConfig.Enabled;
            var finalEnabled = baseEnabled && entityEnabled;

            cboColorMaterialType.IsEnabled = finalEnabled;
            btnChamsVisibleColor.IsEnabled = finalEnabled;
            btnChamsInvisibleColor.IsEnabled = finalEnabled;
        }

        private void ToggleEntityChamsControls()
        {
            var entityEnabled = chkEntityEnabled.IsChecked == true;
            var advWrites = MemWrites.Config.AdvancedMemWrites;
            var baseEnabled = MemWrites.Enabled && Config.ChamsConfig.Enabled;
            var finalEnabled = baseEnabled && entityEnabled;

            if (IsPlayerEntityType())
            {
                playerEntityGroup.IsEnabled = finalEnabled;
                TogglePlayerControls(finalEnabled);
            }
            else
            {
                materialGroup.IsEnabled = finalEnabled;
                cboMaterialType.IsEnabled = finalEnabled;
            }

            cboColorMaterialType.IsEnabled = advWrites && finalEnabled;
            btnChamsVisibleColor.IsEnabled = advWrites && finalEnabled;
            btnChamsInvisibleColor.IsEnabled = advWrites && finalEnabled;
        }

        private void TogglePlayerControls(bool baseEnabled)
        {
            chkClothingChamsEnabled.IsEnabled = baseEnabled;
            chkGearChamsEnabled.IsEnabled = baseEnabled;
            chkDeathMaterialEnabled.IsEnabled = baseEnabled;

            cboClothingMaterialType.IsEnabled = baseEnabled && (chkClothingChamsEnabled.IsChecked == true);
            cboGearMaterialType.IsEnabled = baseEnabled && (chkGearChamsEnabled.IsChecked == true);
            cboDeathMaterialType.IsEnabled = baseEnabled && (chkDeathMaterialEnabled.IsChecked == true);
        }

        private void ToggleMaterialTypeControls()
        {
            var advWrites = Config.MemWrites.AdvancedMemWrites;

            var combos = IsPlayerEntityType()
                ? new[] { cboClothingMaterialType, cboGearMaterialType, cboDeathMaterialType }
                : new[] { cboMaterialType };

            foreach (var combo in combos)
                ToggleAdvancedModeItems(combo, advWrites);
        }

        private void ToggleAdvancedModeItems(ComboBox combo, bool advancedEnabled)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag is string tag)
                {
                    var isAdvanced = tag is "WireFrame" or "VisCheckGlow" or "VisCheckFlat";
                    item.IsEnabled = !isAdvanced || advancedEnabled;
                }
            }
        }

        private void ValidateAdvancedModes(ChamsConfig.EntityChamsSettings chams)
        {
            var enabled = Config.ChamsConfig.Enabled && MemWrites.Enabled;
            var advWrites = Config.MemWrites.AdvancedMemWrites;

            if (!enabled || advWrites) return;

            bool changed = false;
            var entityType = _selectedEntityType;

            if (IsPlayerEntityType())
            {
                if (RequiresModeValidation(chams.ClothingChamsMode))
                {
                    chams.ClothingChamsMode = ChamsMode.Basic;
                    changed = true;
                    LoneLogging.WriteLine($"[CHAMS] Forcing {entityType} clothing mode to Basic due to AdvancedMemWrites being off");
                }

                if (RequiresModeValidation(chams.GearChamsMode))
                {
                    chams.GearChamsMode = ChamsMode.Basic;
                    changed = true;
                    LoneLogging.WriteLine($"[CHAMS] Forcing {entityType} gear mode to Basic due to AdvancedMemWrites being off");
                }

                if (RequiresModeValidation(chams.DeathMaterialMode))
                {
                    chams.DeathMaterialMode = ChamsMode.Basic;
                    changed = true;
                    LoneLogging.WriteLine($"[CHAMS] Forcing {entityType} death mode to Basic due to AdvancedMemWrites being off");
                }
            }
            else if (RequiresModeValidation(chams.Mode))
            {
                chams.Mode = ChamsMode.Basic;
                changed = true;
                LoneLogging.WriteLine($"[CHAMS] Forcing {entityType} mode to Basic due to AdvancedMemWrites being off");
            }

            if (changed) Config.Save();
        }

        private bool RequiresModeValidation(ChamsMode mode) =>
            mode != ChamsMode.Basic && mode != ChamsMode.Visible;

        private void UpdateChamsMode(ChamsMode mode, string propertyName)
        {
            if (!ValidateModeForAdvancedWrites(mode)) return;

            var chams = Config.ChamsConfig.GetEntitySettings(_selectedEntityType);

            switch (propertyName)
            {
                case "Mode": chams.Mode = mode; break;
                case "ClothingChamsMode": chams.ClothingChamsMode = mode; break;
                case "GearChamsMode": chams.GearChamsMode = mode; break;
                case "DeathMaterialMode": chams.DeathMaterialMode = mode; break;
            }

            Config.Save();
        }

        private bool ValidateModeForAdvancedWrites(ChamsMode mode)
        {
            var isAdvanced = Config.MemWrites.AdvancedMemWrites;
            if (isAdvanced || mode == ChamsMode.Basic || mode == ChamsMode.Visible)
                return true;

            LoneLogging.WriteLine($"[CHAMS] Attempted to set advanced mode {mode} while AdvancedMemWrites is off. Defaulting to Basic.");
            return false;
        }

        private void ChamsColorButton_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                var brush = btn.Background as SolidColorBrush ?? new SolidColorBrush(Colors.Red);
                OpenChamsColorPicker(btn, brush, tag);
            }
        }

        private SolidColorBrush OpenChamsColorPicker(Button sourceButton, SolidColorBrush currentBrush, string tag)
        {
            _openColorPicker?.Close();

            var actualBrush = sourceButton.Background as SolidColorBrush ?? currentBrush;
            var picker = HandyControl.Tools.SingleOpenHelper.CreateControl<HandyControl.Controls.ColorPicker>();
            picker.SelectedBrush = actualBrush;
            var window = new HandyControl.Controls.PopupWindow
            {
                PopupElement = picker,
                AllowsTransparency = true,
                WindowStyle = WindowStyle.None,
                MinWidth = 0,
                MinHeight = 0
            };

            _openColorPicker = window;

            var originalBrush = actualBrush.Clone();
            var resultBrush = actualBrush;
            var confirmed = false;

            var parentWindow = Window.GetWindow(sourceButton);
            var espPanel = parentWindow?.FindName("ESPPanel") as FrameworkElement;

            void UpdatePickerPosition()
            {
                try
                {
                    var buttonPos = sourceButton.PointToScreen(new Point(0, 0));
                    var leftPos = buttonPos.X + sourceButton.ActualWidth - 5;
                    var topPos = buttonPos.Y - sourceButton.ActualHeight - 5;
                    window.Left = leftPos;
                    window.Top = topPos;
                }
                catch { }
            }

            EventHandler parentLocationChanged = (s, e) => UpdatePickerPosition();
            SizeChangedEventHandler parentSizeChanged = (s, e) => UpdatePickerPosition();
            EventHandler panelLayoutUpdated = (s, e) => UpdatePickerPosition();

            if (parentWindow != null)
            {
                parentWindow.LocationChanged += parentLocationChanged;
                parentWindow.SizeChanged += parentSizeChanged;
            }

            if (espPanel != null)
            {
                espPanel.LayoutUpdated += panelLayoutUpdated;
            }

            picker.SelectedColorChanged += (s, args) =>
            {
                if (picker.SelectedBrush != null)
                    sourceButton.Background = picker.SelectedBrush;
            };

            picker.Confirmed += (s, args) =>
            {
                if (picker.SelectedBrush is SolidColorBrush scb)
                {
                    resultBrush = scb;
                    confirmed = true;
                    UpdateChamsColor(tag, scb);
                }
                window.Close();
            };

            picker.Canceled += (s, args) =>
            {
                sourceButton.Background = originalBrush;
                window.Close();
            };

            window.Loaded += (s, e) =>
            {
                UpdatePickerPosition();
            };

            window.Closed += (s, e) =>
            {
                _openColorPicker = null;

                if (parentWindow != null)
                {
                    parentWindow.LocationChanged -= parentLocationChanged;
                    parentWindow.SizeChanged -= parentSizeChanged;
                }

                if (espPanel != null)
                {
                    espPanel.LayoutUpdated -= panelLayoutUpdated;
                }
            };

            window.Show(sourceButton, false);
            return confirmed ? resultBrush : null;
        }

        private void UpdateChamsColor(string tag, SolidColorBrush brush)
        {
            var hex = brush.Color.ToString();
            var materialColorSettings = Config.ChamsConfig.GetMaterialColorSettings(_selectedEntityType, _selectedColorMaterialType);
            var isVisible = tag.Contains("VisibleColor");

            if (isVisible)
                materialColorSettings.VisibleColor = hex;
            else
                materialColorSettings.InvisibleColor = hex;

            Config.Save();
            ApplyChamsColorToMaterials(isVisible, brush.Color, _selectedColorMaterialType);
        }

        private void ApplyChamsColorToMaterials(bool isVisible, Color color, ChamsMode specificMaterialMode)
        {
            try
            {
                if (!Config.ChamsConfig.Enabled || !MemWrites.Enabled)
                    return;

                var unityColor = new UnityColor(color.R, color.G, color.B, color.A);

                if (!Config.AdvancedMemWrites)
                {
                    if (Memory.Game is not LocalGameWorld game)
                        return;

                    var cm = game.CameraManager;
                    var chams = Config.ChamsConfig.GetEntitySettings(_selectedEntityType);

                    if (chams.Mode == ChamsMode.Visible)
                    {
                        var nvg = MonoBehaviour.GetComponent(cm.FPSCamera, "NightVision");
                        if (nvg != 0)
                        {
                            var addr = nvg + 0xE0;
                            Memory.WriteValue(addr, unityColor);
                        }
                    }

                    return;
                }

                using var colorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);

                foreach (var materialKvp in ChamsManager.Materials)
                {
                    var (mode, entityType) = materialKvp.Key;
                    var material = materialKvp.Value;

                    if (entityType != _selectedEntityType || mode != specificMaterialMode || material.InstanceID == 0)
                        continue;

                    try
                    {
                        if (isVisible)
                            NativeMethods.SetMaterialColor(colorMem, material.Address, material.ColorVisible, unityColor);
                        else
                            NativeMethods.SetMaterialColor(colorMem, material.Address, material.ColorInvisible, unityColor);

                        LoneLogging.WriteLine($"[CHAMS] Applied {(isVisible ? "visible" : "invisible")} color to {mode}/{entityType}");
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[CHAMS] Failed to apply color to {mode}/{entityType}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS] Failed to apply color to materials: {ex.Message}");
            }
        }

        public void ApplyAllConfiguredMaterialColors()
        {
            try
            {
                if (!Config.ChamsConfig.Enabled || !MemWrites.Enabled)
                    return;

                LoneLogging.WriteLine("[ESP Control] Applying all configured material colors...");

                using var colorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);

                foreach (var entityKvp in Config.ChamsConfig.EntityChams)
                {
                    var entityType = entityKvp.Key;
                    var entitySettings = entityKvp.Value;

                    if (entitySettings.MaterialColors == null)
                        continue;

                    foreach (var materialColorKvp in entitySettings.MaterialColors)
                    {
                        var materialMode = materialColorKvp.Key;
                        var colorSettings = materialColorKvp.Value;

                        if (!ChamsManager.Materials.TryGetValue((materialMode, entityType), out var material) || material.InstanceID == 0)
                            continue;

                        try
                        {
                            if (SKColor.TryParse(colorSettings.VisibleColor, out var visibleColor))
                            {
                                var visibleUnityColor = new UnityColor(visibleColor.Red, visibleColor.Green, visibleColor.Blue, visibleColor.Alpha);
                                NativeMethods.SetMaterialColor(colorMem, material.Address, material.ColorVisible, visibleUnityColor);
                            }

                            if (SKColor.TryParse(colorSettings.InvisibleColor, out var invisibleColor))
                            {
                                var invisibleUnityColor = new UnityColor(invisibleColor.Red, invisibleColor.Green, invisibleColor.Blue, invisibleColor.Alpha);
                                NativeMethods.SetMaterialColor(colorMem, material.Address, material.ColorInvisible, invisibleUnityColor);
                            }

                            LoneLogging.WriteLine($"[CHAMS] Applied configured colors to {materialMode}/{entityType}");
                        }
                        catch (Exception ex)
                        {
                            LoneLogging.WriteLine($"[CHAMS] Failed to apply configured colors to {materialMode}/{entityType}: {ex.Message}");
                        }
                    }
                }

                LoneLogging.WriteLine("[ESP Control] Finished applying all configured material colors");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ESP Control] Error applying configured material colors: {ex.Message}");
            }
        }

        private async Task ApplyAllChamsColorsAsync()
        {
            lock (_chamsColorLock)
            {
                if (_isApplyingChamsColors)
                {
                    LoneLogging.WriteLine("[ESP Control] Chams colors already being applied, skipping duplicate call");
                    return;
                }
                _isApplyingChamsColors = true;
            }

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        LoneLogging.WriteLine("[ESP Control] Applying all chams colors in background...");

                        ApplyAllConfiguredMaterialColors();
                        PlayerChamsManager.ApplyConfiguredColors();
                        GrenadeChamsManager.ApplyConfiguredColors();

                        LoneLogging.WriteLine("[ESP Control] Finished applying all chams colors");
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[ESP Control] Error applying chams colors: {ex.Message}");
                        throw;
                    }
                });
            }
            finally
            {
                lock (_chamsColorLock)
                {
                    _isApplyingChamsColors = false;
                }
            }
        }

        private void InitializeChamsMaterialStatusTimer()
        {
            _chamsMaterialStatusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _chamsMaterialStatusTimer.Tick += ChamsMaterialStatusTimer_Tick;
            _chamsMaterialStatusTimer.Start();

            UpdateChamsMaterialStatus();
        }

        private void ChamsMaterialStatusTimer_Tick(object sender, EventArgs e)
        {
            UpdateChamsMaterialStatus();
        }

        private void UpdateChamsMaterialStatus()
        {
            try
            {
                var status = ChamsManager.GetDetailedStatus();
                UpdateMaterialStatusDisplay(status);
                UpdateMaterialManagementButtons(status);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS STATUS] Error updating status: {ex.Message}");
                ShowFallbackMaterialStatus();
            }
        }

        private void UpdateMaterialStatusDisplay(ChamsMaterialStatus status)
        {
            txtChamsMaterialCount.Text = $"{status.LoadedCount}/{status.ExpectedCount} loaded";

            var (statusText, statusColor) = status.IsComplete
                ? ("Complete", Colors.Green)
                : status.IsPartial
                    ? ("Partial", Colors.Yellow)
                    : ("Failed", Colors.Red);

            txtChamsMaterialStatus.Text = statusText;
            txtChamsMaterialStatus.Foreground = new SolidColorBrush(statusColor);
        }

        private void UpdateMaterialManagementButtons(ChamsMaterialStatus status)
        {
            var advWrites = MemWrites.Config.AdvancedMemWrites;
            var controlEnabled = MemWrites.Enabled && Config.ChamsConfig.Enabled;

            btnRefreshChamsMaterials.IsEnabled = controlEnabled && advWrites && !_isRefreshingChamsMaterials;
            btnClearChamsCache.IsEnabled = controlEnabled && advWrites && !_isRefreshingChamsMaterials;

            var tooltip = status.MissingCombos.Any()
                ? $"Refresh failed materials. Missing: {string.Join(", ", status.MissingCombos.Take(3).Select(x => $"{x.Item1}-{x.Item2}"))}"
                  + (status.MissingCombos.Count > 3 ? $" and {status.MissingCombos.Count - 3} more..." : "")
                : "Force refresh all chams materials. Use if materials failed to load properly.";

            btnRefreshChamsMaterials.ToolTip = tooltip;
        }

        private void ShowFallbackMaterialStatus()
        {
            var materialsCount = ChamsManager.Materials?.Count ?? 0;
            var expectedCount = ChamsManager.ExpectedMaterialCount;

            txtChamsMaterialCount.Text = $"{materialsCount}/{expectedCount} loaded";

            var (statusText, statusColor) = materialsCount switch
            {
                0 => ("Not Loaded", Colors.Red),
                var count when count < expectedCount => ("Partial", Colors.Yellow),
                _ => ("Complete", Colors.Green)
            };

            txtChamsMaterialStatus.Text = statusText;
            txtChamsMaterialStatus.Foreground = new SolidColorBrush(statusColor);
        }

        private async void btnRefreshChamsMaterials_Click(object sender, RoutedEventArgs e)
        {
            if (_isRefreshingChamsMaterials)
                return;

            _isRefreshingChamsMaterials = true;

            try
            {
                Dispatcher.Invoke(() =>
                {
                    btnRefreshChamsMaterials.Content = "Refreshing...";
                    btnRefreshChamsMaterials.IsEnabled = false;
                    btnClearChamsCache.IsEnabled = false;
                    txtChamsMaterialStatus.Text = "Analyzing";
                    txtChamsMaterialStatus.Foreground = new SolidColorBrush(Colors.Orange);
                });

                LoneLogging.WriteLine("[CHAMS REFRESH] Starting smart materials refresh...");

                var success = await Task.Run(async () =>
                {
                    try
                    {
                        var status = ChamsManager.GetDetailedStatus();

                        if (status.IsComplete)
                        {
                            LoneLogging.WriteLine("[CHAMS REFRESH] All materials already loaded");

                            await Dispatcher.InvokeAsync(() =>
                            {
                                NotificationsShared.Info("[CHAMS] All materials already loaded!");
                            });

                            return true;
                        }

                        LoneLogging.WriteLine($"[CHAMS REFRESH] Status: {status.LoadedCount}/{status.ExpectedCount} loaded, {status.WorkingCount} working, {status.FailedCount} failed");

                        if (status.MissingCombos.Any())
                        {
                            var missingList = string.Join(", ", status.MissingCombos.Select(x => $"{x.Item1}-{x.Item2}"));
                            LoneLogging.WriteLine($"[CHAMS REFRESH] Missing materials: {missingList}");
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtChamsMaterialStatus.Text = "Refreshing";
                            txtChamsMaterialStatus.Foreground = new SolidColorBrush(Colors.Blue);
                            NotificationsShared.Info($"[CHAMS] Starting targeted refresh for {status.MissingCombos.Count} missing materials...");
                        });

                        return ChamsManager.SmartRefresh();
                    }
                    catch (Exception ex)
                    {
                        LoneLogging.WriteLine($"[CHAMS REFRESH] SmartRefresh error: {ex.Message}");
                        return false;
                    }
                });

                var finalStatus = await Task.Run(() => ChamsManager.GetDetailedStatus());

                Dispatcher.Invoke(() =>
                {
                    UpdateEntityChamsSettings();
                    ToggleChamsControls();
                });

                if (finalStatus.IsComplete)
                {
                    LoneLogging.WriteLine("[CHAMS REFRESH] All materials successfully loaded!");
                    NotificationsShared.Success("[CHAMS] All materials successfully loaded!");
                }
                else if (finalStatus.LoadedCount > 0)
                {
                    var recovered = Math.Max(0, finalStatus.LoadedCount);
                    LoneLogging.WriteLine($"[CHAMS REFRESH] Partially successful: {recovered} materials loaded");
                    NotificationsShared.Info($"[CHAMS] {finalStatus.LoadedCount}/{finalStatus.ExpectedCount} total loaded.");

                    if (finalStatus.MissingCombos.Any())
                    {
                        var stillMissing = string.Join(", ", finalStatus.MissingCombos.Take(5).Select(x => $"{x.Item1}-{x.Item2}"));
                        LoneLogging.WriteLine($"[CHAMS REFRESH] Still missing: {stillMissing}{(finalStatus.MissingCombos.Count > 5 ? "..." : "")}");
                    }
                }
                else
                {
                    LoneLogging.WriteLine("[CHAMS REFRESH] Refresh failed - no materials recovered");
                    NotificationsShared.Error("[CHAMS] Refresh failed. Check logs for details.");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS REFRESH] Unexpected error: {ex.Message}");
                NotificationsShared.Error($"[CHAMS] Refresh error: {ex.Message}");
            }
            finally
            {
                _isRefreshingChamsMaterials = false;

                Dispatcher.Invoke(() =>
                {
                    btnRefreshChamsMaterials.Content = "🔄 Refresh Materials";
                    btnRefreshChamsMaterials.IsEnabled = true;
                    btnClearChamsCache.IsEnabled = true;
                });

                UpdateChamsMaterialStatus();
            }
        }

        private void btnClearChamsCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear the chams material cache?\n\nThis will force a complete reload of all materials on the next refresh.",
                    "Clear Chams Cache",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var cache = Config.LowLevelCache;
                    cache.ChamsMaterialCache.Clear();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await cache.SaveAsync();
                            LoneLogging.WriteLine("[CHAMS CACHE] Material cache cleared successfully");
                            NotificationsShared.Info("[CHAMS] Material cache cleared. Use 'Refresh Materials' to reload.");
                        }
                        catch (Exception ex)
                        {
                            LoneLogging.WriteLine($"[CHAMS CACHE] Error saving cleared cache: {ex.Message}");
                            NotificationsShared.Warning($"[CHAMS] Cache cleared but save failed: {ex.Message}");
                        }
                    });

                    ChamsManager.Reset();
                    UpdateChamsMaterialStatus();
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[CHAMS CACHE] Error clearing cache: {ex.Message}");
                NotificationsShared.Error($"[CHAMS] Error clearing cache: {ex.Message}");
            }
        }

        private void FeatureInstanceCheck()
        {
            var cfg = Config.MemWrites;
            var memWritesOn = MemWrites.Enabled;
            MemWriteFeature<Chams>.Instance.Enabled = (memWritesOn && cfg.Chams.Enabled);
        }

        private bool GetModeFromCombo(ComboBox combo, out ChamsMode mode)
        {
            mode = ChamsMode.Basic;
            return combo.SelectedItem is ComboBoxItem item &&
                   item.Tag is string tag &&
                   _materialTypeMapping.TryGetValue(tag, out mode);
        }

        public async void LoadImportedChamsSettings()
        {
            try
            {
                LoneLogging.WriteLine("[CHAMS IMPORT] Loading imported chams settings...");
                _isImporting = true;

                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateEntityChamsSettings();
                });

                if (Config.ChamsConfig.Enabled && MemWrites.Enabled)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ApplyAllChamsColorsAsync();
                        }
                        catch (Exception ex)
                        {
                            LoneLogging.WriteLine($"[CHAMS IMPORT] Error applying imported chams colors: {ex.Message}");
                        }
                        finally
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _isImporting = false;
                            });
                        }
                    });
                }
                else
                {
                    _isImporting = false;
                }

                LoneLogging.WriteLine("[CHAMS IMPORT] Imported chams settings loaded successfully");
            }
            catch (Exception ex)
            {
                _isImporting = false;
                LoneLogging.WriteLine($"[CHAMS IMPORT] Error loading imported chams settings: {ex.Message}");
            }
        }

        public void UpdateChamsControls()
        {
            ToggleChamsControls();
            UpdateEntityChamsSettings();
        }
        #endregion

        #region Events
        private void ChamsCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not string tag) return;

            var value = cb.IsChecked == true;
            var chams = Config.ChamsConfig.GetEntitySettings(_selectedEntityType);

            switch (tag)
            {
                case "EnableChams":
                    Config.MemWrites.Chams.Enabled = value;
                    MemWriteFeature<Chams>.Instance.Enabled = value;
                    ToggleChamsControls();
                    break;

                case "EntityEnabled":
                    chams.Enabled = value;
                    ToggleEntityChamsControls();
                    break;

                case "ClothingChamsEnabled":
                    chams.ClothingChamsEnabled = value;
                    TogglePlayerControls(MemWrites.Enabled && Config.ChamsConfig.Enabled && chkEntityEnabled.IsChecked == true);
                    break;

                case "GearChamsEnabled":
                    chams.GearChamsEnabled = value;
                    TogglePlayerControls(MemWrites.Enabled && Config.ChamsConfig.Enabled && chkEntityEnabled.IsChecked == true);
                    break;

                case "DeathMaterialEnabled":
                    chams.DeathMaterialEnabled = value;
                    TogglePlayerControls(MemWrites.Enabled && Config.ChamsConfig.Enabled && chkEntityEnabled.IsChecked == true);
                    break;
            }

            Config.Save();
            LoneLogging.WriteLine($"[CHAMS] {tag} for {_selectedEntityType} changed to {value}");
        }

        private void cboChamsEntityType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetSelectedEntityType();
            UpdateEntityChamsSettings();
        }

        private void cboMaterialType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetModeFromCombo(cboMaterialType, out var mode))
                UpdateChamsMode(mode, "Mode");
        }

        private void cboClothingMaterialType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetModeFromCombo(cboClothingMaterialType, out var mode))
                UpdateChamsMode(mode, "ClothingChamsMode");
        }

        private void cboGearMaterialType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetModeFromCombo(cboGearMaterialType, out var mode))
                UpdateChamsMode(mode, "GearChamsMode");
        }

        private void cboDeathMaterialType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetModeFromCombo(cboDeathMaterialType, out var mode))
                UpdateChamsMode(mode, "DeathMaterialMode");
        }

        private void cboColorMaterialType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetModeFromCombo(cboColorMaterialType, out var mode))
            {
                _selectedColorMaterialType = mode;
                UpdateColorSettings();
            }
        }
        #endregion
        #endregion

        #region Fuser Tab
        #region Functions/Methods
        private void RegisterFuserEvents()
        {
            // General Settings
            btnStartESP.Click += btnStartESP_Click;
            nudFPSCap.ValueChanged += nudFPSCap_ValueChanged;
            chkAutoFullscreen.Checked += FuserCheckbox_Checked;
            chkAutoFullscreen.Unchecked += FuserCheckbox_Checked;
            sldrFuserFontScale.ValueChanged += FuserSlider_ValueChanged;
            sldrFuserLineScale.ValueChanged += FuserSlider_ValueChanged;
            ccbESPOptions.SelectionChanged += espOptionsCheckComboBox_SelectionChanged;

            // Crosshair Settings
            chkCrosshairEnabled.Checked += FuserCheckbox_Checked;
            chkCrosshairEnabled.Unchecked += FuserCheckbox_Checked;
            cboCrosshairType.SelectionChanged += FuserComboBox_SelectionChanged;
            sldrFuserCrosshairScale.ValueChanged += FuserSlider_ValueChanged;

            // Player Information
            cboFuserPlayerType.SelectionChanged += cboFuserPlayerType_SelectionChanged;
            chkHighAlert.Checked += FuserCheckbox_Checked;
            chkHighAlert.Unchecked += FuserCheckbox_Checked;
            chkImportantIndicators.Checked += FuserCheckbox_Checked;
            chkImportantIndicators.Unchecked += FuserCheckbox_Checked;
            sldrPlayerTypeRenderDistance.ValueChanged += FuserSlider_ValueChanged;
            cboPlayerRenderMode.SelectionChanged += FuserComboBox_SelectionChanged;
            ccbFuserPlayerInformation.SelectionChanged += espPlayerInfoCheckComboBox_SelectionChanged;

            // Entity Information
            cboFuserEntityType.SelectionChanged += cboFuserEntityType_SelectionChanged;
            chkShowGrenadeRadius.Checked += FuserCheckbox_Checked;
            chkShowGrenadeRadius.Unchecked += FuserCheckbox_Checked;
            cboEntityRenderMode.SelectionChanged += FuserComboBox_SelectionChanged;
            sldrEntityTypeRenderDistance.ValueChanged += FuserSlider_ValueChanged;
            ccbFuserEntityInformation.SelectionChanged += espEntityInfoCheckComboBox_SelectionChanged;
        }

        private void LoadFuserSettings()
        {
            var cfg = Config.ESP;

            // General
            chkAutoFullscreen.IsChecked = cfg.AutoFullscreen;
            nudFPSCap.Value = cfg.FPSCap;
            sldrFuserFontScale.Value = cfg.FontScale;
            sldrFuserLineScale.Value = cfg.LineScale;
            InitializeFuserOptions();

            // Crosshair
            var crosshairEnabled = cfg.Crosshair.Enabled;
            chkCrosshairEnabled.IsChecked = crosshairEnabled;
            cboCrosshairType.SelectedIndex = cfg.Crosshair.Type;
            sldrFuserCrosshairScale.Value = cfg.Crosshair.Scale;
            sldrFuserCrosshairScale.IsEnabled = crosshairEnabled;

            // Player Type Settings
            InitializeFuserPlayerTypeSettings();

            // Entity Type Settings
            InitializeFuserEntityTypeSettings();

            if (cfg.AutoFullscreen)
                btnStartESP_Click(null, null);
        }

        private void InitializeFuserPlayerTypeSettings()
        {
            if (Config.ESP.PlayerTypeESPSettings == null)
                Config.ESP.PlayerTypeESPSettings = new PlayerTypeSettingsESPConfig();

            Config.ESP.PlayerTypeESPSettings.InitializeDefaults();
            Config.Save();
            cboFuserPlayerType.Items.Clear();

            var playerTypeItems = new List<ComboBoxItem>();

            foreach (PlayerType type in Enum.GetValues(typeof(PlayerType)))
            {
                if (type != PlayerType.Default)
                {
                    var displayName = type.GetDescription();
                    var item = new ComboBoxItem
                    {
                        Content = displayName,
                        Tag = type.ToString()
                    };
                    playerTypeItems.Add(item);
                }
            }

            playerTypeItems.Add(new ComboBoxItem { Content = "Aimbot Locked", Tag = "AimbotLocked" });
            playerTypeItems.Add(new ComboBoxItem { Content = "Focused", Tag = "Focused" });
            playerTypeItems.Sort((x, y) => string.Compare(x.Content.ToString(), y.Content.ToString()));

            foreach (var item in playerTypeItems)
            {
                cboFuserPlayerType.Items.Add(item);
            }

            ccbFuserPlayerInformation.Items.Clear();

            foreach (var info in _availableFuserPlayerInformation)
            {
                ccbFuserPlayerInformation.Items.Add(new CheckComboBoxItem { Content = info });
            }

            if (cboFuserPlayerType.Items.Count > 0)
            {
                cboFuserPlayerType.SelectedIndex = 0;
                _currentFuserPlayerType = ((ComboBoxItem)cboFuserPlayerType.SelectedItem).Tag.ToString();
                LoadFuserPlayerTypeSettings(_currentFuserPlayerType);
            }
        }

        private void LoadFuserPlayerTypeSettings(string playerType)
        {
            _isLoadingFuserPlayerSettings = true;

            try
            {
                var settings = Config.ESP.PlayerTypeESPSettings.GetSettings(playerType);

                ccbFuserPlayerInformation.SelectedItems.Clear();

                chkHighAlert.IsChecked = settings.HighAlert;
                chkImportantIndicators.IsChecked = settings.ImportantIndicator;
                sldrPlayerTypeRenderDistance.Value = settings.RenderDistance;

                foreach (CheckComboBoxItem item in ccbFuserPlayerInformation.Items)
                {
                    var info = item.Content.ToString();
                    item.IsSelected = settings.Information.Contains(info);
                }

                foreach (ComboBoxItem item in cboPlayerRenderMode.Items)
                {
                    if ((int)settings.RenderMode == cboPlayerRenderMode.Items.IndexOf(item))
                    {
                        cboPlayerRenderMode.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isLoadingFuserPlayerSettings = false;
            }
        }

        private void SaveFuserPlayerTypeSettings(string playerType)
        {
            if (_isLoadingFuserPlayerSettings)
                return;

            var settings = Config.ESP.PlayerTypeESPSettings.GetSettings(playerType);
            settings.Information.Clear();
            settings.HighAlert = chkHighAlert.IsChecked == true;
            settings.ImportantIndicator = chkImportantIndicators.IsChecked == true;
            settings.RenderDistance = (int)sldrPlayerTypeRenderDistance.Value;

            foreach (CheckComboBoxItem item in ccbFuserPlayerInformation.SelectedItems)
            {
                settings.Information.Add(item.Content.ToString());
            }

            settings.RenderMode = (ESPPlayerRenderMode)cboPlayerRenderMode.SelectedIndex;

            Config.Save();
            LoneLogging.WriteLine($"Saved ESP player type settings for {playerType}");
        }

        private void InitializeFuserEntityTypeSettings()
        {
            if (Config.ESP.EntityTypeESPSettings == null)
                Config.ESP.EntityTypeESPSettings = new EntityTypeSettingsESPConfig();

            Config.ESP.EntityTypeESPSettings.InitializeDefaults();
            Config.Save();
            cboFuserEntityType.Items.Clear();

            var entityTypeItems = new List<ComboBoxItem>
            {
                new ComboBoxItem { Content = "Static Container", Tag = "StaticContainer" },
                new ComboBoxItem { Content = "Refill Container", Tag = "RefillContainer" },
                new ComboBoxItem { Content = "Loot", Tag = "Loot" },
                new ComboBoxItem { Content = "Grenade", Tag = "Grenade" },
            };

            entityTypeItems.Sort((x, y) => string.Compare(x.Content.ToString(), y.Content.ToString()));

            foreach (var item in entityTypeItems)
            {
                cboFuserEntityType.Items.Add(item);
            }

            ccbFuserEntityInformation.Items.Clear();

            foreach (var info in _availableFuserEntityInformation)
            {
                ccbFuserEntityInformation.Items.Add(new CheckComboBoxItem { Content = info });
            }

            if (cboFuserEntityType.Items.Count > 0)
            {
                cboFuserEntityType.SelectedIndex = 0;
                _currentFuserEntityType = ((ComboBoxItem)cboFuserEntityType.SelectedItem).Tag.ToString();
                LoadFuserEntityTypeSettings(_currentFuserEntityType);
            }
        }

        private void LoadFuserEntityTypeSettings(string entityType)
        {
            _isLoadingFuserEntitySettings = true;

            try
            {
                var settings = Config.ESP.EntityTypeESPSettings.GetSettings(entityType);

                sldrEntityTypeRenderDistance.Value = settings.RenderDistance;

                ccbFuserEntityInformation.SelectedItems.Clear();

                foreach (CheckComboBoxItem item in ccbFuserEntityInformation.Items)
                {
                    var info = item.Content.ToString();

                    if (settings.Information.Contains(info))
                        item.IsSelected = true;
                    else
                        item.IsSelected = false;
                }

                switch (entityType)
                {
                    case "Grenade":
                        chkShowGrenadeRadius.IsChecked = settings.ShowRadius;
                        break;
                }

                foreach (ComboBoxItem item in cboEntityRenderMode.Items)
                {
                    if (item.Content.ToString() == settings.RenderMode.ToString())
                    {
                        cboEntityRenderMode.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isLoadingFuserEntitySettings = false;
            }

            grenadeSettings.Visibility = Visibility.Collapsed;

            switch (_currentFuserEntityType)
            {
                case "Grenade":
                    grenadeSettings.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void SaveFuserEntityTypeSettings(string entityType)
        {
            if (_isLoadingFuserEntitySettings)
                return;

            var settings = Config.ESP.EntityTypeESPSettings.GetSettings(entityType);
            settings.RenderDistance = (int)sldrEntityTypeRenderDistance.Value;
            settings.Information.Clear();

            foreach (CheckComboBoxItem item in ccbFuserEntityInformation.SelectedItems)
            {
                settings.Information.Add(item.Content.ToString());
            }

            if (cboEntityRenderMode.SelectedItem is ComboBoxItem selectedItem)
            {
                var renderModeText = selectedItem.Content.ToString();
                if (Enum.TryParse<EntityRenderMode>(renderModeText, out var renderMode))
                {
                    settings.RenderMode = renderMode;
                }
            }

            switch (entityType)
            {
                case "Grenade":
                    settings.ShowRadius = chkShowGrenadeRadius.IsChecked == true;
                    break;
            }

            Config.Save();
            LoneLogging.WriteLine($"Saved ESP entity type settings for {entityType}");
        }

        private void InitializeFuserOptions()
        {
            _isLoadingFuserOptionSettings = true;

            try
            {
                ccbESPOptions.Items.Clear();

                foreach (var option in _availableFuserOptions)
                {
                    ccbESPOptions.Items.Add(new CheckComboBoxItem { Content = option });
                }

                UpdateESPOptionsSelections();
            }
            finally
            {
                _isLoadingFuserOptionSettings = false;
            }
        }

        private void UpdateESPOptionsSelections()
        {
            var cfg = Config.ESP;
            ccbESPOptions.SelectedItems.Clear();

            foreach (CheckComboBoxItem item in ccbESPOptions.Items)
            {
                var content = item.Content.ToString();

                var isSelected = content switch
                {
                    "Fireport Aim" => cfg.ShowFireportAim,
                    "Aimbot FOV" => cfg.ShowAimFOV,
                    "Raid Stats" => cfg.ShowRaidStats,
                    "Aimbot Lock" => cfg.ShowAimLock,
                    "Status Text" => cfg.ShowStatusText,
                    "FPS" => cfg.ShowFPS,
                    "Magazine Info" => cfg.ShowMagazine,
                    _ => false
                };

                item.IsSelected = isSelected;
            }
        }

        /// <summary>
        /// Scales all ESP font sizes based on the current font scale value
        /// </summary>
        private void ScaleESPFonts()
        {
            var fontScale = Config.ESP.FontScale;

            SKPaints.TextUSECESP.TextSize = 12f * fontScale;
            SKPaints.TextBEARESP.TextSize = 12f * fontScale;
            SKPaints.TextAIESP.TextSize = 12f * fontScale;
            SKPaints.TextFriendlyESP.TextSize = 12f * fontScale;
            SKPaints.TextSpecialESP.TextSize = 12f * fontScale;
            SKPaints.TextStreamerESP.TextSize = 12f * fontScale;
            SKPaints.TextAimbotLockedESP.TextSize = 12f * fontScale;
            SKPaints.TextFocusedESP.TextSize = 12f * fontScale;
            SKPaints.TextDefaultLootESP.TextSize = 12f * fontScale;
            SKPaints.TextThrowableLootESP.TextSize = 12f * fontScale;
            SKPaints.TextWeaponLootESP.TextSize = 12f * fontScale;
            SKPaints.TextMedsESP.TextSize = 12f * fontScale;
            SKPaints.TextBackpacksESP.TextSize = 12f * fontScale;
            SKPaints.TextContainerLootESP.TextSize = 11f * fontScale;
            SKPaints.TextRefillContainerESP.TextSize = 11f * fontScale;
            SKPaints.TextMagazineESP.TextSize = 42f * fontScale;
            SKPaints.TextMagazineInfoESP.TextSize = 16f * fontScale;
            SKPaints.TextBasicESP.TextSize = 12f * fontScale;
            SKPaints.TextBasicESPLeftAligned.TextSize = 12f * fontScale;
            SKPaints.TextBasicESPRightAligned.TextSize = 12f * fontScale;
            SKPaints.TextStatusSmallEsp.TextSize = 13f * fontScale;
            SKPaints.TextExplosiveESP.TextSize = 13f * fontScale;
            SKPaints.TextPulsingAsteriskESP.TextSize = 18f * fontScale;
            SKPaints.TextPulsingAsteriskOutlineESP.TextSize = 18f * fontScale;
        }

        /// <summary>
        /// Scales all ESP line stroke widths based on the current line scale value
        /// </summary>
        private void ScaleESPLines()
        {
            var lineScale = Config.ESP.LineScale;

            SKPaints.PaintUSECESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintBEARESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintAIESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintFriendlyESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintSpecialESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintStreamerESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintAimbotLockedESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintFocusedESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintBasicESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintHighAlertAimlineESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintHighAlertBorderESP.StrokeWidth = 3f * lineScale;
            SKPaints.PaintDefaultLootESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintThrowableLootESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintWeaponLootESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintMedsESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintBackpacksESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintContainerLootESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintRefillContainerESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintExplosiveESP.StrokeWidth = 1f * lineScale;
            SKPaints.PaintExplosiveRadiusESP.StrokeWidth = 1.5f * lineScale;
            SKPaints.PaintESPHealthBar.StrokeWidth = 1f * lineScale;
            SKPaints.PaintESPHealthBarBg.StrokeWidth = 1f * lineScale;
            SKPaints.PaintESPHealthBarBorder.StrokeWidth = 1f * lineScale;
        }

        /// <summary>
        /// Scales all ESP line stroke widths based on the current line scale value
        /// </summary>
        private void ScaleESPCrosshair()
        {
            var scale = Config.ESP.Crosshair.Scale;

            SKPaints.PaintCrosshairESP.StrokeWidth = 1.75f * scale;
            SKPaints.PaintCrosshairESPDot.StrokeWidth = 1.75f * scale;
        }

        private void SavePlayerTypeSettings()
        {
            if (!string.IsNullOrEmpty(_currentFuserPlayerType) && !_isLoadingFuserPlayerSettings)
                SaveFuserPlayerTypeSettings(_currentFuserPlayerType);
        }

        private void SaveEntityTypeSettings()
        {
            if (!string.IsNullOrEmpty(_currentFuserEntityType) && !_isLoadingFuserEntitySettings)
                SaveFuserEntityTypeSettings(_currentFuserEntityType);
        }

        private bool IsOptionSelected(string option)
        {
            foreach (CheckComboBoxItem item in ccbESPOptions.SelectedItems)
            {
                if (item.Content.ToString() == option)
                    return true;
            }

            return false;
        }
        #endregion

        #region Events
        private void FuserCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tag)
            {
                var value = cb.IsChecked == true;
                LoneLogging.WriteLine($"[Checkbox] {cb.Name} changed to {value}");
                switch (tag)
                {
                    case "AutoFullscreen":
                        Config.ESP.AutoFullscreen = value;
                        break;
                    case "CrosshairEnabled":
                        Config.ESP.Crosshair.Enabled = value;
                        sldrFuserCrosshairScale.IsEnabled = value;
                        break;
                    case "FuserMagazine":
                        Config.ESP.ShowMagazine = value;
                        break;
                    case "FuserFireportAim":
                        Config.ESP.ShowFireportAim = value;
                        break;
                    case "FuserAimbotFOV":
                        Config.ESP.ShowAimFOV = value;
                        break;
                    case "FuserRaidStats":
                        Config.ESP.ShowRaidStats = value;
                        break;
                    case "FuserAimbotLock":
                        Config.ESP.ShowAimLock = value;
                        break;
                    case "FuserStatusText":
                        Config.ESP.ShowStatusText = value;
                        break;
                    case "FuserFPS":
                        Config.ESP.ShowFPS = value;
                        break;
                    case "HighAlertIndicator":
                    case "ImportantIndicators":
                        SavePlayerTypeSettings(); break;
                    case "ShowGrenadeRadius":
                        SaveEntityTypeSettings();
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("Saved Config");
            }
        }

        private void FuserSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is TextValueSlider slider && slider.Tag is string tag)
            {
                var intValue = (int)e.NewValue;
                var floatValue = (float)e.NewValue;
                switch (tag)
                {
                    case "PlayerTypeRenderDistance": SavePlayerTypeSettings(); break;
                    case "EntityTypeRenderDistance": SaveEntityTypeSettings(); break;
                    case "FuserFontScale":
                        Config.ESP.FontScale = floatValue;
                        ScaleESPFonts();
                        break;
                    case "FuserLineScale":
                        Config.ESP.LineScale = floatValue;
                        ScaleESPLines();
                        break;
                    case "FuserCrosshairScale":
                        Config.ESP.Crosshair.Scale = floatValue;
                        ScaleESPCrosshair();
                        break;
                }

                Config.Save();
            }
        }

        private void FuserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cbo && cbo.Tag is string tag)
            {
                switch (tag)
                {
                    case "PlayerRenderMode":
                        SavePlayerTypeSettings();
                        break;
                    case "EntityRenderMode":
                        SaveEntityTypeSettings();
                        break;
                    case "CrosshairType":
                        Config.ESP.Crosshair.Type = cbo.SelectedIndex;
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("[ComboBox] Selection changed and config saved.");
            }
        }

        private void nudFPSCap_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (e.Info is double value)
            {
                var fpsValue = (int)value;
                Config.ESP.FPSCap = fpsValue;
                Config.Save();

                ESPForm.Window?.UpdateRenderTimerInterval(fpsValue);

                LoneLogging.WriteLine($"[FPS Cap] Changed to {fpsValue}");
            }
        }

        public void btnStartESP_Click(object sender, RoutedEventArgs e)
        {
            btnStartESP.Content = "Running...";
            btnStartESP.IsEnabled = false;

            var t = new Thread(() =>
            {
                try
                {
                    ESPForm.ShowESP = true;
                    System.Windows.Forms.Application.Run(new ESPForm());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"ESP Critical Runtime Error!\n{ex.Message}\n\n{ex.StackTrace}");
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnStartESP.Content = "Start ESP";
                        btnStartESP.IsEnabled = true;
                    });
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void cboFuserPlayerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboFuserPlayerType.SelectedItem is ComboBoxItem item)
            {
                SavePlayerTypeSettings();

                _currentFuserPlayerType = item.Tag.ToString();
                LoadFuserPlayerTypeSettings(_currentFuserPlayerType);
            }
        }

        private void cboFuserEntityType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboFuserEntityType.SelectedItem is ComboBoxItem item)
            {
                SaveEntityTypeSettings();

                _currentFuserEntityType = item.Tag.ToString();
                LoadFuserEntityTypeSettings(_currentFuserEntityType);
            }
        }

        private void espPlayerInfoCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SavePlayerTypeSettings();
        }

        private void espEntityInfoCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveEntityTypeSettings();
        }

        private void espOptionsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingFuserOptionSettings)
                return;

            Config.ESP.ShowFireportAim = IsOptionSelected("Fireport Aim");
            Config.ESP.ShowAimFOV = IsOptionSelected("Aimbot FOV");
            Config.ESP.ShowRaidStats = IsOptionSelected("Raid Stats");
            Config.ESP.ShowAimLock = IsOptionSelected("Aimbot Lock");
            Config.ESP.ShowStatusText = IsOptionSelected("Status Text");
            Config.ESP.ShowFPS = IsOptionSelected("FPS");

            Config.Save();
            LoneLogging.WriteLine("Saved ESP option settings");
        }
        #endregion
        #endregion
    }
}