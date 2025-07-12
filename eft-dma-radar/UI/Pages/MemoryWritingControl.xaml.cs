using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.UI.Controls;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using HandyControl.Controls;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.Features.MemoryWrites.Aimbot;
using static eft_dma_shared.Common.Unity.MonoLib;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = eft_dma_shared.Common.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for MemoryWritingSettingsControl.xaml
    /// </summary>
    public partial class MemoryWritingControl : UserControl
    {
        #region Fields and Properties
        private const int INTERVAL = 100; // 0.1 second

        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        private static Config Config => Program.Config;

        private bool _isLoadingAimbotOptions = false;
        private readonly string[] _availableAimbotOptions = new string[]
        {
            "Safe Lock",
            "Disable Re-Lock",
            "Auto Bone",
            "Headshot AI",
            "Random Bone"
        };
        #endregion

        public MemoryWritingControl()
        {
            InitializeComponent();
            TooltipManager.AssignMemoryWritingTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("MemoryWriting");
                ExpanderManager.Instance.RegisterExpanders(this, "MemoryWriting",
                    expGlobalSettings,
                    expAimbotSettings,
                    expWeapons,
                    expMovement,
                    expWorld,
                    expCamera,
                    expMisc);

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

        #region Memory Writing Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterSettingsEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
        }

        public void LoadSettings()
        {
            Dispatcher.Invoke(() =>
            {
                LoadAllSettings();
            });
        }
        #endregion

        #region Events
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
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

        #region Memory Writing Settings
        #region Functions/Methods
        private void RegisterSettingsEvents()
        {
            // Global Settings
            chkMasterSwitch.Checked += MemWritingCheckbox_Checked;
            chkMasterSwitch.Unchecked += MemWritingCheckbox_Checked;
            chkAdvancedWrites.Checked += MemWritingCheckbox_Checked;
            chkAdvancedWrites.Unchecked += MemWritingCheckbox_Checked;
            chkAntiPage.Checked += MemWritingCheckbox_Checked;
            chkAntiPage.Unchecked += MemWritingCheckbox_Checked;
            chkRageMode.Checked += MemWritingCheckbox_Checked;
            chkRageMode.Unchecked += MemWritingCheckbox_Checked;
            btnAntiAFK.Click += btnAntiAFK_Click;
            btnUnlockMaps.Click += btnUnlockMaps_Click;
            btnGymHack.Click += btnGymHack_Click;

            // Aimbot Settings
            chkEnableAimbot.Checked += MemWritingCheckbox_Checked;
            chkEnableAimbot.Unchecked += MemWritingCheckbox_Checked;
            rdbFOV.Checked += MemWritingRadioButton_Checked;
            rdbCQB.Checked += MemWritingRadioButton_Checked;
            cboTargetBone.SelectionChanged += cboTargetBone_SelectionChanged;
            sldrAimbotFOV.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotDistance.ValueChanged += MemWritingSlider_ValueChanged;
            ccbAimbotOptions.SelectionChanged += aimbotOptionsCheckComboBox_SelectionChanged;
            sldrAimbotRNGHead.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotRNGTorso.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotRNGArms.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotRNGLegs.ValueChanged += MemWritingSlider_ValueChanged;

            // Weapons
            chkNoWeaponMalfunctions.Checked += MemWritingCheckbox_Checked;
            chkNoWeaponMalfunctions.Unchecked += MemWritingCheckbox_Checked;
            chkMagDrills.Checked += MemWritingCheckbox_Checked;
            chkMagDrills.Unchecked += MemWritingCheckbox_Checked;
            chkFastWeaponOps.Checked += MemWritingCheckbox_Checked;
            chkFastWeaponOps.Unchecked += MemWritingCheckbox_Checked;
            chkDisableWeaponCollision.Checked += MemWritingCheckbox_Checked;
            chkDisableWeaponCollision.Unchecked += MemWritingCheckbox_Checked;
            chkRemoveableAttachments.Checked += MemWritingCheckbox_Checked;
            chkRemoveableAttachments.Unchecked += MemWritingCheckbox_Checked;
            chkNoRecoil.Checked += MemWritingCheckbox_Checked;
            chkNoRecoil.Unchecked += MemWritingCheckbox_Checked;
            btnNoRecoilConfig.Click += MemWritingButton_Clicked;
            sldrNoRecoilAmt.ValueChanged += MemWritingSlider_ValueChanged;
            sldrNoSwayAmt.ValueChanged += MemWritingSlider_ValueChanged;

            // Movement
            chkInfiniteStamina.Checked += MemWritingCheckbox_Checked;
            chkInfiniteStamina.Unchecked += MemWritingCheckbox_Checked;
            chkMoveSpeed.Checked += MemWritingCheckbox_Checked;
            chkMoveSpeed.Unchecked += MemWritingCheckbox_Checked;
            btnMoveSpeedConfig.Click += MemWritingButton_Clicked;
            sldrMoveSpeedMultiplier.ValueChanged += MemWritingSlider_ValueChanged;
            chkFastDuck.Checked += MemWritingCheckbox_Checked;
            chkFastDuck.Unchecked += MemWritingCheckbox_Checked;
            chkMuleMode.Checked += MemWritingCheckbox_Checked;
            chkMuleMode.Unchecked += MemWritingCheckbox_Checked;
            chkNoInertia.Checked += MemWritingCheckbox_Checked;
            chkNoInertia.Unchecked += MemWritingCheckbox_Checked;
            chkWideLean.Checked += MemWritingCheckbox_Checked;
            chkWideLean.Unchecked += MemWritingCheckbox_Checked;
            btnWideLeanConfig.Click += MemWritingButton_Clicked;
            sldrLeanAmt.ValueChanged += MemWritingSlider_ValueChanged;
            chkLongJump.Checked += MemWritingCheckbox_Checked;
            chkLongJump.Unchecked += MemWritingCheckbox_Checked;
            chkNoFall.Checked += MemWritingCheckbox_Checked;
            chkNoFall.Unchecked += MemWritingCheckbox_Checked;
            btnLongJumpConfig.Click += MemWritingButton_Clicked;
            sldrLongJumpMultiplier.ValueChanged += MemWritingSlider_ValueChanged;           

            // World
            chkTimeOfDay.Checked += MemWritingCheckbox_Checked;
            chkTimeOfDay.Unchecked += MemWritingCheckbox_Checked;
            btnTimeOfDayConfig.Click += MemWritingButton_Clicked;
            sldrTimeOfDayHour.ValueChanged += MemWritingSlider_ValueChanged;
            chkFullBright.Checked += MemWritingCheckbox_Checked;
            chkFullBright.Unchecked += MemWritingCheckbox_Checked;
            btnFullBrightConfig.Click += MemWritingButton_Clicked;
            sldrFullBrightIntensity.ValueChanged += MemWritingSlider_ValueChanged;
            chkLTW.Checked += MemWritingCheckbox_Checked;
            chkLTW.Unchecked += MemWritingCheckbox_Checked;
            btnLTWConfig.Click += MemWritingButton_Clicked;
            sldrLTWZoom.ValueChanged += MemWritingSlider_ValueChanged;
            chkExtendedReach.Checked += MemWritingCheckbox_Checked;
            chkExtendedReach.Unchecked += MemWritingCheckbox_Checked;
            btnExtendedReachConfig.Click += MemWritingButton_Clicked;
            sldrExtendedReachDistance.ValueChanged += MemWritingSlider_ValueChanged;
            chkSilentLoot.Checked += MemWritingCheckbox_Checked;
            chkSilentLoot.Unchecked += MemWritingCheckbox_Checked;
            btnSilentLootConfig.Click += MemWritingButton_Clicked;
            sldrSilentLootDistance.ValueChanged += MemWritingSlider_ValueChanged;
            sldrSilentLootMaxDistance.ValueChanged += MemWritingSlider_ValueChanged;

            // Camera
            chkNoVisor.Checked += MemWritingCheckbox_Checked;
            chkNoVisor.Unchecked += MemWritingCheckbox_Checked;
            chkNightVision.Checked += MemWritingCheckbox_Checked;
            chkNightVision.Unchecked += MemWritingCheckbox_Checked;
            chkThermalVision.Checked += MemWritingCheckbox_Checked;
            chkThermalVision.Unchecked += MemWritingCheckbox_Checked;
            chkThirdPerson.Checked += MemWritingCheckbox_Checked;
            chkThirdPerson.Unchecked += MemWritingCheckbox_Checked;
            chkOwlMode.Checked += MemWritingCheckbox_Checked;
            chkOwlMode.Unchecked += MemWritingCheckbox_Checked;
            chkDisableScreenEffects.Checked += MemWritingCheckbox_Checked;
            chkDisableScreenEffects.Unchecked += MemWritingCheckbox_Checked;
            chkDisableShadows.Checked += MemWritingCheckbox_Checked;
            chkDisableShadows.Unchecked += MemWritingCheckbox_Checked;
            chkDisableGrass.Checked += MemWritingCheckbox_Checked;
            chkDisableGrass.Unchecked += MemWritingCheckbox_Checked;
            chkClearWeather.Checked += MemWritingCheckbox_Checked;
            chkClearWeather.Unchecked += MemWritingCheckbox_Checked;
            chkDisableHeadBobbing.Checked += MemWritingCheckbox_Checked;
            chkDisableHeadBobbing.Unchecked += MemWritingCheckbox_Checked;
            chkFOVChanger.Checked += MemWritingCheckbox_Checked;
            chkFOVChanger.Unchecked += MemWritingCheckbox_Checked;
            btnFOVConfig.Click += MemWritingButton_Clicked;
            sldrFOVBase.ValueChanged += MemWritingSlider_ValueChanged;
            sldrADSFOV.ValueChanged += MemWritingSlider_ValueChanged;
            sldrTPPFOV.ValueChanged += MemWritingSlider_ValueChanged;
            sldrZoomFOV.ValueChanged += MemWritingSlider_ValueChanged;

            // Misc
            chkStreamerMode.Checked += MemWritingCheckbox_Checked;
            chkStreamerMode.Unchecked += MemWritingCheckbox_Checked;
            chkHideRaidCode.Checked += MemWritingCheckbox_Checked;
            chkHideRaidCode.Unchecked += MemWritingCheckbox_Checked;
            chkInstantPlant.Checked += MemWritingCheckbox_Checked;
            chkInstantPlant.Unchecked += MemWritingCheckbox_Checked;
            chkMedPanel.Checked += MemWritingCheckbox_Checked;
            chkMedPanel.Unchecked += MemWritingCheckbox_Checked;
            chkDisableInventoryBlur.Checked += MemWritingCheckbox_Checked;
            chkDisableInventoryBlur.Unchecked += MemWritingCheckbox_Checked;
            chkBigHeads.Checked += MemWritingCheckbox_Checked;
            chkBigHeads.Unchecked += MemWritingCheckbox_Checked;
            btnBigHeadsConfig.Click += MemWritingButton_Clicked;
            sldrBigHeadScale.ValueChanged += MemWritingSlider_ValueChanged;
        }

        private void LoadAllSettings()
        {
            var cfg = Config.MemWrites;

            // Global Settings
            chkMasterSwitch.IsChecked = cfg.MemWritesEnabled;
            chkAdvancedWrites.IsChecked = cfg.AdvancedMemWrites;
            chkAntiPage.IsChecked = cfg.AntiPage;

            // Aimbot Settings
            LoadAimbotOptions();

            // Weapon
            chkNoWeaponMalfunctions.IsChecked = cfg.NoWeaponMalfunctions;
            chkMagDrills.IsChecked = cfg.FastLoadUnload;
            chkFastWeaponOps.IsChecked = cfg.FastWeaponOps;
            chkDisableWeaponCollision.IsChecked = cfg.DisableWeaponCollision;
            chkRemoveableAttachments.IsChecked = cfg.RemoveableAttachments;
            chkNoRecoil.IsChecked = cfg.NoRecoil;
            sldrNoRecoilAmt.Value = cfg.NoRecoilAmount;
            sldrNoSwayAmt.Value = cfg.NoSwayAmount;

            // Movement
            chkInfiniteStamina.IsChecked = cfg.InfStamina;
            chkMoveSpeed.IsChecked = cfg.MoveSpeed.Enabled;
            sldrMoveSpeedMultiplier.Value = cfg.MoveSpeed.Multiplier;
            chkFastDuck.IsChecked = cfg.FastDuck;
            chkMuleMode.IsChecked = cfg.MuleMode;
            chkNoInertia.IsChecked = cfg.NoInertia;
            chkWideLean.IsChecked = cfg.WideLean.Enabled;
            sldrLeanAmt.Value = cfg.WideLean.Amount;
            chkLongJump.IsChecked = cfg.LongJump.Enabled;
            sldrLongJumpMultiplier.Value = cfg.LongJump.Multiplier;
            chkNoFall.IsChecked = cfg.NoFall;

            // World
            chkDisableShadows.IsChecked = cfg.DisableShadows;
            chkDisableGrass.IsChecked = cfg.DisableGrass;
            chkClearWeather.IsChecked = cfg.ClearWeather;
            chkTimeOfDay.IsChecked = cfg.TimeOfDay.Enabled;
            sldrTimeOfDayHour.Value = cfg.TimeOfDay.Hour;
            chkFullBright.IsChecked = cfg.FullBright.Enabled;
            sldrFullBrightIntensity.Value = cfg.FullBright.Intensity;
            chkLTW.IsChecked = cfg.LootThroughWalls.Enabled;
            chkSilentLoot.IsChecked = cfg.SilentLoot.Enabled;
            sldrLTWZoom.Value = cfg.LootThroughWalls.ZoomAmount;
            sldrSilentLootDistance.Value = cfg.SilentLoot.Distance;
            sldrSilentLootMaxDistance.Value = cfg.SilentLoot.MaxDistance;
            chkExtendedReach.IsChecked = cfg.ExtendedReach.Enabled;
            sldrExtendedReachDistance.Value = cfg.ExtendedReach.Distance;

            // Camera
            chkNoVisor.IsChecked = cfg.NoVisor;
            chkNightVision.IsChecked = cfg.NightVision;
            chkThermalVision.IsChecked = cfg.ThermalVision;
            chkThirdPerson.IsChecked = cfg.ThirdPerson;
            chkOwlMode.IsChecked = cfg.OwlMode;
            chkDisableScreenEffects.IsChecked = cfg.DisableScreenEffects;
            chkDisableHeadBobbing.IsChecked = cfg.DisableHeadBobbing;
            chkFOVChanger.IsChecked = cfg.FOV.Enabled;
            sldrFOVBase.Value = cfg.FOV.Base;
            sldrADSFOV.Value = cfg.FOV.ADS;
            sldrTPPFOV.Value = cfg.FOV.ThirdPerson;
            sldrZoomFOV.Value = cfg.FOV.InstantZoom;

            // Misc
            chkStreamerMode.IsChecked = cfg.StreamerMode;
            chkHideRaidCode.IsChecked = cfg.HideRaidCode;
            chkInstantPlant.IsChecked = cfg.InstantPlant;
            chkMedPanel.IsChecked = cfg.MedPanel;
            chkDisableInventoryBlur.IsChecked = cfg.DisableInventoryBlur;
            chkBigHeads.IsChecked = cfg.BigHead.Enabled;
            sldrBigHeadScale.Value = cfg.BigHead.Scale;

            ToggleMemWritingControls();
            FeatureInstanceCheck();
        }

        private void LoadAimbotOptions()
        {
            _isLoadingAimbotOptions = true;

            try
            {
                var cfg = Config.MemWrites;
                var aimbotConfig = cfg.Aimbot;
                chkEnableAimbot.IsChecked = aimbotConfig.Enabled;
                var rdb = (aimbotConfig.TargetingMode == AimbotTargetingMode.FOV) ? rdbFOV : rdbCQB;
                rdb.IsChecked = true;
                cboTargetBone.SelectedIndex = cfg.Aimbot.Bone switch
                {
                    Bones.HumanHead => 0,
                    Bones.HumanNeck => 1,
                    Bones.HumanSpine3 => 2,
                    Bones.HumanPelvis => 3,
                    Bones.Legs => 4,
                    _ => 0
                };

                sldrAimbotFOV.Value = aimbotConfig.FOV;
                sldrAimbotDistance.Value = aimbotConfig.Distance;
                sldrAimbotRNGHead.Value = aimbotConfig.RandomBone.HeadPercent;
                sldrAimbotRNGTorso.Value = aimbotConfig.RandomBone.TorsoPercent;
                sldrAimbotRNGArms.Value = aimbotConfig.RandomBone.ArmsPercent;
                sldrAimbotRNGLegs.Value = aimbotConfig.RandomBone.LegsPercent;

                ccbAimbotOptions.Items.Clear();
                foreach (var option in _availableAimbotOptions)
                {
                    ccbAimbotOptions.Items.Add(new CheckComboBoxItem { Content = option });
                }
            }
            finally
            {
                _isLoadingAimbotOptions = false;
            }

            UpdateAimbotOptionSelections();
        }

        private void UpdateAimbotOptionSelections()
        {
            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Safe Lock"] = Config.MemWrites.Aimbot.SilentAim.SafeLock,
                ["Disable Re-Lock"] = Config.MemWrites.Aimbot.DisableReLock,
                ["Auto Bone"] = Config.MemWrites.Aimbot.SilentAim.AutoBone,
                ["Headshot AI"] = Config.MemWrites.Aimbot.HeadshotAI,
                ["Random Bone"] = Config.MemWrites.Aimbot.RandomBone.Enabled
            };

            foreach (CheckComboBoxItem item in ccbAimbotOptions.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    item.IsSelected = shouldBeSelected;
            }
        }

        private void ToggleMemWritingControls()
        {
            var memWritingEnabled = MemWrites.Enabled;

            // Global Settings
            chkRageMode.IsEnabled = memWritingEnabled;
            btnAntiAFK.IsEnabled = memWritingEnabled;
            btnGymHack.IsEnabled = memWritingEnabled;
            btnUnlockMaps.IsEnabled = memWritingEnabled;

            // Aimbot Settings
            ToggleAimbotControls();

            // Weapon
            chkNoWeaponMalfunctions.IsEnabled = memWritingEnabled;
            chkMagDrills.IsEnabled = memWritingEnabled;
            chkFastWeaponOps.IsEnabled = memWritingEnabled;
            chkDisableWeaponCollision.IsEnabled = memWritingEnabled;
            ToggleNoRecoilControls();

            // Movement
            chkInfiniteStamina.IsEnabled = memWritingEnabled;
            chkFastDuck.IsEnabled = memWritingEnabled;
            chkMuleMode.IsEnabled = memWritingEnabled;
            chkNoInertia.IsEnabled = memWritingEnabled;
            chkNoFall.IsEnabled = memWritingEnabled;
            ToggleMoveSpeedControls();
            ToggleWideLeanControls();
            ToggleLongJumpControls();

            // World
            chkDisableShadows.IsEnabled = memWritingEnabled;
            chkDisableGrass.IsEnabled = memWritingEnabled;
            chkClearWeather.IsEnabled = memWritingEnabled;
            ToggleFullBrightControls();
            ToggleTimeOfDayControls();
            ToggleLTWControls();
            ToggleSilentLootControls();
            ToggleExtendedReachControls();

            // Camera
            chkNoVisor.IsEnabled = memWritingEnabled;
            chkNightVision.IsEnabled = memWritingEnabled;
            chkThermalVision.IsEnabled = memWritingEnabled;
            chkThirdPerson.IsEnabled = memWritingEnabled;
            chkOwlMode.IsEnabled = memWritingEnabled;
            chkDisableHeadBobbing.IsEnabled = memWritingEnabled;
            ToggleFOVControls();

            // Misc
            chkInstantPlant.IsEnabled = memWritingEnabled;
            chkMedPanel.IsEnabled = memWritingEnabled;
            chkDisableInventoryBlur.IsEnabled = memWritingEnabled;
            ToggleBigHeadControls();

            ToggleAdvMemWritingControls();
        }

        private void ToggleAimbotControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.Aimbot.Enabled;
            var rndBoneEnabled = Config.MemWrites.Aimbot.RandomBone.Enabled;

            chkEnableAimbot.IsEnabled = memWrites;
            rdbFOV.IsEnabled = enableControl;
            rdbCQB.IsEnabled = enableControl;
            sldrAimbotFOV.IsEnabled = enableControl;
            sldrAimbotDistance.IsEnabled = enableControl;
            ccbAimbotOptions.IsEnabled = enableControl;

            ToggleAimbotRandomBoneControls();
        }

        private void ToggleAimbotRandomBoneControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.Aimbot.Enabled;
            var rndBoneEnabled = Config.MemWrites.Aimbot.RandomBone.Enabled;

            cboTargetBone.IsEnabled = enableControl && !rndBoneEnabled;

            pnlBoneRNG.Visibility = (enableControl && rndBoneEnabled ? Visibility.Visible : Visibility.Collapsed);
            sldrAimbotRNGHead.IsEnabled = enableControl && rndBoneEnabled;
            sldrAimbotRNGTorso.IsEnabled = enableControl && rndBoneEnabled;
            sldrAimbotRNGArms.IsEnabled = enableControl && rndBoneEnabled;
            sldrAimbotRNGLegs.IsEnabled = enableControl && rndBoneEnabled;
        }

        private void ToggleNoRecoilControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.NoRecoil;

            chkNoRecoil.IsEnabled = memWrites;
            btnNoRecoilConfig.IsEnabled = enableControl;
            sldrNoRecoilAmt.IsEnabled = enableControl;
            sldrNoSwayAmt.IsEnabled = enableControl;

            if (!enableControl && pnlNoRecoil.Visibility == Visibility.Visible)
                pnlNoRecoil.Visibility = Visibility.Collapsed;
        }

        private void ToggleMoveSpeedControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.MoveSpeed.Enabled;

            chkMoveSpeed.IsEnabled = memWrites;
            btnMoveSpeedConfig.IsEnabled = enableControl;
            sldrMoveSpeedMultiplier.IsEnabled = enableControl;

            if (!enableControl && pnlMoveSpeed.Visibility == Visibility.Visible)
                pnlMoveSpeed.Visibility = Visibility.Collapsed;
        }

        private void ToggleWideLeanControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.WideLean.Enabled;

            chkWideLean.IsEnabled = memWrites;
            btnWideLeanConfig.IsEnabled = enableControl;
            sldrLeanAmt.IsEnabled = enableControl;

            if (!enableControl && pnlWideLean.Visibility == Visibility.Visible)
                pnlWideLean.Visibility = Visibility.Collapsed;
        }

        private void ToggleLongJumpControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.LongJump.Enabled;

            chkLongJump.IsEnabled = memWrites;
            btnLongJumpConfig.IsEnabled = enableControl;
            sldrLongJumpMultiplier.IsEnabled = enableControl;

            if (!enableControl && pnlLongJump.Visibility == Visibility.Visible)
                pnlLongJump.Visibility = Visibility.Collapsed;
        }

        private void ToggleBigHeadControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.BigHead.Enabled;

            chkBigHeads.IsEnabled = memWrites;
            btnBigHeadsConfig.IsEnabled = enableControl;
            sldrBigHeadScale.IsEnabled = enableControl;

            if (!enableControl && pnlBigHeads.Visibility == Visibility.Visible)
                pnlBigHeads.Visibility = Visibility.Collapsed;
        }

        private void ToggleTimeOfDayControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.TimeOfDay.Enabled;

            chkTimeOfDay.IsEnabled = memWrites;
            btnTimeOfDayConfig.IsEnabled = enableControl;
            sldrTimeOfDayHour.IsEnabled = enableControl;

            if (!enableControl && pnlTimeOfDay.Visibility == Visibility.Visible)
                pnlTimeOfDay.Visibility = Visibility.Collapsed;
        }

        private void ToggleFullBrightControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.FullBright.Enabled;

            chkFullBright.IsEnabled = memWrites;
            btnFullBrightConfig.IsEnabled = enableControl;
            sldrFullBrightIntensity.IsEnabled = enableControl;

            if (!enableControl && pnlFullBright.Visibility == Visibility.Visible)
                pnlFullBright.Visibility = Visibility.Collapsed;
        }

        private void ToggleLTWControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.LootThroughWalls.Enabled;

            chkLTW.IsEnabled = memWrites;
            btnLTWConfig.IsEnabled = enableControl;
            sldrLTWZoom.IsEnabled = enableControl;

            if (!enableControl && pnlLTW.Visibility == Visibility.Visible)
                pnlLTW.Visibility = Visibility.Collapsed;
        }
        private void ToggleSilentLootControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.SilentLoot.Enabled;

            chkSilentLoot.IsEnabled = memWrites;
            btnSilentLootConfig.IsEnabled = enableControl;
            sldrSilentLootDistance.IsEnabled = enableControl;
            sldrSilentLootMaxDistance.IsEnabled = enableControl;

            if (!enableControl && pnlSilentLoot.Visibility == Visibility.Visible)
                pnlSilentLoot.Visibility = Visibility.Collapsed;
        }

        private void ToggleExtendedReachControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.ExtendedReach.Enabled;

            chkExtendedReach.IsEnabled = memWrites;
            btnExtendedReachConfig.IsEnabled = enableControl;
            sldrExtendedReachDistance.IsEnabled = enableControl;

            if (!enableControl && pnlExtendedReach.Visibility == Visibility.Visible)
                pnlExtendedReach.Visibility = Visibility.Collapsed;
        }

        private void ToggleAdvMemWritingControls()
        {
            var memWritingEnabled = MemWrites.Enabled;
            var advMemWrites = Config.MemWrites.AdvancedMemWrites;
            var enabled = (memWritingEnabled && advMemWrites);

            // General Settings
            chkAdvancedWrites.IsEnabled = memWritingEnabled;
            chkAntiPage.IsEnabled = enabled;
            
            // Chams
            MainWindow.Window.ESPControl.UpdateChamsControls();

            // Weapon
            chkRemoveableAttachments.IsEnabled = enabled;

            // World
            chkDisableShadows.IsEnabled = enabled;

            // Camera
            chkDisableScreenEffects.IsEnabled = enabled;
            chkFOVChanger.IsEnabled = enabled;
            btnFOVConfig.IsEnabled = (enabled && Config.MemWrites.FOV.Enabled);

            // Misc
            chkStreamerMode.IsEnabled = enabled;
            chkHideRaidCode.IsEnabled = enabled;
        }

        public void ToggleAimbotBone()
        {
            Dispatcher.Invoke(() =>
            {
                int maxIndex = cboTargetBone.Items.Count - 1;
                int currentIndex = cboTargetBone.SelectedIndex;
                int newIndex = currentIndex + 1;

                cboTargetBone.SelectedIndex = (newIndex > maxIndex) ? 0 : newIndex;
            });
        }

        private void ToggleFOVControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && Config.MemWrites.FOV.Enabled;

            chkFOVChanger.IsEnabled = memWrites && Config.MemWrites.AdvancedMemWrites;
            btnFOVConfig.IsEnabled = enableControl && Config.MemWrites.AdvancedMemWrites;
            sldrFOVBase.IsEnabled = enableControl;
            sldrADSFOV.IsEnabled = enableControl;
            sldrTPPFOV.IsEnabled = enableControl;
            sldrZoomFOV.IsEnabled = enableControl;

            if (!enableControl && pnlFOV.Visibility == Visibility.Visible)
                pnlFOV.Visibility = Visibility.Collapsed;
        }

        public void FeatureInstanceCheck()
        {
            var cfg = Config.MemWrites;
            var memWritesOn = MemWrites.Enabled;
            var advMemWritesOn = memWritesOn && cfg.AdvancedMemWrites;

            MemPatchFeature<FOVChanger>.Instance.Enabled = (advMemWritesOn && cfg.FOV.Enabled);

            MemWriteFeature<Aimbot>.Instance.Enabled = (memWritesOn && cfg.Aimbot.Enabled);
            MemPatchFeature<NoWepMalfPatch>.Instance.Enabled = (memWritesOn && cfg.NoWeaponMalfunctions);
            MemPatchFeature<FastLoadUnload>.Instance.Enabled = (memWritesOn && cfg.FastLoadUnload);
            MemWriteFeature<FastWeaponOps>.Instance.Enabled = (memWritesOn && cfg.FastWeaponOps);
            MemWriteFeature<DisableWeaponCollision>.Instance.Enabled = (memWritesOn && cfg.DisableWeaponCollision);
            MemPatchFeature<RemoveableAttachments>.Instance.Enabled = (advMemWritesOn && cfg.RemoveableAttachments);
            MemWriteFeature<NoRecoil>.Instance.Enabled = (memWritesOn && cfg.NoRecoil);
            MemWriteFeature<InfStamina>.Instance.Enabled = (memWritesOn && cfg.InfStamina);
            MemWriteFeature<MoveSpeed>.Instance.Enabled = (memWritesOn && cfg.MoveSpeed.Enabled);
            MemWriteFeature<FastDuck>.Instance.Enabled = (memWritesOn && cfg.FastDuck);
            MemWriteFeature<MuleMode>.Instance.Enabled = (memWritesOn && cfg.MuleMode);
            MemWriteFeature<NoInertia>.Instance.Enabled = (memWritesOn && cfg.NoInertia);
            MemWriteFeature<TimeOfDay>.Instance.Enabled = (memWritesOn && cfg.TimeOfDay.Enabled);
            MemWriteFeature<LootThroughWalls>.Instance.Enabled = (memWritesOn && cfg.LootThroughWalls.Enabled);
            MemWriteFeature<ExtendedReach>.Instance.Enabled = (memWritesOn && cfg.ExtendedReach.Enabled);
            MemWriteFeature<FullBright>.Instance.Enabled = (memWritesOn && cfg.FullBright.Enabled);
            MemPatchFeature<DisableShadows>.Instance.Enabled = (advMemWritesOn && cfg.DisableShadows);
            MemWriteFeature<DisableGrass>.Instance.Enabled = (memWritesOn && cfg.DisableGrass);
            MemWriteFeature<ClearWeather>.Instance.Enabled = (memWritesOn && cfg.ClearWeather);
            MemWriteFeature<NoVisor>.Instance.Enabled = (memWritesOn && cfg.NoVisor);
            MemWriteFeature<ThermalVision>.Instance.Enabled = (memWritesOn && cfg.ThermalVision);
            MemWriteFeature<NightVision>.Instance.Enabled = (memWritesOn && cfg.NightVision);
            MemWriteFeature<WideLean>.Instance.Enabled = (memWritesOn && cfg.WideLean.Enabled);
            MemWriteFeature<LongJump>.Instance.Enabled = (memWritesOn && cfg.LongJump.Enabled);
            MemWriteFeature<NoFall>.Instance.Enabled = (memWritesOn && cfg.NoFall);
            MemWriteFeature<ThirdPerson>.Instance.Enabled = (memWritesOn && cfg.ThirdPerson);
            MemWriteFeature<OwlMode>.Instance.Enabled = (memWritesOn && cfg.OwlMode);
            MemWriteFeature<DisableHeadBobbing>.Instance.Enabled = (memWritesOn && cfg.DisableHeadBobbing);
            MemPatchFeature<StreamerMode>.Instance.Enabled = (advMemWritesOn && cfg.StreamerMode);
            MemPatchFeature<HideRaidCode>.Instance.Enabled = (advMemWritesOn && cfg.HideRaidCode);
            MemWriteFeature<InstantPlant>.Instance.Enabled = (memWritesOn && cfg.InstantPlant);
            MemWriteFeature<MedPanel>.Instance.Enabled = (memWritesOn && cfg.MedPanel);
            MemWriteFeature<DisableInventoryBlur>.Instance.Enabled = (memWritesOn && cfg.DisableInventoryBlur);
            MemPatchFeature<DisableScreenEffects>.Instance.Enabled = (advMemWritesOn && cfg.DisableScreenEffects);
            MemWriteFeature<BigHead>.Instance.Enabled = (memWritesOn && cfg.BigHead.Enabled);
            MemPatchFeature<SilentLoot>.Instance.Enabled = (memWritesOn && cfg.SilentLoot.Enabled);
        }

        private void ToggleSettingsPanel(UIElement panel)
        {
            panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        public void UpdateSpecificAimbotOption(string optionName, bool isSelected)
        {
            if (_isLoadingAimbotOptions)
                return;

            foreach (CheckComboBoxItem item in ccbAimbotOptions.Items)
            {
                if (item.Content.ToString() == optionName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine($"Updated aimbot option: {optionName} = {isSelected}");
        }
        #endregion

        #region Events
        private void MemWritingCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tag)
            {
                var shouldProceed = true;
                var value = cb.IsChecked == true;

                switch (tag)
                {
                    case "MemWritesEnabled":
                        if (value && !Config.MemWrites.MemWritesEnabled)
                        {
                            shouldProceed = ConfirmMemoryWritingEnable(false);
                            if (!shouldProceed)
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    cb.IsChecked = false;
                                }), DispatcherPriority.Render);
                                return;
                            }
                        }
                        break;
                    case "AdvancedMemWrites":
                        if (value && !Config.MemWrites.AdvancedMemWrites)
                        {
                            shouldProceed = ConfirmMemoryWritingEnable(true);
                            if (!shouldProceed)
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    cb.IsChecked = false;
                                }), DispatcherPriority.Render);
                                return;
                            }
                        }
                        break;
                }

                LoneLogging.WriteLine($"[Checkbox] {cb.Name} changed to {value}");

                switch (tag)
                {
                    case "MemWritesEnabled":
                        Config.MemWrites.MemWritesEnabled = value;
                        MemWrites.Enabled = value;
                        ToggleMemWritingControls();
                        break;
                    case "AdvancedMemWrites":
                        Config.MemWrites.AdvancedMemWrites = value;
                        ToggleAdvMemWritingControls();
                        break;
                    case "AntiPage":
                        Config.MemWrites.AntiPage = value;
                        break;
                    case "RageMode":
                        Config.MemWrites.RageMode = value;
                        MemWriteFeature<RageMode>.Instance.Enabled = value;
                        break;
                    case "EnableAimbot":
                        Config.MemWrites.Aimbot.Enabled = value;
                        MemWriteFeature<Aimbot>.Instance.Enabled = value;
                        ToggleAimbotControls();
                        break;
                    case "AimbotSafeLock":
                        Config.MemWrites.Aimbot.SilentAim.SafeLock = value;
                        break;
                    case "AimbotAutoBone":
                        Config.MemWrites.Aimbot.SilentAim.AutoBone = value;
                        break;
                    case "AimbotDisableReLock":
                        Config.MemWrites.Aimbot.DisableReLock = value;
                        break;
                    case "HeadshotAI":
                        Config.MemWrites.Aimbot.HeadshotAI = value;
                        break;
                    case "AimbotRandomBone":
                        Config.MemWrites.Aimbot.RandomBone.Enabled = value;
                        ToggleAimbotRandomBoneControls();
                        break;
                    case "NoWeaponMalfunctions":
                        Config.MemWrites.NoWeaponMalfunctions = value;
                        MemPatchFeature<NoWepMalfPatch>.Instance.Enabled = value;
                        break;
                    case "MagDrills":
                        Config.MemWrites.FastLoadUnload = value;
                        MemPatchFeature<FastLoadUnload>.Instance.Enabled = value;
                        break;
                    case "FastWeaponOps":
                        Config.MemWrites.FastWeaponOps = value;
                        MemWriteFeature<FastWeaponOps>.Instance.Enabled = value;
                        break;
                    case "DisableWeaponCollision":
                        MemWrites.Config.DisableWeaponCollision = value;
                        MemWriteFeature<DisableWeaponCollision>.Instance.Enabled = value;
                        break;
                    case "RemoveableAttachments":
                        Config.MemWrites.RemoveableAttachments = value;
                        MemPatchFeature<RemoveableAttachments>.Instance.Enabled = value;
                        break;
                    case "NoRecoil":
                        Config.MemWrites.NoRecoil = value;
                        MemWriteFeature<NoRecoil>.Instance.Enabled = value;
                        ToggleNoRecoilControls();
                        break;
                    case "InfiniteStamina":
                        Config.MemWrites.InfStamina = value;
                        MemWriteFeature<InfStamina>.Instance.Enabled = value;
                        break;
                    case "MoveSpeed":
                        Config.MemWrites.MoveSpeed.Enabled = value;
                        MemWriteFeature<MoveSpeed>.Instance.Enabled = value;
                        ToggleMoveSpeedControls();
                        break;
                    case "TimeOfDay":
                        Config.MemWrites.TimeOfDay.Enabled = value;
                        MemWriteFeature<TimeOfDay>.Instance.Enabled = value;
                        ToggleTimeOfDayControls();
                        break;
                    case "DisableShadows":
                        MemWrites.Config.DisableShadows = value;
                        MemPatchFeature<DisableShadows>.Instance.Enabled = value;
                        break;
                    case "DisableGrass":
                        MemWrites.Config.DisableGrass = value;
                        MemWriteFeature<DisableGrass>.Instance.Enabled = value;
                        break;
                    case "ClearWeather":
                        MemWrites.Config.ClearWeather = value;
                        MemWriteFeature<ClearWeather>.Instance.Enabled = value;
                        break;
                    case "LTW":
                        Config.MemWrites.LootThroughWalls.Enabled = value;
                        MemWriteFeature<LootThroughWalls>.Instance.Enabled = value;
                        ToggleLTWControls();
                        break;
                    case "SilentLoot":
                        Config.MemWrites.SilentLoot.Enabled = value;
                        MemPatchFeature<SilentLoot>.Instance.Enabled = value;
                        ToggleSilentLootControls();
                        NotificationsShared.Info("awawawa");
                        break;
                    case "ExtendedReach":
                        Config.MemWrites.ExtendedReach.Enabled = value;
                        MemWriteFeature<ExtendedReach>.Instance.Enabled = value;
                        ToggleExtendedReachControls();
                        break;
                    case "FullBright":
                        Config.MemWrites.FullBright.Enabled = value;
                        MemWriteFeature<FullBright>.Instance.Enabled = value;
                        ToggleFullBrightControls();
                        break;
                    case "NoVisor":
                        MemWrites.Config.NoVisor = value;
                        MemWriteFeature<NoVisor>.Instance.Enabled = value;
                        break;
                    case "ThermalVision":
                        MemWrites.Config.ThermalVision = value;
                        MemWriteFeature<ThermalVision>.Instance.Enabled = value;
                        break;
                    case "NightVision":
                        MemWrites.Config.NightVision = value;
                        MemWriteFeature<NightVision>.Instance.Enabled = value;
                        break;
                    case "WideLean":
                        MemWrites.Config.WideLean.Enabled = value;
                        MemWriteFeature<WideLean>.Instance.Enabled = value;
                        ToggleWideLeanControls();
                        break;
                    case "ThirdPerson":
                        MemWrites.Config.ThirdPerson = value;
                        MemWriteFeature<ThirdPerson>.Instance.Enabled = value;
                        break;
                    case "OwlMode":
                        MemWrites.Config.OwlMode = value;
                        MemWriteFeature<OwlMode>.Instance.Enabled = value;
                        break;
                    case "FOVChanger":
                        Config.MemWrites.FOV.Enabled = value;
                        MemPatchFeature<FOVChanger>.Instance.Enabled = value;
                        ToggleFOVControls();
                        break;
                    case "StreamerMode":
                        MemWrites.Config.StreamerMode = value;
                        MemPatchFeature<StreamerMode>.Instance.Enabled = value;
                        break;
                    case "HideRaidCode":
                        MemWrites.Config.HideRaidCode = value;
                        MemPatchFeature<HideRaidCode>.Instance.Enabled = value;
                        break;
                    case "InstantPlant":
                        MemWrites.Config.InstantPlant = value;
                        MemWriteFeature<InstantPlant>.Instance.Enabled = value;
                        break;
                    case "DisableInventoryBlur":
                        MemWrites.Config.DisableInventoryBlur = value;
                        MemWriteFeature<DisableInventoryBlur>.Instance.Enabled = value;
                        break;
                    case "MedPanel":
                        MemWrites.Config.MedPanel = value;
                        MemWriteFeature<MedPanel>.Instance.Enabled = value;
                        break;
                    case "DisableScreenEffects":
                        MemWrites.Config.DisableScreenEffects = value;
                        MemPatchFeature<DisableScreenEffects>.Instance.Enabled = value;
                        break;
                    case "DisableHeadBobbing":
                        MemWrites.Config.DisableHeadBobbing = value;
                        MemWriteFeature<DisableHeadBobbing>.Instance.Enabled = value;
                        break;
                    case "FastDuck":
                        MemWrites.Config.FastDuck = value;
                        MemWriteFeature<FastDuck>.Instance.Enabled = value;
                        break;
                    case "MuleMode":
                        MemWrites.Config.MuleMode = value;
                        MemWriteFeature<MuleMode>.Instance.Enabled = value;
                        break;
                    case "NoInertia":
                        MemWrites.Config.NoInertia = value;
                        MemWriteFeature<NoInertia>.Instance.Enabled = value;
                        break;
                    case "NoFall":
                        MemWrites.Config.NoFall = value;
                        MemWriteFeature<NoFall>.Instance.Enabled = value;
                        break;
                    case "LongJump":
                        MemWrites.Config.LongJump.Enabled = value;
                        MemWriteFeature<LongJump>.Instance.Enabled = value;
                        ToggleLongJumpControls();
                        break;
                    case "BigHeads":
                        MemWrites.Config.BigHead.Enabled = value;
                        MemWriteFeature<BigHead>.Instance.Enabled = value;
                        ToggleBigHeadControls();
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("Saved Convig");
            }
        }

        private void MemWritingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is TextValueSlider slider && slider.Tag is string tag)
            {
                var intValue = (int)e.NewValue;
                var floatValue = (float)e.NewValue;
                var roundedValue = (float)Math.Round(floatValue, 1);

                switch (tag)
                {
                    case "AimbotFOV":
                        Config.MemWrites.Aimbot.FOV = floatValue;
                        break;
                    case "AimbotDistance":
                        Config.MemWrites.Aimbot.Distance = floatValue;
                        break;
                    case "NoRecoilAmount":
                        Config.MemWrites.NoRecoilAmount = intValue;
                        break;
                    case "NoSwayAmount":
                        Config.MemWrites.NoSwayAmount = intValue;
                        break;

                    case "RNGHead":
                    case "RNGTorso":
                    case "RNGArms":
                    case "RNGLegs":
                    {
                        var rng = Config.MemWrites.Aimbot.RandomBone;

                        switch (tag)
                        {
                            case "RNGHead": rng.HeadPercent = intValue; break;
                            case "RNGTorso": rng.TorsoPercent = intValue; break;
                            case "RNGArms": rng.ArmsPercent = intValue; break;
                            case "RNGLegs": rng.LegsPercent = intValue; break;
                        }
            
                        var total = rng.HeadPercent + rng.TorsoPercent + rng.ArmsPercent + rng.LegsPercent;

                        if (total > 100)
                        {
                            var overflow = total - 100;
                            var sliders = new Dictionary<string, Action<int>>
                            {
                                ["RNGHead"] = v => rng.HeadPercent = v,
                                ["RNGTorso"] = v => rng.TorsoPercent = v,
                                ["RNGArms"] = v => rng.ArmsPercent = v,
                                ["RNGLegs"] = v => rng.LegsPercent = v
                            };
            
                            var values = new Dictionary<string, int>
                            {
                                ["RNGHead"] = rng.HeadPercent,
                                ["RNGTorso"] = rng.TorsoPercent,
                                ["RNGArms"] = rng.ArmsPercent,
                                ["RNGLegs"] = rng.LegsPercent
                            };
            
                            foreach (var key in values.Keys.ToList())
                            {
                                if (key == tag) continue;
            
                                if (overflow == 0) break;
            
                                int reduceBy = Math.Min(values[key], overflow);
                                values[key] -= reduceBy;
                                overflow -= reduceBy;
                            }

                            foreach (var kv in values)
                                sliders[kv.Key](kv.Value);

                            sldrAimbotRNGHead.Value = rng.HeadPercent;
                            sldrAimbotRNGTorso.Value = rng.TorsoPercent;
                            sldrAimbotRNGArms.Value = rng.ArmsPercent;
                            sldrAimbotRNGLegs.Value = rng.LegsPercent;
                        }
            
                        break;
                    }
                    case "LTWZoom":
                        Config.MemWrites.LootThroughWalls.ZoomAmount = roundedValue;
                        break;
                    case "SilentLootDistance":
                        Config.MemWrites.SilentLoot.Distance = roundedValue;
                        break;
                    case "SilentLootMaxDistance":
                        Config.MemWrites.SilentLoot.MaxDistance = roundedValue;
                        break;
                    case "ExtendedReachDistance":
                        Config.MemWrites.ExtendedReach.Distance = roundedValue;
                        break;
                    case "TimeOfDayHour":
                        Config.MemWrites.TimeOfDay.Hour = intValue;
                        break;
                    case "FullBrightIntensity":
                        Config.MemWrites.FullBright.Intensity = floatValue;
                        break;
                    case "LeanAmt":
                        Config.MemWrites.WideLean.Amount = roundedValue;
                        break;
                    case "JumpMultiplier":
                        Config.MemWrites.LongJump.Multiplier = roundedValue;
                        break;
                    case "MoveSpeedMultiplier":
                        Config.MemWrites.MoveSpeed.Multiplier = floatValue;
                        break;
                    case "BigHeadScale":
                        Config.MemWrites.BigHead.Scale = floatValue;
                        break;
                    case "FOVBase":
                        Config.MemWrites.FOV.Base = intValue;
                        break;
                    case "ADSFOV":
                        Config.MemWrites.FOV.ADS = intValue;
                        break;
                    case "TPPFOV":
                        Config.MemWrites.FOV.ThirdPerson = intValue;
                        break;
                    case "ZoomFOV":
                        Config.MemWrites.FOV.InstantZoom = intValue;
                        break;
                }

                Config.Save();
            }
        }

        private void MemWritingRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string mode)
            {
                Config.MemWrites.Aimbot.TargetingMode = mode switch
                {
                    "FOV" => AimbotTargetingMode.FOV,
                    "CQB" => AimbotTargetingMode.CQB,
                    _ => Config.MemWrites.Aimbot.TargetingMode
                };

                Config.Save();
            }
        }

        private void MemWritingButton_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "NoRecoilPanel":
                        ToggleSettingsPanel(pnlNoRecoil);
                        break;
                    case "MoveSpeedPanel":
                        ToggleSettingsPanel(pnlMoveSpeed);
                        break;
                    case "WideLeanPanel":
                        ToggleSettingsPanel(pnlWideLean);
                        break;
                    case "LongJumpPanel":
                        ToggleSettingsPanel(pnlLongJump);
                        break;
                    case "TimeOfDayPanel":
                        ToggleSettingsPanel(pnlTimeOfDay);
                        break;
                    case "FullBrightPanel":
                        ToggleSettingsPanel(pnlFullBright);
                        break;
                    case "LTWPanel":
                        ToggleSettingsPanel(pnlLTW);
                        break;
                    case "SilentLootPanel":
                        ToggleSettingsPanel(pnlSilentLoot);
                        break;
                    case "ExtendedReachPanel":
                        ToggleSettingsPanel(pnlExtendedReach);
                        break;
                    case "FOVPanel":
                        ToggleSettingsPanel(pnlFOV);
                        break;
                    case "BigHeadsPanel":
                        ToggleSettingsPanel(pnlBigHeads);
                        break;
                }

                Config.Save();
                LoneLogging.WriteLine("Saved Convig");
            }
        }

        private void cboTargetBone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Config?.MemWrites?.Aimbot == null)
                return;

            if (cboTargetBone.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content is string boneName)
            {
                Config.MemWrites.Aimbot.Bone = boneName switch
                {
                    "Head" => Bones.HumanHead,
                    "Neck" => Bones.HumanNeck,
                    "Thorax" => Bones.HumanSpine3,
                    "Stomach" => Bones.HumanPelvis,
                    "Legs" => Bones.Legs,
                    _ => Bones.HumanHead
                };
                Config.Save();
            }
        }

        private void aimbotOptionsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAimbotOptions)
                return;

            foreach (CheckComboBoxItem item in ccbAimbotOptions.Items)
            {
                var option = item.Content.ToString();
                var isSelected = item.IsSelected;

                switch (option)
                {
                    case "Safe Lock":
                        Config.MemWrites.Aimbot.SilentAim.SafeLock = isSelected;
                        break;
                    case "Disable Re-Lock":
                        Config.MemWrites.Aimbot.DisableReLock = isSelected;
                        break;
                    case "Auto Bone":
                        Config.MemWrites.Aimbot.SilentAim.AutoBone = isSelected;
                        break;
                    case "Headshot AI":
                        Config.MemWrites.Aimbot.HeadshotAI = isSelected;
                        break;
                    case "Random Bone":
                        Config.MemWrites.Aimbot.RandomBone.Enabled = isSelected;
                        ToggleAimbotRandomBoneControls();
                        break;
                }
            }

            Config.Save();
            LoneLogging.WriteLine("Saved aimbot options settings");
        }

        private async void btnAntiAFK_Click(object sender, RoutedEventArgs e)
        {
            btnAntiAFK.Content = "Please Wait...";
            btnAntiAFK.IsEnabled = false;

            try
            {
                await Task.Run(() => // Run on non ui thread
                {
                    MemWriteFeature<AntiAfk>.Instance.Set();
                });

                NotificationsShared.Success("Anti-AFK is Set!\n\n NOTE: If you leave the Main Menu, you may need to re-set this.");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"ERROR Setting Anti-AFK! Your memory may be paged out, try close and re-open the game and try again.\n\n ${ex}");
            }
            finally
            {
                btnAntiAFK.Content = "Anti-AFK";
                btnAntiAFK.IsEnabled = true;
            }
        }

        private async void btnUnlockMaps_Click(object sender, RoutedEventArgs e)
        {
            btnUnlockMaps.IsEnabled = false;

            try
            {
                await Task.Run(() => // Run on non ui thread
                {
                    MemWriteFeature<UnlockMaps>.Instance.Set();
                });

                NotificationsShared.Success("All maps available!\n\n NOTE: If you leave the Main Menu, you may need to re-set this.");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"ERROR unlocking maps! Your memory may be paged out, try close and re-open the game and try again.\n\n ${ex}");
            }
            finally
            {
                btnUnlockMaps.IsEnabled = true;
            }
        }

        private async void btnGymHack_Click(object sender, RoutedEventArgs e)
        {
            btnGymHack.Content = "Please Wait...";
            btnGymHack.IsEnabled = false;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await MemPatchFeature<GymPatch>.Instance.Apply(cts.Token);
                NotificationsShared.Success("Gym Hack is Set! This will remain set until the game closes.");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"ERROR Setting Gym Hack! Make sure you started a workout in the 15 second window, otherwise your memory may be paged out.\n\nReopen your game and try again.\n\n {ex}");
            }
            finally
            {
                btnGymHack.Content = "Gym Hack";
                btnGymHack.IsEnabled = true;
            }
        }
        #endregion

        #endregion

        #region Config Import Handling
        public static class MemoryWritingImportHandler
        {
            public enum MemoryWritingDecision
            {
                DisableAll,
                EnableBasicOnly,
                EnableAll,
                KeepCurrent
            }

            /// <summary>
            /// Shows memory writing confirmation dialogs from the Memory Writing panel
            /// </summary>
            public static MemoryWritingDecision ShowMemoryWritingConfirmation(bool hasBasicMemWrites, bool hasAdvancedMemWrites)
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!hasBasicMemWrites)
                        return MemoryWritingDecision.KeepCurrent;

                    if (hasAdvancedMemWrites)
                    {
                        var advancedResult = MessageBox.Show(
                            "⚠️ ADVANCED MEMORY WRITING DETECTED ⚠️\n\n" +
                            "The configuration you're importing has Advanced Memory Writing features enabled.\n\n" +
                            "Advanced features include things such as:\n" +
                            "• Shellcode injection\n" +
                            "• Advanced chams (vischeck)\n" +
                            "• FOV changer\n" +
                            "• Disable Screen Effects (eg flash bangs etc)\n" +
                            "• Streamer Mode\n\n" +
                            "⚠️ WARNING: These features may carry a higher detection risk!\n\n" +
                            "Do you want to enable Advanced Memory Writing features?",
                            "Advanced Memory Writing Configuration",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (advancedResult == MessageBoxResult.Yes)
                        {
                            return MemoryWritingDecision.EnableAll;
                        }
                        else if (hasBasicMemWrites)
                        {
                            var basicResult = MessageBox.Show(
                                "Advanced Memory Writing has been disabled.\n\n" +
                                "However, this configuration also contains Basic Memory Writing features:\n\n" +
                                "• Aimbot, No Recoil, Infinite Stamina\n" +
                                "• Movement modifications (Speed, No Inertia, etc.)\n" +
                                "• Visual modifications (Night Vision, etc.)\n" +
                                "• And other game modifications\n\n" +
                                "⚠️ WARNING: These features still carry detection risk!\n\n" +
                                "Do you want to enable Basic Memory Writing features?",
                                "Basic Memory Writing Configuration",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            return basicResult == MessageBoxResult.Yes ? MemoryWritingDecision.EnableBasicOnly : MemoryWritingDecision.DisableAll;
                        }
                        else
                        {
                            return MemoryWritingDecision.DisableAll;
                        }
                    }
                    else if (hasBasicMemWrites)
                    {
                        var basicResult = MessageBox.Show(
                            "⚠️ MEMORY WRITING DETECTED ⚠️\n\n" +
                            "The configuration you're importing has Memory Writing features enabled.\n\n" +
                            "Memory writing features include:\n" +
                            "• Aimbot, No Recoil, Infinite Stamina\n" +
                            "• Movement modifications (Speed, No Inertia, etc.)\n" +
                            "• Visual modifications (Night Vision, etc.)\n" +
                            "• And other game modifications\n\n" +
                            "⚠️ WARNING: These features carry increased detection risk!\n\n" +
                            "Do you want to enable Memory Writing features?",
                            "Memory Writing Configuration",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        return basicResult == MessageBoxResult.Yes ? MemoryWritingDecision.EnableBasicOnly : MemoryWritingDecision.DisableAll;
                    }

                    return MemoryWritingDecision.KeepCurrent;
                });
            }

            /// <summary>
            /// Applies the memory writing decision to the imported config
            /// </summary>
            public static void ApplyMemoryWritingDecision(Config importedConfig, MemoryWritingDecision decision)
            {
                switch (decision)
                {
                    case MemoryWritingDecision.DisableAll:
                        importedConfig.MemWrites.MemWritesEnabled = false;
                        importedConfig.MemWrites.AdvancedMemWrites = false;
                        LoneLogging.WriteLine("[Config] User chose to disable all Memory Writing features during import");
                        NotificationsShared.Info("[Config] All Memory Writing features have been disabled. You can enable them later in the Memory Writing panel if needed.");
                        break;

                    case MemoryWritingDecision.EnableBasicOnly:
                        importedConfig.MemWrites.MemWritesEnabled = true;
                        importedConfig.MemWrites.AdvancedMemWrites = false;
                        LoneLogging.WriteLine("[Config] User chose to enable Basic Memory Writing features only during import");
                        NotificationsShared.Warning("[Config] Basic Memory Writing features are enabled. Advanced features have been disabled. Please be aware of the associated risks.");
                        break;

                    case MemoryWritingDecision.EnableAll:
                        importedConfig.MemWrites.MemWritesEnabled = true;
                        importedConfig.MemWrites.AdvancedMemWrites = true;
                        LoneLogging.WriteLine("[Config] User chose to keep all Memory Writing features enabled during import");
                        NotificationsShared.Warning("[Config] All Memory Writing features including Advanced features are enabled. Please be aware of the significant risks associated with these features.");
                        break;

                    case MemoryWritingDecision.KeepCurrent:
                        break;
                }
            }
        }

        /// <summary>
        /// Called from GeneralSettingsControl when importing config with memory writing features
        /// </summary>
        public static MemoryWritingImportHandler.MemoryWritingDecision HandleConfigImportMemoryWriting(Config importedConfig)
        {
            var hasBasicMemWrites = importedConfig.MemWrites.MemWritesEnabled;
            var hasAdvancedMemWrites = importedConfig.MemWrites.AdvancedMemWrites;

            return MemoryWritingImportHandler.ShowMemoryWritingConfirmation(hasBasicMemWrites, hasAdvancedMemWrites);
        }

        /// <summary>
        /// Shows confirmation when user manually enables memory writing features
        /// </summary>
        public bool ConfirmMemoryWritingEnable(bool isAdvanced = false)
        {
            return Dispatcher.Invoke(() =>
            {
                if (isAdvanced)
                {
                    var result = MessageBox.Show(
                        "⚠️ ENABLING ADVANCED MEMORY WRITING ⚠️\n\n" +
                        "You are about to enable Advanced Memory Writing features.\n\n" +
                        "Advanced features include things such as:\n" +
                        "• Shellcode injection\n" +
                        "• Advanced chams (vischeck)\n" +
                        "• FOV changer\n" +
                        "• Disable Screen Effects (eg flash bangs etc)\n" +
                        "• Streamer Mode\n\n" +
                        "⚠️ WARNING: These features may carry a higher detection risk!\n\n" +
                        "Are you sure you want to enable Advanced Memory Writing?",
                        "Advanced Memory Writing Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    return result == MessageBoxResult.Yes;
                }
                else
                {
                    var result = MessageBox.Show(
                        "⚠️ ENABLING MEMORY WRITING ⚠️\n\n" +
                        "You are about to enable Memory Writing features.\n\n" +
                        "Memory writing features include:\n" +
                        "• Aimbot, No Recoil, Infinite Stamina\n" +
                        "• Movement modifications (Speed, No Inertia, etc.)\n" +
                        "• Visual modifications (Night Vision, etc.)\n" +
                        "• And other game modifications\n\n" +
                        "⚠️ WARNING: These features carry increased detection risk!\n\n" +
                        "Are you sure you want to enable Memory Writing?",
                        "Memory Writing Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    return result == MessageBoxResult.Yes;
                }
            });
        }
        #endregion
    }
}