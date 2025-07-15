using arena_dma_radar.Arena.ArenaPlayer.Plugins;
using arena_dma_radar.Arena.Features;
using arena_dma_radar.Arena.Features.MemoryWrites;
using arena_dma_radar.Arena.GameWorld;
using arena_dma_radar.Arena.Loot;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.SKWidgetControl;
using eft_dma_shared.Common.ESP;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Config;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Misc.Data.Arena;
using eft_dma_shared.Common.UI.Controls;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel.PhysX;
using HandyControl.Controls;
using HandyControl.Data;
using HandyControl.Themes;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static arena_dma_radar.Arena.ArenaPlayer.Player;
using static arena_dma_radar.Arena.Features.MemoryWrites.Aimbot;
using static SDK.Offsets;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using InputManager = eft_dma_shared.Common.Misc.InputManager;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = eft_dma_shared.Common.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace arena_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for GeneralSettingsControl.xaml
    /// </summary>
    public partial class GeneralSettingsControl : UserControl
    {
        #region Fields and Properties
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        public ObservableCollection<QuestListItem> QuestItems { get; } = new();

        public ObservableCollection<HotkeyActionModel> AvailableHotkeyActions { get; } = new();
        private readonly Dictionary<string, List<int>> _actionKeyMappings = new();
        private readonly Dictionary<int, string> _actionIdToKeyMap = new();
        private ObservableCollection<HotkeyDisplayModel> _hotkeyList = new();
        private readonly Dictionary<string, bool> _toggleStates = new();
        private readonly Dictionary<string, DateTime> _lastExecutionTime = new();
        private const int HOTKEY_COOLDOWN_MS = 50; // Prevent spam
        private bool _keyInputBoxIsCapturing = false;

        private const int INTERVAL = 100; // 0.1 second
        private const int HK_ZoomAmt = 2; // amt to zoom

        private PopupWindow _openColorPicker;
        private Dictionary<string, SolidColorBrush> _brushFields = new Dictionary<string, SolidColorBrush>();

        private static Config Config => Program.Config;

        private string _currentPlayerType;
        private string _currentEntityType;
        private bool _isLoadingLootFilterSettings = false;
        private bool _isLoadingPlayerSettings = false;
        private bool _isLoadingSettingAndWidgets = false;
        private bool _isLoadingEntitySettings = false;

        private MainWindow mainWindow => MainWindow.Window;

        private readonly string[] _availableInformation = new string[]
        {
            "Bomb",
            "ADS",
            "Ammo Type",
            "Distance",
            "Group",
            "Health",
            "Height",
            "Level",
            "Name",
            "KD",
            "Tag",
            "Weapon"
        };

        private readonly string[] _availableWidgets = new string[]
        {
            "Aimview Widget",
            "Debug Widget",
            "Player Info Widget"
        };

        private readonly string[] _availableGeneralOptions = new string[]
        {
            "Connect Groups",
            "Mask Names",
            "Death Markers"
        };

        private readonly string[] _availableEntityInformation = new string[]
        {
            "Name",
            "Distance"
        };

        private readonly string[] _availableLootFilterOptions = new string[]
        {
            "Throwables",
            "Weapons",
            "Meds",
            "Backpacks"
        };
        #endregion

        public GeneralSettingsControl()
        {
            InitializeComponent();
            TooltipManager.AssignGeneralSettingsTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("GeneralSettings");
                ExpanderManager.Instance.RegisterExpanders(this, "GeneralSettings",
                    expGeneralOptions,
                    expPlayerInformation,
                    expEntityInformation,
                    expMonitorSettings,
                    expPlayerColors,
                    expOtherColors,
                    expHUDColors,
                    expInterfaceColors,
                    expApplicationHotkeys);

                try
                {
                    await PanelCoordinator.Instance.WaitForAllPanelsAsync();

                    this.DataContext = this;

                    InitializeControlEvents();
                    LoadSettings();
                }
                catch (TimeoutException ex)
                {
                    LoneLogging.WriteLine($"[PANELS] {ex.Message}");
                }
            };
        }

        #region General Settings Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterGeneralEvents();
                RegisterColorEvents();
                RegisterHotkeyEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;

            btnMenu.Click += GeneralButton_Click;
            mnuExportConfig.Click += GeneralMenuItem_Click;
            mnuImportConfig.Click += GeneralMenuItem_Click;
        }

        private void LoadSettings()
        {
            Dispatcher.Invoke(() =>
            {
                LoadGeneralSettings();
                LoadColorSettings();
                LoadHotkeySettings();
            });
        }

        private void OpenContextMenu()
        {
            var btn = btnMenu;

            if (btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void ExportConfigToClipboard()
        {
            try
            {
                if (Config == null)
                {
                    NotificationsShared.Warning("[Config] No configuration available to export.");
                    return;
                }

                var configForExport = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(Config));
                configForExport.Cache = null;

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var jsonData = JsonSerializer.Serialize(configForExport, options);
                Clipboard.SetText(jsonData);

                NotificationsShared.Success("[Config] Configuration exported to clipboard successfully! (Cache & Web Radar settings excluded)");
                LoneLogging.WriteLine("[Config] Configuration exported to clipboard (excluding Cache & WebRadar)");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Config] Export error: {ex}");
                NotificationsShared.Error($"[Config] Export error: {ex.Message}");
            }
        }

        private async void ImportConfigFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    NotificationsShared.Warning("[Config] Clipboard does not contain text data.");
                    return;
                }

                var clipboardText = Clipboard.GetText();

                var warningResult = MessageBox.Show(
                        "WARNING: Importing a configuration will replace current settings including:\n\n" +
                        "• General settings & UI preferences\n" +
                        "• Player/Entity display settings\n" +
                        "• Color configurations\n" +
                        "• Hotkey assignments\n" +
                        "• ESP configurations\n" +
                        "• Panel and toolbar positions\n" +
                        "• Memory writing settings\n" +
                        "• Death marker settings\n\n" +
                        "NOTE: Cache & Web Radar data will not be preserved.\n\n" +
                        "This action cannot be undone. Continue?",
                        "Import Configuration Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (warningResult != MessageBoxResult.Yes)
                    return;

                var importButton = this.FindName("mnuImportConfig") as MenuItem;
                if (importButton != null)
                    importButton.IsEnabled = false;

                try
                {
                    Config importedConfig = null;

                    await Task.Run(() =>
                    {
                        try
                        {
                            var options = new JsonSerializerOptions
                            {
                                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                                IgnoreReadOnlyProperties = true,
                                ReadCommentHandling = JsonCommentHandling.Skip,
                                AllowTrailingCommas = true,
                                PropertyNameCaseInsensitive = true
                            };

                            importedConfig = JsonSerializer.Deserialize<Config>(clipboardText, options);

                            if (importedConfig == null)
                            {
                                throw new InvalidOperationException("Deserialized config is null");
                            }

                            LoneLogging.WriteLine("[Config] Configuration deserialized successfully");
                        }
                        catch (Exception ex)
                        {
                            LoneLogging.WriteLine($"[Config] Failed to process configuration: {ex.Message}");
                            throw new JsonException("Invalid configuration data in clipboard", ex);
                        }
                    });

                    if (importedConfig == null)
                    {
                        NotificationsShared.Error("[Config] Invalid configuration data in clipboard.");
                        return;
                    }

                    NotificationsShared.Info("[Config] Applying imported configuration...");

                    await Task.Run(async () =>
                    {
                        try
                        {
                            LoneLogging.WriteLine("[Config] Starting config import process...");

                            var currentCache = Config.Cache;

                            Config.EnsureComplexObjectsInitialized(importedConfig);

                            importedConfig.Cache = currentCache;

                            if (importedConfig.MemWrites.MemWritesEnabled)
                            {
                                var memoryWritingDecision = MemoryWritingControl.HandleConfigImportMemoryWriting(importedConfig);
                                MemoryWritingControl.MemoryWritingImportHandler.ApplyMemoryWritingDecision(importedConfig, memoryWritingDecision);
                            }

                            Program.UpdateConfig(importedConfig);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                var mainWindow = MainWindow.Window;
                                if (mainWindow != null)
                                {
                                    mainWindow.ValidateAndFixImportedPanelPositions();
                                    mainWindow.ValidateAndFixImportedToolbarPosition();
                                }
                            });

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                LoadGeneralSettings();
                                await Task.Delay(50);

                                UpdateUIScale();
                                await Task.Delay(50);

                                LoadColorSettings();
                                await Task.Delay(50);

                                LoadHotkeySettings();
                                await Task.Delay(50);
                            });

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                var mainWindow = MainWindow.Window;
                                if (mainWindow != null)
                                {
                                    if (mainWindow.MemoryWritingControl != null)
                                    {
                                        MemWrites.Enabled = Config.MemWrites.MemWritesEnabled;
                                        mainWindow.MemoryWritingControl.LoadSettings();
                                        await Task.Delay(50);

                                        mainWindow.MemoryWritingControl.FeatureInstanceCheck();
                                        await Task.Delay(50);
                                    }

                                    if (mainWindow.ESPControl != null)
                                    {
                                        mainWindow.ESPControl.LoadSettings();
                                        await Task.Delay(50);

                                        mainWindow.ESPControl.LoadImportedChamsSettings();
                                        await Task.Delay(50);
                                    }

                                    mainWindow.RestorePanelPositions();
                                    mainWindow.RestoreToolbarPosition();

                                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                                    timer.Tick += (s, args) =>
                                    {
                                        timer.Stop();
                                        mainWindow.EnsureAllPanelsInBounds();
                                        if (mainWindow.customToolbar != null)
                                            mainWindow.EnsurePanelInBounds(mainWindow.customToolbar, mainWindow.mainContentGrid);

                                        LoneLogging.WriteLine("[Config] Panel and toolbar positions applied and validated");
                                    };
                                    timer.Start();
                                }
                            });

                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateFeatureInstances();
                            });

                            Config.Save();

                            LoneLogging.WriteLine("[Config] Configuration imported successfully");
                        }
                        catch (Exception ex)
                        {
                            LoneLogging.WriteLine($"[Config] Import error during config application: {ex}");
                            throw;
                        }
                    });

                    NotificationsShared.Success("Configuration imported successfully!");
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"[Config] Import error: {ex}");
                    NotificationsShared.Error($"[Config] Import error: {ex.Message}");
                }
                finally
                {
                    if (importButton != null)
                        importButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Config] Import error: {ex}");
                NotificationsShared.Error($"[Config] Import error: {ex.Message}");
            }
        }

        private void UpdateFeatureInstances()
        {
            try
            {
                var mainWindow = MainWindow.Window;
                MemWrites.Enabled = Config.MemWrites.MemWritesEnabled;

                if (mainWindow?.MemoryWritingControl != null)
                    mainWindow.MemoryWritingControl.FeatureInstanceCheck();

                if (mainWindow?.ESPControl != null)
                    mainWindow.ESPControl.UpdateChamsControls();

                LoneLogging.WriteLine("[Config] Feature instances updated successfully");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[Config] Error updating feature instances: {ex}");
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

        private void GeneralButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "ContextMenu":
                        OpenContextMenu();
                        break;
                }
            }
        }

        private void GeneralMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu && mnu.Tag is string tag)
            {
                switch (tag)
                {
                    case "ExportConfig":
                        ExportConfigToClipboard();
                        break;
                    case "ImportConfig":
                        ImportConfigFromClipboard();
                        break;
                }
            }
        }
        #endregion
        #endregion

        #region General Tab
        #region Functions/Methods
        private void RegisterGeneralEvents()
        {
            // General Options
            chkMapSetup.Checked += GeneralCheckbox_Checked;
            chkMapSetup.Unchecked += GeneralCheckbox_Checked;
            ccbWidgets.SelectionChanged += widgetsCheckComboBox_SelectionChanged;
            ccbGeneralOptions.SelectionChanged += generalOptionsCheckComboBox_SelectionChanged;

            nudFPSLimit.ValueChanged += GeneralNUD_ValueChanged;
            sldrUIScale.ValueChanged += GeneralSlider_ValueChanged;

            // Player Information
            cboPlayerType.SelectionChanged += cboPlayerType_SelectionChanged;
            chkHeightIndicator.Checked += GeneralCheckbox_Checked;
            chkHeightIndicator.Unchecked += GeneralCheckbox_Checked;
            chkImportantIndicator.Checked += GeneralCheckbox_Checked;
            chkImportantIndicator.Unchecked += GeneralCheckbox_Checked;
            chkHighAlert.Checked += GeneralCheckbox_Checked;
            chkHighAlert.Unchecked += GeneralCheckbox_Checked;
            sldrPlayerTypeRenderDistance.ValueChanged += GeneralSlider_ValueChanged;
            sldrPlayerTypeAimlineLength.ValueChanged += GeneralSlider_ValueChanged;
            ccbInformation.SelectionChanged += playerInfoCheckComboBox_SelectionChanged;
            sldrMinimumKD.ValueChanged += GeneralSlider_ValueChanged;

            // Entity Information
            cboEntityType.SelectionChanged += cboEntityType_SelectionChanged;
            sldrEntityTypeRenderDistance.ValueChanged += GeneralSlider_ValueChanged;
            ccbEntityInformation.SelectionChanged += entityInfoCheckComboBox_SelectionChanged;
            chkExplosiveRadius.Checked += GeneralCheckbox_Checked;
            chkExplosiveRadius.Unchecked += GeneralCheckbox_Checked;
            ccbLootFilters.SelectionChanged += ccbLootFilters_SelectionChanged;

            // Monitor
            cboMonitor.SelectionChanged += GeneralComboBox_SelectionChanged;
            btnRefreshMonitors.Click += btnRefreshMonitors_Click;
            txtGameWidth.TextChanged += GeneralTextbox_TextChanged;
            txtGameHeight.TextChanged += GeneralTextbox_TextChanged;
        }

        private void LoadGeneralSettings()
        {
            // General Options
            LoadGeneralOptions();

            nudFPSLimit.Value = Config.RadarTargetFPS;
            sldrUIScale.Value = Config.UIScale;

            // Monitor
            txtGameHeight.Text = Config.MonitorHeight.ToString();
            txtGameWidth.Text = Config.MonitorWidth.ToString();
            CameraManagerBase.UpdateViewportRes();

            UpdateUIScale();

            InitializePlayerTypeSettings();
            InitializeEntityTypeSettings();
        }

        private void UpdateLootFilterSelections()
        {
            if (_isLoadingLootFilterSettings)
                return;

            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Throwables"] = Config.ShowThrowables,
                ["Weapons"] = Config.ShowWeapons,
                ["Meds"] = Config.ShowMeds,
                ["Backpacks"] = Config.ShowBackpacks,
            };

            foreach (CheckComboBoxItem item in ccbLootFilters.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    if (item.IsSelected != shouldBeSelected)
                        item.IsSelected = shouldBeSelected;
            }
        }

        private void InitializePlayerTypeSettings()
        {
            if (Config.PlayerTypeSettings == null)
                Config.PlayerTypeSettings = new PlayerTypeSettingsConfig();

            Config.PlayerTypeSettings.InitializeDefaults();
            Config.Save();
            cboPlayerType.Items.Clear();

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
            playerTypeItems.Add(new ComboBoxItem { Content = "LocalPlayer", Tag = "LocalPlayer" });
            playerTypeItems.Sort((x, y) => string.Compare(x.Content.ToString(), y.Content.ToString()));

            foreach (var item in playerTypeItems)
            {
                cboPlayerType.Items.Add(item);
            }

            ccbInformation.Items.Clear();

            foreach (var info in _availableInformation)
            {
                ccbInformation.Items.Add(new CheckComboBoxItem { Content = info });
            }

            if (cboPlayerType.Items.Count > 0)
            {
                cboPlayerType.SelectedIndex = 0;
                _currentPlayerType = ((ComboBoxItem)cboPlayerType.SelectedItem).Tag.ToString();
                LoadPlayerTypeSettings(_currentPlayerType);
            }
        }

        private void InitializeEntityTypeSettings()
        {
            if (Config.EntityTypeSettings == null)
                Config.EntityTypeSettings = new EntityTypeSettingsConfig();

            Config.EntityTypeSettings.InitializeDefaults();
            Config.Save();
            cboEntityType.Items.Clear();

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
                cboEntityType.Items.Add(item);
            }

            ccbEntityInformation.Items.Clear();

            foreach (var info in _availableEntityInformation)
            {
                ccbEntityInformation.Items.Add(new CheckComboBoxItem { Content = info });
            }

            if (cboEntityType.Items.Count > 0)
            {
                cboEntityType.SelectedIndex = 0;
                _currentEntityType = ((ComboBoxItem)cboEntityType.SelectedItem).Tag.ToString();
                LoadEntityTypeSettings(_currentEntityType);
            }
        }

        private void LoadGeneralOptions()
        {
            _isLoadingSettingAndWidgets = true;

            try
            {
                ccbWidgets.Items.Clear();
                foreach (var widget in _availableWidgets)
                {
                    ccbWidgets.Items.Add(new CheckComboBoxItem { Content = widget });
                }

                ccbGeneralOptions.Items.Clear();
                foreach (var option in _availableGeneralOptions)
                {
                    ccbGeneralOptions.Items.Add(new CheckComboBoxItem { Content = option });
                }

                ccbLootFilters.Items.Clear();
                foreach (var option in _availableLootFilterOptions)
                {
                    ccbLootFilters.Items.Add(new CheckComboBoxItem { Content = option });
                }
            }
            finally
            {
                _isLoadingSettingAndWidgets = false;
            }

            UpdateWidgetOptionSelections();
            UpdateGeneralOptionSelections();
            UpdateLootFilterSelections();
        }

        private void UpdateWidgetOptionSelections()
        {
            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Aimview Widget"] = Config.AimviewWidgetEnabled,
                ["Debug Widget"] = Config.ShowDebugWidget,
                ["Player Info Widget"] = Config.ShowInfoTab
            };

            foreach (CheckComboBoxItem item in ccbWidgets.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    item.IsSelected = shouldBeSelected;
            }
        }

        private void UpdateGeneralOptionSelections()
        {
            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Connect Groups"] = Config.ConnectGroups,
                ["Mask Names"] = Config.MaskNames,
                ["Death Markers"] = Config.DeathMarkers
            };

            foreach (CheckComboBoxItem item in ccbGeneralOptions.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    item.IsSelected = shouldBeSelected;
            }
        }

        private void UpdateSpecificWidgetOption(string widgetName, bool isSelected)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbWidgets.Items)
            {
                if (item.Content.ToString() == widgetName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine($"Updated widget option: {widgetName} = {isSelected}");
        }

        private void UpdateSpecificGeneralOption(string optionName, bool isSelected)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbGeneralOptions.Items)
            {
                if (item.Content.ToString() == optionName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine($"Updated general option: {optionName} = {isSelected}");
        }

        public void UpdateSpecificLootFilterOption(string optionName, bool isSelected)
        {
            if (_isLoadingLootFilterSettings)
                return;

            foreach (CheckComboBoxItem item in ccbLootFilters.Items)
            {
                if (item.Content.ToString() == optionName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine($"Updated loot filter option: {optionName} = {isSelected}");
        }

        private void LoadPlayerTypeSettings(string playerType)
        {
            _isLoadingPlayerSettings = true;
            try
            {
                var settings = Config.PlayerTypeSettings.GetSettings(playerType);

                chkHeightIndicator.IsChecked = settings.HeightIndicator;
                chkImportantIndicator.IsChecked = settings.ImportantIndicator;
                chkHighAlert.IsChecked = settings.HighAlert;
                sldrPlayerTypeRenderDistance.Value = settings.RenderDistance;
                sldrPlayerTypeAimlineLength.Value = settings.AimlineLength;
                sldrMinimumKD.Value = settings.MinKD;

                ccbInformation.SelectedItems.Clear();

                foreach (CheckComboBoxItem item in ccbInformation.Items)
                {
                    var info = item.Content.ToString();

                    if (settings.Information.Contains(info))
                        item.IsSelected = true;
                    else
                        item.IsSelected = false;
                }
            }
            finally
            {
                _isLoadingPlayerSettings = false;
            }

            UpdatePlayerInformationControlsVisibility();
        }

        private void UpdatePlayerInformationControlsVisibility()
        {
            if (_isLoadingPlayerSettings)
                return;

            kdSettings.Visibility = Visibility.Collapsed;

            var showKD = false;
            foreach (CheckComboBoxItem item in ccbInformation.SelectedItems)
            {
                var info = item.Content.ToString();
                if (info == "KD")
                {
                    showKD = true;
                    break;
                }
            }

            if (showKD)
                kdSettings.Visibility = Visibility.Visible;
        }

        private void SavePlayerTypeSettings(string playerType)
        {
            if (_isLoadingPlayerSettings)
                return;

            var settings = Config.PlayerTypeSettings.GetSettings(playerType);
            settings.HeightIndicator = chkHeightIndicator.IsChecked == true;
            settings.ImportantIndicator = chkImportantIndicator.IsChecked == true;
            settings.HighAlert = chkHighAlert.IsChecked == true;
            settings.RenderDistance = (int)sldrPlayerTypeRenderDistance.Value;
            settings.AimlineLength = (int)sldrPlayerTypeAimlineLength.Value;
            settings.MinKD = (float)sldrMinimumKD.Value;
            settings.Information.Clear();

            foreach (CheckComboBoxItem item in ccbInformation.SelectedItems)
            {
                settings.Information.Add(item.Content.ToString());
            }

            Config.Save();
            LoneLogging.WriteLine($"Saved player type settings for {playerType}");
        }

        private void LoadEntityTypeSettings(string entityType)
        {
            _isLoadingEntitySettings = true;
            try
            {
                var settings = Config.EntityTypeSettings.GetSettings(entityType);

                sldrEntityTypeRenderDistance.Value = settings.RenderDistance;

                ccbEntityInformation.SelectedItems.Clear();

                foreach (CheckComboBoxItem item in ccbEntityInformation.Items)
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
                        chkExplosiveRadius.IsChecked = settings.ShowRadius;
                        break;
                }
            }
            finally
            {
                _isLoadingEntitySettings = false;
            }

            grenadeSettings.Visibility = Visibility.Collapsed;

            switch (_currentEntityType)
            {
                case "Grenade":
                    grenadeSettings.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void SaveEntityTypeSettings(string entityType)
        {
            if (_isLoadingEntitySettings)
                return;

            var settings = Config.EntityTypeSettings.GetSettings(entityType);
            settings.RenderDistance = (int)sldrEntityTypeRenderDistance.Value;
            settings.Information.Clear();

            foreach (CheckComboBoxItem item in ccbEntityInformation.SelectedItems)
            {
                settings.Information.Add(item.Content.ToString());
            }

            switch (entityType)
            {
                case "Grenade":
                    settings.ShowRadius = chkExplosiveRadius.IsChecked == true;
                    break;
            }

            Config.Save();
            LoneLogging.WriteLine($"Saved entity type settings for {entityType}");
        }

        private void ToggleMapSetup()
        {
            var cbo = chkMapSetup;
            var value = cbo.IsChecked == true;
            var panel = MainWindow.Window.MapSetupPanel;
            var config = LoneMapManager.Map.Config;
            var mapControl = MainWindow.Window.MapSetupControl;

            if (value && Memory.InRaid && Memory.LocalPlayer != null)
                mapControl.UpdateMapConfiguration(config.X, config.Y, config.Scale);

            panel.Visibility = (panel.Visibility != Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateUIScale()
        {
            var newScale = (float)sldrUIScale.Value;
            var mainWindow = MainWindow.Window;
            mainWindow?.AimView?.SetScaleFactor(newScale);
            mainWindow?.PlayerInfo?.SetScaleFactor(newScale);
            mainWindow?.DebugInfo?.SetScaleFactor(newScale);

            #region UpdatePaints

            // Outlines
            SKPaints.TextOutline.TextSize = 12f * newScale;
            SKPaints.TextOutline.StrokeWidth = 2f * newScale;
            // Shape Outline is computed before usage due to different stroke widths

            SKPaints.PaintConnectorGroup.StrokeWidth = 2.25f * newScale;
            SKPaints.PaintMouseoverGroup.StrokeWidth = 3 * newScale;
            SKPaints.TextMouseoverGroup.TextSize = 12 * newScale;
            SKPaints.PaintLocalPlayer.StrokeWidth = 3 * newScale;
            SKPaints.TextLocalPlayer.TextSize = 12 * newScale;
            SKPaints.PaintTeammate.StrokeWidth = 3 * newScale;
            SKPaints.TextTeammate.TextSize = 12 * newScale;
            SKPaints.PaintUSEC.StrokeWidth = 3 * newScale;
            SKPaints.TextUSEC.TextSize = 12 * newScale;
            SKPaints.PaintBEAR.StrokeWidth = 3 * newScale;
            SKPaints.TextBEAR.TextSize = 12 * newScale;
            SKPaints.PaintSpecial.StrokeWidth = 3 * newScale;
            SKPaints.TextSpecial.TextSize = 12 * newScale;
            SKPaints.PaintStreamer.StrokeWidth = 3 * newScale;
            SKPaints.TextStreamer.TextSize = 12 * newScale;
            SKPaints.PaintAimbotLocked.StrokeWidth = 3 * newScale;
            SKPaints.TextAimbotLocked.TextSize = 12 * newScale;
            SKPaints.PaintAI.StrokeWidth = 3 * newScale;
            SKPaints.TextAI.TextSize = 12 * newScale;
            SKPaints.PaintFocused.StrokeWidth = 3 * newScale;
            SKPaints.TextFocused.TextSize = 12 * newScale;
            SKPaints.TextMouseover.TextSize = 12 * newScale;
            SKPaints.PaintDeathMarker.StrokeWidth = 3 * newScale;
            SKPaints.PaintDefaultLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintThrowableLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintWeaponLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintMeds.StrokeWidth = 3 * newScale;
            SKPaints.PaintBackpacks.StrokeWidth = 3 * newScale;
            SKPaints.PaintContainerLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintRefillContainer.StrokeWidth = 3 * newScale;
            SKPaints.TextContainer.TextSize = 12 * newScale;
            SKPaints.TextRefillContainer.TextSize = 12 * newScale;
            SKPaints.TextDefaultLoot.TextSize = 12 * newScale;
            SKPaints.TextThrowableLoot.TextSize = 12 * newScale;
            SKPaints.TextWeaponLoot.TextSize = 12 * newScale;
            SKPaints.TextMeds.TextSize = 12 * newScale;
            SKPaints.TextBackpacks.TextSize = 12 * newScale;
            SKPaints.PaintTransparentBacker.StrokeWidth = 1 * newScale;
            SKPaints.TextRadarStatus.TextSize = 48 * newScale;
            SKPaints.TextStatusSmall.TextSize = 13 * newScale;
            SKPaints.PaintExplosives.StrokeWidth = 3 * newScale;
            SKPaints.PaintExplosivesDanger.StrokeWidth = 3 * newScale;
            SKPaints.TextExplosives.TextSize = 12 * newScale;
            SKPaints.TextExplosivesDanger.TextSize = 12 * newScale;
            SKPaints.TextPulsingAsterisk.TextSize = 24 * newScale;
            SKPaints.TextPulsingAsteriskOutline.TextSize = 24 * newScale;
            #endregion
        }

        private void InitMonitors()
        {
            LoneLogging.WriteLine("[InitMonitors] Starting monitor initialization...");
            if (!Memory.Ready)
            {
                LoneLogging.WriteLine("[ERROR] Memory or Game is null, cannot initialize monitors.");
                return;
            }

            var gameRes = Memory.GetMonitorRes();
            LoneLogging.WriteLine($"[InitMonitors] Game resolution: {gameRes.Width}x{gameRes.Height}");

            var monitors = MonitorHelper.GetAllMonitors();
            LoneLogging.WriteLine($"[InitMonitors] Found {monitors.Count} monitor(s).");

            cboMonitor.Items.Clear();
            var selectedIndex = 0;

            for (int i = 0; i < monitors.Count; i++)
            {
                var mon = monitors[i];
                LoneLogging.WriteLine($"[InitMonitors] Monitor {i + 1}: {mon.Bounds.Width}x{mon.Bounds.Height}");

                var isGame = (int)mon.Bounds.Width == gameRes.Width && (int)mon.Bounds.Height == gameRes.Height;
                if (isGame)
                    LoneLogging.WriteLine($"[InitMonitors] Monitor {i + 1} matches game resolution and will be selected.");

                var label = isGame ? $"Game Monitor ({mon.Bounds.Width}x{mon.Bounds.Height})"
                                   : $"Monitor {i + 1} ({mon.Bounds.Width}x{mon.Bounds.Height})";

                var item = new ComboBoxItem
                {
                    Content = label,
                    Tag = i
                };

                cboMonitor.Items.Add(item);

                if (isGame)
                    selectedIndex = i;
            }

            if (cboMonitor.Items.Count > 0)
            {
                cboMonitor.SelectedIndex = selectedIndex;
                txtGameWidth.Text = monitors[selectedIndex].Bounds.Width.ToString();
                txtGameHeight.Text = monitors[selectedIndex].Bounds.Height.ToString();

                LoneLogging.WriteLine($"[InitMonitors] Selected monitor index: {selectedIndex}");
            }
        }

        private void UpdateMonitorWH()
        {
            try
            {
                if (cboMonitor.SelectedIndex < 0 || cboMonitor.SelectedItem == null)
                    return;

                var monitors = MonitorHelper.GetAllMonitors();

                if (monitors == null || monitors.Count == 0)
                {
                    LoneLogging.WriteLine("[UpdateMonitorWH] No monitors found");
                    return;
                }

                var selectedIndex = cboMonitor.SelectedIndex;

                if (selectedIndex >= monitors.Count)
                {
                    if (monitors.Count > 0)
                        selectedIndex = 0;
                    else
                        return;
                }

                var selectedMonitor = monitors[selectedIndex];
                var monitorWidth = selectedMonitor.Bounds.Width;
                var monitorHeight = selectedMonitor.Bounds.Height;

                Config.MonitorWidth = (int)monitorWidth;
                Config.MonitorHeight = (int)monitorHeight;

                txtGameWidth.Text = monitorWidth.ToString();
                txtGameHeight.Text = monitorHeight.ToString();

                CameraManagerBase.UpdateViewportRes();
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[ERROR] UpdateMonitorWH: {ex.Message}");
            }
        }

        private void SavePlayerTypeSettings()
        {
            if (!string.IsNullOrEmpty(_currentPlayerType) && !_isLoadingPlayerSettings)
                SavePlayerTypeSettings(_currentPlayerType);
        }

        private void SaveEntityTypeSettings()
        {
            if (!string.IsNullOrEmpty(_currentEntityType) && !_isLoadingEntitySettings)
                SaveEntityTypeSettings(_currentEntityType);
        }

        private bool IsFilterOptionSelected(string option)
        {
            foreach (CheckComboBoxItem item in ccbLootFilters.SelectedItems)
            {
                if (item.Content.ToString() == option)
                    return true;
            }

            return false;
        }
        #endregion

        #region Events
        private void GeneralCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cbo && cbo.Tag is string tag)
            {
                var value = cbo.IsChecked == true;

                LoneLogging.WriteLine($"[Checkbox] {cbo.Name} changed to {value}");

                switch (tag)
                {
                    case "ShowMapSetup":
                        ToggleMapSetup();
                        break;
                    case "PlayerHeightIndicator":
                    case "ImportantIndicator":
                    case "HighAlert":
                        SavePlayerTypeSettings();
                        break;
                    case "ShowExplosiveRadius":
                        SaveEntityTypeSettings();
                        break;
                    case "Throwables":
                        Config.ShowThrowables = value;
                        break;
                    case "Weapons":
                        Config.ShowWeapons = value;
                        break;
                    case "Meds":
                        Config.ShowMeds = value;
                        break;
                    case "Backpacks":
                        Config.ShowBackpacks = value;
                        break;
                    case "RefreshMonitors":
                        InitMonitors();
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("Saved Config");
            }
        }

        private void GeneralSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (sender is TextValueSlider sldr && sldr.Tag is string tag)
            {
                var value = sldr.Value;

                switch (tag)
                {
                    case "UIScale":
                        Config.UIScale = (float)value;
                        UpdateUIScale();
                        break;
                    case "AimlineLength":
                        SavePlayerTypeSettings();
                        break;
                }

                Config.Save();
            }
        }

        private void GeneralTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is HandyControl.Controls.TextBox txt && txt.Tag is string tag)
            {
                var text = txt.Text.Trim();
                int.TryParse(text, out int intValue);

                switch (tag)
                {
                    case "GameWidth":
                        Config.MonitorWidth = intValue;
                        CameraManagerBase.UpdateViewportRes();
                        break;
                    case "GameHeight":
                        Config.MonitorHeight = intValue;
                        CameraManagerBase.UpdateViewportRes();
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("Saved Config");
            }
        }

        private void GeneralComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is HandyControl.Controls.ComboBox cbo && cbo.Tag is string tag)
            {
                switch (tag)
                {
                    case "Monitor":
                        Config.ESP.SelectedScreen = cbo.SelectedIndex;
                        UpdateMonitorWH();
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("[ComboBox] Selection changed and config saved.");
            }
        }

        private void GeneralSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is TextValueSlider slider && slider.Tag is string tag)
            {
                var intValue = (int)e.NewValue;
                var floatValue = (float)e.NewValue;
                switch (tag)
                {
                    case "UIScale":
                        Config.UIScale = floatValue;
                        UpdateUIScale();
                        break;
                    case "PlayerTypeRenderDistance":
                    case "PlayerTypeAimlineLength":
                    case "MinimumKD":
                        SavePlayerTypeSettings(); break;
                    case "EntityTypeRenderDistance":
                        SaveEntityTypeSettings(); break;
                }

                Config.Save();
            }
        }

        private void GeneralNUD_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (sender is HandyControl.Controls.NumericUpDown nud && nud.Tag is string tag && e.Info is double value)
            {
                var intValue = (int)value;

                switch (tag)
                {
                    case "FPSLimit":
                        Config.RadarTargetFPS = intValue;
                        Config.Save();
                        MainWindow.Window.UpdateRenderTimerInterval(intValue);
                        break;
                }

                Config.Save();
            }
        }

        private void btnRefreshMonitors_Click(object sender, RoutedEventArgs e)
        {
            InitMonitors();
        }

        private void widgetsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbWidgets.Items)
            {
                var widgetOption = item.Content.ToString();
                var isSelected = item.IsSelected;

                switch (widgetOption)
                {
                    case "Aimview Widget":
                        Config.AimviewWidgetEnabled = isSelected;
                        break;
                    case "Debug Widget":
                        Config.ShowDebugWidget = isSelected;
                        break;
                    case "Player Info Widget":
                        Config.ShowInfoTab = isSelected;
                        break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine("Saved widget settings");
        }

        private void generalOptionsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbGeneralOptions.Items)
            {
                var option = item.Content.ToString();
                var isSelected = item.IsSelected;

                switch (option)
                {
                    case "Connect Groups":
                        Config.ConnectGroups = isSelected;
                        break;
                    case "Mask Names":
                        Config.MaskNames = isSelected;
                        break;
                    case "Death Markers":
                        Config.DeathMarkers = isSelected;
                        break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine("Saved general options settings");
        }

        private void cboPlayerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboPlayerType.SelectedItem is ComboBoxItem item)
            {
                SavePlayerTypeSettings();

                _currentPlayerType = item.Tag.ToString();
                LoadPlayerTypeSettings(_currentPlayerType);
            }
        }

        private void playerInfoCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SavePlayerTypeSettings();
            UpdatePlayerInformationControlsVisibility();
        }

        private void cboEntityType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboEntityType.SelectedItem is ComboBoxItem item)
            {
                SaveEntityTypeSettings();

                _currentEntityType = item.Tag.ToString();
                LoadEntityTypeSettings(_currentEntityType);
            }
        }

        private void entityInfoCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveEntityTypeSettings();
        }

        private void ccbLootFilters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingLootFilterSettings)
                return;

            Config.ShowThrowables = IsFilterOptionSelected("Throwables");
            Config.ShowWeapons = IsFilterOptionSelected("Weapons");
            Config.ShowMeds = IsFilterOptionSelected("Meds");
            Config.ShowBackpacks = IsFilterOptionSelected("Backpacks");

            Config.Save();
            LoneLogging.WriteLine("Saved loot filter settings");
        }
        #endregion
        #endregion

        #region Colors Tab
        #region Functions/Methods
        private void RegisterColorEvents()
        {
            // Players
            btnLocalPlayerColor.Click += ColorButton_Clicked;
            btnFriendlyColor.Click += ColorButton_Clicked;
            btnUSECColor.Click += ColorButton_Clicked;
            btnBEARColor.Click += ColorButton_Clicked;
            btnStreamerColor.Click += ColorButton_Clicked;
            btnFocusedColor.Click += ColorButton_Clicked;
            btnSpecialColor.Click += ColorButton_Clicked;
            btnScavColor.Click += ColorButton_Clicked;
            btnAimbotTargetColor.Click += ColorButton_Clicked;

            // Loot
            btnContainerLootColor.Click += ColorButton_Clicked;
            btnRefillContainersColor.Click += ColorButton_Clicked;
            btnThrowablesColor.Click += ColorButton_Clicked;
            btnWeaponsColor.Click += ColorButton_Clicked;
            btnMedsColor.Click += ColorButton_Clicked;
            btnBackpacskColor.Click += ColorButton_Clicked;

            // Other
            btnDeathMarkerColor.Click += ColorButton_Clicked;
            btnExplosivesColor.Click += ColorButton_Clicked;
            btnGroupLinesColor.Click += ColorButton_Clicked;

            // Fuser HUD Colors
            btnFPSColor.Click += ColorButton_Clicked;
            btnRaidStatsColor.Click += ColorButton_Clicked;
            btnStatusTextColor.Click += ColorButton_Clicked;
            btnMagazineInfoColor.Click += ColorButton_Clicked;
            btnCrosshairColor.Click += ColorButton_Clicked;
            btnFireportAimColor.Click += ColorButton_Clicked;
            btnAimbotFOVColor.Click += ColorButton_Clicked;
            btnAimbotLockColor.Click += ColorButton_Clicked;
            btnClosestPlayerColor.Click += ColorButton_Clicked;
            btnMiniRadarThemeColor.Click += ColorButton_Clicked;

            // Interface
            btnAccentColor.Click += ColorButton_Clicked;
            btnRegionColor.Click += ColorButton_Clicked;
            btnSecondaryRegionColor.Click += ColorButton_Clicked;
            btnBorderColor.Click += ColorButton_Clicked;
            btnRadarBackgroundColor.Click += ColorButton_Clicked;
            btnFuserBackgroundColor.Click += ColorButton_Clicked;
        }

        private void LoadColorSettings()
        {
            InitializeBrushFields();

            foreach (var colorPair in Config.Colors)
            {
                var colorOption = colorPair.Key;
                var colorValue = colorPair.Value;
                var tagName = colorOption.ToString();

                if (_brushFields.TryGetValue(tagName, out var brush))
                {
                    try
                    {
                        var color = ColorConverter.ConvertFromString(colorValue);
                        if (color != null)
                        {
                            brush.Color = (Color)color;

                            var colorDict = new Dictionary<RadarColorOption, string>
                            {
                                [colorOption] = colorValue
                            };
                            RadarColorOptions.SetColors(colorDict);
                        }
                    }
                    catch (Exception) { }
                }
            }

            foreach (var colorPair in Config.ESP.Colors)
            {
                var colorOption = colorPair.Key;
                var colorValue = colorPair.Value;
                var tagName = colorOption.ToString();

                if (_brushFields.TryGetValue(tagName, out var brush))
                {
                    try
                    {
                        var color = ColorConverter.ConvertFromString(colorValue);
                        if (color != null)
                        {
                            brush.Color = (Color)color;

                            var colorDict = new Dictionary<EspColorOption, string>
                            {
                                [colorOption] = colorValue
                            };
                            EspColorOptions.SetColors(colorDict);
                        }
                    }
                    catch (Exception) { }
                }
            }

            foreach (var colorPair in Config.InterfaceColors)
            {
                var colorOption = colorPair.Key;
                var colorValue = colorPair.Value;
                var tagName = InterfaceColorOptions.GetTagFromOption(colorOption);

                if (!string.IsNullOrEmpty(tagName) && _brushFields.TryGetValue(tagName, out var brush))
                {
                    try
                    {
                        var color = ColorConverter.ConvertFromString(colorValue);
                        if (color != null)
                        {
                            brush.Color = (Color)color;
                            InterfaceColorOptions.UpdateColor(Config, colorOption, (Color)color);
                        }
                    }
                    catch (Exception) { }
                }
            }

            InterfaceColorOptions.LoadColors(Config);
            Config.Save();
        }

        private void InitializeBrushFields()
        {
            // Player colors
            _brushFields["LocalPlayer"] = localPlayerBrush;
            _brushFields["Friendly"] = friendlyBrush;
            _brushFields["USEC"] = USECBrush;
            _brushFields["BEAR"] = BEARBrush;
            _brushFields["Streamer"] = streamerBrush;
            _brushFields["Special"] = specialBrush;
            _brushFields["Focused"] = focusedBrush;
            _brushFields["Scav"] = scavBrush;
            _brushFields["AimbotTarget"] = aimbotTargetBrush;

            // Loot colors
            _brushFields["ContainerLoot"] = containerLootBrush;
            _brushFields["RefillContainers"] = refillContainersBrush;
            _brushFields["ThrowablesFilterLoot"] = throwablesBrush;
            _brushFields["WeaponsFilterLoot"] = weaponsBrush;
            _brushFields["MedsFilterLoot"] = medsBrush;
            _brushFields["BackpacksFilterLoot"] = backpacksBrush;

            // Other colors
            _brushFields["DeathMarker"] = deathMarkerBrush;
            _brushFields["Explosives"] = explosivesBrush;
            _brushFields["GroupLines"] = groupLinesBrush;

            // Fuser HUD colors
            _brushFields["FPS"] = FPSBrush;
            _brushFields["RaidStats"] = raidStatsBrush;
            _brushFields["StatusText"] = statusTextBrush;
            _brushFields["MagazineInfo"] = magazineInfoBrush;
            _brushFields["Crosshair"] = crosshairBrush;
            _brushFields["FireportAim"] = fireportAimBrush;
            _brushFields["AimbotFOV"] = aimbotFOVBrush;
            _brushFields["AimbotLock"] = aimbotLockBrush;
            _brushFields["ClosestPlayer"] = closestPlayerBrush;
            _brushFields["MiniRadarTheme"] = miniRadarThemeBrush;

            // Interface colors
            _brushFields["Interface.Accent"] = accentColor;
            _brushFields["Interface.Region"] = regionColor;
            _brushFields["Interface.SecondaryRegion"] = secondaryRegionColor;
            _brushFields["Interface.Border"] = borderColor;
            _brushFields["Interface.RadarBackground"] = radarBackgroundColor;
            _brushFields["Interface.FuserBackground"] = fuserBackgroundColor;
        }

        private void UpdateColor(string tag, SolidColorBrush brush)
        {
            if (_brushFields.TryGetValue(tag, out var fieldBrush))
                fieldBrush.Color = brush.Color;

            if (tag.StartsWith("Interface."))
            {
                if (InterfaceColorOptions.TryGetColorOption(tag, out var option))
                    InterfaceColorOptions.UpdateColor(Config, option, brush.Color);
                return;
            }

            if (Enum.TryParse<EspColorOption>(tag, out var espOption))
            {
                var hexColor = brush.Color.ToString();

                if (Config.ESP.Colors == null)
                    Config.ESP.Colors = new Dictionary<EspColorOption, string>();

                Config.ESP.Colors[espOption] = hexColor;

                var colorDict = new Dictionary<EspColorOption, string>
                {
                    [espOption] = hexColor
                };

                EspColorOptions.SetColors(colorDict);
                Config.Save();
            }

            if (Enum.TryParse<RadarColorOption>(tag, out var radarOption))
            {
                var hexColor = brush.Color.ToString();

                if (Config.Colors == null)
                    Config.Colors = new Dictionary<RadarColorOption, string>();

                Config.Colors[radarOption] = hexColor;

                var colorDict = new Dictionary<RadarColorOption, string>
                {
                    [radarOption] = hexColor
                };

                RadarColorOptions.SetColors(colorDict);
                Config.Save();
            }
        }

        private SolidColorBrush OpenColorPicker(Button sourceButton, SolidColorBrush currentBrush, string tag)
        {
            _openColorPicker?.Close();

            SolidColorBrush actualBrush = sourceButton.Background as SolidColorBrush;
            if (actualBrush == null)
                actualBrush = currentBrush;

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

            var parentWindow = MainWindow.GetWindow(sourceButton);
            var generalSettingsPanel = parentWindow?.FindName("GeneralSettingsPanel") as FrameworkElement;

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

            picker.SelectedColorChanged += (s, args) =>
            {
                if (picker.SelectedBrush != null)
                    sourceButton.Background = picker.SelectedBrush;
            };

            picker.Confirmed += (s, args) =>
            {
                if (picker.SelectedBrush != null)
                {
                    resultBrush = picker.SelectedBrush as SolidColorBrush;
                    confirmed = true;

                    UpdateColor(tag, resultBrush);
                }
                window.Close();
            };

            picker.Canceled += (s, args) =>
            {
                sourceButton.Background = originalBrush;
                window.Close();
            };

            EventHandler parentLocationChanged = (s, e) => UpdatePickerPosition();
            SizeChangedEventHandler parentSizeChanged = (s, e) => UpdatePickerPosition();
            EventHandler panelLayoutUpdated = (s, e) => UpdatePickerPosition();

            if (parentWindow != null)
            {
                parentWindow.LocationChanged += parentLocationChanged;
                parentWindow.SizeChanged += parentSizeChanged;
            }

            if (generalSettingsPanel != null)
            {
                generalSettingsPanel.LayoutUpdated += panelLayoutUpdated;
            }

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

                if (generalSettingsPanel != null)
                {
                    generalSettingsPanel.LayoutUpdated -= panelLayoutUpdated;
                }
            };

            window.Show(sourceButton, false);

            return confirmed ? resultBrush : null;
        }
        #endregion
        #region Events
        private void ColorButton_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (_brushFields.TryGetValue(tag, out var brush))
                    OpenColorPicker(btn, brush, tag);
            }
        }
        #endregion
        #endregion

        #region Hotkeys Tab
        #region Functions/Methods
        private void LoadHotkeySettings()
        {
            hotkeyListView.ItemsSource = _hotkeyList;

            LoadHotkeyActions();
            LoadHotkeysFromConfig();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RegisterHotkeyHandlers();
            }), DispatcherPriority.Background);
        }

        private void RegisterHotkeyEvents()
        {
            btnAddHotkey.Click += btnAddHotkey_Click;
            btnRemoveHotkey.Click += btnRemoveHotkey_Click;

            cboAction.PreviewKeyDown += cboAction_PreviewKeyDown;
            keyInputBox.CapturingStateChanged += KeyInputBox_CapturingStateChanged;
        }

        private void RegisterHotkeyHandlers()
        {
            if (!InputManager.IsReady)
            {
                LoneLogging.WriteLine("[Hotkeys] InputManager not ready, retrying hotkey registration");

                Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RegisterHotkeyHandlers();
                    }), DispatcherPriority.Background);
                });

                return;
            }

            UnregisterAllHotkeyHandlers();

            var registeredCount = 0;
            foreach (var (actionKey, entry) in GetAllHotkeys())
            {
                if (entry.Enabled && entry.Key != -1)
                {
                    var capturedActionKey = actionKey;
                    var capturedEntry = entry;
                    var actionName = $"{actionKey}_{DateTime.Now.Ticks}";
                    var actionId = InputManager.RegisterKeyAction(entry.Key, actionName, (sender, e) =>
                    {
                        HandleHotkeyEvent(capturedActionKey, capturedEntry, e);
                    });

                    if (actionId != -1)
                    {
                        if (!_actionKeyMappings.ContainsKey(actionKey))
                            _actionKeyMappings[actionKey] = new List<int>();

                        _actionKeyMappings[actionKey].Add(actionId);
                        _actionIdToKeyMap[actionId] = actionKey;
                        registeredCount++;
                    }
                    else
                    {
                        LoneLogging.WriteLine($"[Hotkeys] Failed to register hotkey for {actionKey} (Key: {entry.Key})");
                    }
                }
            }

            LoneLogging.WriteLine($"[Hotkeys] Registered {registeredCount} hotkey handlers");
        }

        private void UnregisterAllHotkeyHandlers()
        {
            foreach (var actionIds in _actionKeyMappings.Values)
            {
                foreach (var actionId in actionIds)
                {
                    InputManager.UnregisterKeyAction(actionId);
                }
            }

            _actionKeyMappings.Clear();
            _actionIdToKeyMap.Clear();
        }

        private void HandleHotkeyEvent(string actionKey, HotkeyEntry entry, InputManager.KeyEventArgs e)
        {
            if (_lastExecutionTime.TryGetValue(actionKey, out var lastTime))
                if ((DateTime.UtcNow - lastTime).TotalMilliseconds < HOTKEY_COOLDOWN_MS)
                    return;

            switch (entry.Mode)
            {
                case HotkeyMode.Toggle:
                    if (e.IsPressed)
                    {
                        var currentState = _toggleStates.GetValueOrDefault(actionKey);
                        var newState = !currentState;
                        _toggleStates[actionKey] = newState;

                        Dispatcher.Invoke(() => ExecuteHotkeyAction(actionKey, newState));
                        _lastExecutionTime[actionKey] = DateTime.UtcNow;
                    }
                    break;

                case HotkeyMode.OnKey:
                    if (IsContinuousAction(actionKey))
                    {
                        if (e.IsPressed)
                        {
                            Dispatcher.Invoke(() => ExecuteHotkeyAction(actionKey, true));
                            _lastExecutionTime[actionKey] = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() => ExecuteHotkeyAction(actionKey, e.IsPressed));
                        _lastExecutionTime[actionKey] = DateTime.UtcNow;
                    }
                    break;
            }
        }

        private string GetKeyDisplayName(int keyCode)
        {
            if (keyCode >= 0x01 && keyCode <= 0x06)
            {
                return keyCode switch
                {
                    0x01 => "Mouse1",
                    0x02 => "Mouse2",
                    0x04 => "Mouse3",
                    0x05 => "Mouse4",
                    0x06 => "Mouse5",
                    _ => $"Mouse{keyCode}"
                };
            }
            else if (keyCode > 0)
            {
                try
                {
                    var key = KeyInterop.KeyFromVirtualKey(keyCode);
                    return GetKeyName(key);
                }
                catch
                {
                    return $"Key{keyCode}";
                }
            }

            return "None";
        }

        private string GetKeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return (key - Key.D0).ToString();

            return key switch
            {
                Key.LeftAlt => "LeftAlt",
                Key.RightAlt => "RightAlt",
                Key.LeftCtrl => "LeftCtrl",
                Key.RightCtrl => "RightCtrl",
                Key.LeftShift => "LeftShift",
                Key.RightShift => "RightShift",
                Key.LWin => "LeftWin",
                Key.RWin => "RightWin",
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Enter => "Enter",
                Key.Back => "Backspace",
                Key.Delete => "Delete",
                Key.Insert => "Insert",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Escape => "Escape",
                Key.CapsLock => "CapsLock",
                Key.NumLock => "NumLock",
                Key.Scroll => "ScrollLock",
                Key.PrintScreen => "PrintScreen",
                Key.Pause => "Pause",
                Key.NumPad0 => "Numpad0",
                Key.NumPad1 => "Numpad1",
                Key.NumPad2 => "Numpad2",
                Key.NumPad3 => "Numpad3",
                Key.NumPad4 => "Numpad4",
                Key.NumPad5 => "Numpad5",
                Key.NumPad6 => "Numpad6",
                Key.NumPad7 => "Numpad7",
                Key.NumPad8 => "Numpad8",
                Key.NumPad9 => "Numpad9",
                Key.Multiply => "Numpad*",
                Key.Add => "Numpad+",
                Key.Subtract => "Numpad-",
                Key.Divide => "Numpad/",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemMinus => "-",
                Key.OemPlus => "=",
                Key.Oem1 => ";",
                Key.Oem2 => "/",
                Key.Oem3 => "`",
                Key.Oem4 => "[",
                Key.Oem5 => "\\",
                Key.Oem6 => "]",
                Key.Oem7 => "'",
                _ => key.ToString()
            };
        }

        private void LoadHotkeyActions()
        {
            LoneLogging.WriteLine("[HotkeyCombo] Loading available hotkey actions...");

            AvailableHotkeyActions.Clear();

            foreach (PropertyInfo prop in typeof(HotkeyConfig).GetProperties())
            {
                var displayName = SplitCamelCase(prop.Name);
                AvailableHotkeyActions.Add(new HotkeyActionModel
                {
                    Key = prop.Name,
                    Name = displayName
                });
            }

            cboAction.ItemsSource = AvailableHotkeyActions;
        }

        private void LoadHotkeysFromConfig()
        {
            if (Config == null)
                return;

            _hotkeyList.Clear();
            _toggleStates.Clear();

            var config = Config.HotKeys;
            var properties = typeof(HotkeyConfig).GetProperties();

            foreach (var prop in properties)
            {
                if (prop.GetValue(config) is HotkeyEntry entry && entry.Enabled && entry.Key != -1)
                {
                    var actionKey = prop.Name;
                    var displayName = actionKey;
                    var model = AvailableHotkeyActions.FirstOrDefault(a => a.Key == actionKey);
                    if (model != null)
                        displayName = model.Name;

                    var keyString = GetKeyDisplayName(entry.Key);

                    _hotkeyList.Add(new HotkeyDisplayModel
                    {
                        Action = displayName,
                        Key = keyString,
                        Type = entry.Mode == HotkeyMode.Toggle ? "Toggle" : "OnKey"
                    });

                    if (entry.Mode == HotkeyMode.Toggle)
                        _toggleStates[actionKey] = false;
                }
            }
        }

        private bool IsContinuousAction(string actionKey)
        {
            return actionKey switch
            {
                nameof(HotkeyConfig.ZoomIn) => true,
                nameof(HotkeyConfig.ZoomOut) => true,
                nameof(HotkeyConfig.MiniRadarZoomIn) => true,
                nameof(HotkeyConfig.MiniRadarZoomOut) => true,
                _ => false
            };
        }

        private string SplitCamelCase(string input)
        {
            return Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
        }

        private IEnumerable<(string ActionKey, HotkeyEntry Entry)> GetAllHotkeys()
        {
            var config = Config.HotKeys;
            var props = typeof(HotkeyConfig).GetProperties();

            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(HotkeyEntry))
                {
                    var entry = prop.GetValue(config) as HotkeyEntry;

                    if (entry != null)
                        yield return (prop.Name, entry);
                }
            }
        }

        private void ExecuteHotkeyAction(string actionKey, bool isActive)
        {
            switch (actionKey)
            {
                #region Loot
                case nameof(HotkeyConfig.ShowThrowables):
                    Config.ShowThrowables = isActive;
                    UpdateSpecificLootFilterOption("Throwables", isActive);
                    break;
                case nameof(HotkeyConfig.ShowWeapons):
                    Config.ShowWeapons = isActive;
                    UpdateSpecificLootFilterOption("Weapons", isActive);
                    break;
                case nameof(HotkeyConfig.ShowMeds):
                    Config.ShowMeds = isActive;
                    UpdateSpecificLootFilterOption("Meds", isActive);
                    break;
                case nameof(HotkeyConfig.ShowBackpacks):
                    Config.ShowBackpacks = isActive;
                    UpdateSpecificLootFilterOption("Backpacks", isActive);
                    break;
                #endregion

                #region Fuser ESP
                case nameof(HotkeyConfig.ToggleFuserESP):
                    ESPForm.ShowESP = isActive;
                    break;
                case nameof(HotkeyConfig.MiniRadarZoomIn):
                    ExecuteContinuousAction(actionKey, () => ESPForm.Window?.ZoomIn(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.MiniRadarZoomOut):
                    ExecuteContinuousAction(actionKey, () => ESPForm.Window?.ZoomOut(HK_ZoomAmt));
                    break;
                #endregion

                #region Memory Writes
                // Global
                case nameof(HotkeyConfig.ToggleRageMode):
                    Config.MemWrites.RageMode = isActive;
                    mainWindow.MemoryWritingControl.chkRageMode.IsChecked = isActive;
                    break;
                // Aimbot
                case nameof(HotkeyConfig.ToggleAimbot):
                    Config.MemWrites.Aimbot.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkEnableAimbot.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.EngageAimbot):
                    Aimbot.Engaged = isActive;
                    break;
                case nameof(HotkeyConfig.ToggleAimbotMode):
                    if (isActive)
                    {
                        Config.MemWrites.Aimbot.TargetingMode = Config.MemWrites.Aimbot.TargetingMode == AimbotTargetingMode.FOV
                            ? AimbotTargetingMode.CQB
                            : AimbotTargetingMode.FOV;
                    }
                    break;
                case nameof(HotkeyConfig.AimbotBone):
                    if (isActive)
                        mainWindow.MemoryWritingControl.ToggleAimbotBone();
                    break;
                case nameof(HotkeyConfig.SafeLock):
                    Config.MemWrites.Aimbot.SilentAim.SafeLock = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Safe Lock", isActive);
                    break;
                case nameof(HotkeyConfig.RandomBone):
                    Config.MemWrites.Aimbot.RandomBone.Enabled = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Random Bone", isActive);
                    break;
                case nameof(HotkeyConfig.AutoBone):
                    Config.MemWrites.Aimbot.SilentAim.AutoBone = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Auto Bone", isActive);
                    break;
                case nameof(HotkeyConfig.HeadshotAI):
                    Config.MemWrites.Aimbot.HeadshotAI = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Headshot AI", isActive);
                    break;
                // Weapons
                case nameof(HotkeyConfig.NoMalfunctions):
                    Config.MemWrites.NoWeaponMalfunctions = isActive;
                    mainWindow.MemoryWritingControl.chkNoWeaponMalfunctions.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.FastWeaponOps):
                    Config.MemWrites.FastWeaponOps = isActive;
                    mainWindow.MemoryWritingControl.chkFastWeaponOps.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.DisableWeaponCollision):
                    Config.MemWrites.DisableWeaponCollision = isActive;
                    mainWindow.MemoryWritingControl.chkDisableWeaponCollision.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.NoRecoil):
                    Config.MemWrites.NoRecoil = isActive;
                    mainWindow.MemoryWritingControl.chkNoRecoil.IsChecked = isActive;
                    break;
                // Movement
                case nameof(HotkeyConfig.WideLean):
                    Config.MemWrites.WideLean.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkWideLean.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.WideLeanUp):
                    SetWideLeanDirection(isActive ? WideLean.EWideLeanDirection.Up : WideLean.EWideLeanDirection.Off);
                    break;
                case nameof(HotkeyConfig.WideLeanLeft):
                    SetWideLeanDirection(isActive ? WideLean.EWideLeanDirection.Left : WideLean.EWideLeanDirection.Off);
                    break;
                case nameof(HotkeyConfig.WideLeanRight):
                    SetWideLeanDirection(isActive ? WideLean.EWideLeanDirection.Right : WideLean.EWideLeanDirection.Off);
                    break;
                case nameof(HotkeyConfig.MoveSpeed):
                    Config.MemWrites.MoveSpeed.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkMoveSpeed.IsChecked = isActive;
                    break;
                // World
                case nameof(HotkeyConfig.DisableShadows):
                    Config.MemWrites.DisableShadows = isActive;
                    mainWindow.MemoryWritingControl.chkDisableShadows.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.DisableGrass):
                    Config.MemWrites.DisableGrass = isActive;
                    mainWindow.MemoryWritingControl.chkDisableGrass.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ClearWeather):
                    Config.MemWrites.ClearWeather = isActive;
                    mainWindow.MemoryWritingControl.chkClearWeather.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.TimeOfDay):
                    Config.MemWrites.TimeOfDay.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkTimeOfDay.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.FullBright):
                    Config.MemWrites.FullBright.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkFullBright.IsChecked = isActive;
                    break;
                // Camera
                case nameof(HotkeyConfig.NoVisor):
                    Config.MemWrites.NoVisor = isActive;
                    mainWindow.MemoryWritingControl.chkNoVisor.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ThermalVision):
                    Config.MemWrites.ThermalVision = isActive;
                    mainWindow.MemoryWritingControl.chkThermalVision.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ThirdPerson):
                    Config.MemWrites.ThirdPerson = isActive;
                    mainWindow.MemoryWritingControl.chkThirdPerson.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.OwlMode):
                    Config.MemWrites.OwlMode = isActive;
                    mainWindow.MemoryWritingControl.chkOwlMode.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.InstantZoom):
                    Config.MemWrites.FOV.InstantZoomActive = isActive;
                    break;
                // Misc
                case nameof(HotkeyConfig.BigHeads):
                    Config.MemWrites.BigHead.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkBigHeads.IsChecked = isActive;
                    break;
                #endregion

                #region General Settings
                case nameof(HotkeyConfig.AimviewWidget):
                    Config.AimviewWidgetEnabled = isActive;
                    UpdateSpecificWidgetOption("Aimview Widget", isActive);
                    break;
                case nameof(HotkeyConfig.DebugWidget):
                    Config.ShowDebugWidget = isActive;
                    UpdateSpecificWidgetOption("Debug Widget", isActive);
                    break;
                case nameof(HotkeyConfig.PlayerInfoWidget):
                    Config.ShowInfoTab = isActive;
                    UpdateSpecificWidgetOption("Player Info Widget", isActive);
                    break;
                case nameof(HotkeyConfig.ConnectGroups):
                    Config.ConnectGroups = isActive;
                    UpdateSpecificGeneralOption("Connect Groups", isActive);
                    break;
                case nameof(HotkeyConfig.MaskNames):
                    Config.MaskNames = isActive;
                    UpdateSpecificGeneralOption("Mask Names", isActive);
                    break;
                case nameof(HotkeyConfig.ZoomIn):
                    ExecuteContinuousAction(actionKey, () => mainWindow.ZoomIn(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.ZoomOut):
                    ExecuteContinuousAction(actionKey, () => mainWindow.ZoomOut(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.BattleMode):
                    Config.BattleMode = isActive;
                    break;
                #endregion

                default:
                    LoneLogging.WriteLine($"[Hotkey] No action defined for: {actionKey}");
                    break;
            }
        }

        private void ExecuteContinuousAction(string actionKey, Action action)
        {
            action();

            if (IsContinuousAction(actionKey))
            {
                var config = Config.HotKeys;
                var prop = typeof(HotkeyConfig).GetProperty(actionKey);
                if (prop?.GetValue(config) is HotkeyEntry entry)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        if (InputManager.IsKeyDown(entry.Key))
                            Dispatcher.Invoke(() => ExecuteContinuousAction(actionKey, action));
                    });
                }
            }
        }

        private void SetWideLeanDirection(WideLean.EWideLeanDirection dir)
        {
            if (!Config.MemWrites.WideLean.Enabled)
            {
                WideLean.Direction = WideLean.EWideLeanDirection.Off;
                return;
            }

            WideLean.Direction = WideLean.Direction == dir ? WideLean.EWideLeanDirection.Off : dir;
        }

        private void RefreshHotkeyDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                hotkeyListView.Items.Refresh();
            });
        }

        #endregion

        #region Events
        private void btnAddHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (cboAction.SelectedValue is string actionKey &&
                cboAction.SelectedItem is HotkeyActionModel actionModel)
            {
                if (keyInputBox.SelectedKeyCode == -1)
                {
                    NotificationsShared.Error("Please select a key or mouse button first.");
                    return;
                }

                var keyName = keyInputBox.GetCurrentKeyName();
                var keyCode = keyInputBox.SelectedKeyCode;
                var type = rdbToggle.IsChecked == true ? "Toggle" : "OnKey";
                var existingAction = _hotkeyList.FirstOrDefault(h => h.Action == actionModel.Name);

                if (existingAction != null)
                {
                    _hotkeyList.Remove(existingAction);

                    var prop = typeof(HotkeyConfig).GetProperty(actionKey);
                    if (prop?.GetValue(Config.HotKeys) is HotkeyEntry oldEntry)
                    {
                        oldEntry.Enabled = false;
                        oldEntry.Key = -1;
                    }
                }

                _hotkeyList.Add(new HotkeyDisplayModel
                {
                    Action = actionModel.Name,
                    Key = keyName,
                    Type = type
                });

                var configProp = typeof(HotkeyConfig).GetProperty(actionKey);
                if (configProp?.GetValue(Config.HotKeys) is HotkeyEntry entry)
                {
                    entry.Enabled = true;
                    entry.Key = keyCode;
                    entry.Mode = type == "Toggle" ? HotkeyMode.Toggle : HotkeyMode.OnKey;
                    Config.Save();

                    RegisterHotkeyHandlers();
                }

                keyInputBox.ClearInput();
                RefreshHotkeyDisplay();
            }
        }

        private void btnRemoveHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (hotkeyListView.SelectedItem is HotkeyDisplayModel selected)
            {
                _hotkeyList.Remove(selected);

                var actionKey = AvailableHotkeyActions.FirstOrDefault(a => a.Name == selected.Action)?.Key;

                if (!string.IsNullOrEmpty(actionKey))
                {
                    var prop = typeof(HotkeyConfig).GetProperty(actionKey);
                    if (prop?.GetValue(Config.HotKeys) is HotkeyEntry entry)
                    {
                        entry.Enabled = false;
                        entry.Key = -1;
                        Config.Save();

                        _toggleStates.Remove(actionKey);
                    }
                }

                RegisterHotkeyHandlers();
                RefreshHotkeyDisplay();
            }
        }

        private void KeyInputBox_CapturingStateChanged(object sender, bool isCapturing)
        {
            _keyInputBoxIsCapturing = isCapturing;
        }

        private void cboAction_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_keyInputBoxIsCapturing)
                e.Handled = true;
        }
        #endregion
        #endregion
    }
}