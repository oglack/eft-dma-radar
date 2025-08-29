using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.UI.Misc;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using HelixToolkit.Wpf;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using eft_dma_shared.Common.Misc;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using eft_dma_shared.Common.Unity.LowLevel.Chams;

namespace eft_dma_radar.UI.Pages
{
    public partial class PlayerPreviewControl : UserControl
    {
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;
        private bool _isRotating = false;
        private Point _dragStartPoint;
        private Point _lastMousePos;
        private Point3D footLeft;
        private AxisAngleRotation3D _rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        private RotateTransform3D _rotateTransform;
        public static PlayerPreviewControl Instance { get; private set; }
        private const int INTERVAL = 1000;
        private SKColor EspColor;

        public PlayerPreviewControl()
        {
            InitializeComponent();
            Loaded += PlayerPreviewControl_Loaded;
            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                    await Task.Delay(INTERVAL);

                Instance = this;

                PanelCoordinator.Instance.SetPanelReady("PlayerPreview");

                try
                {
                    await PanelCoordinator.Instance.WaitForAllPanelsAsync();

                    cmbEspPlayerType.SelectionChanged += cmbEspPlayerType_SelectionChanged;
                }
                catch (TimeoutException ex)
                {
                    LoneLogging.WriteLine($"[PANELS] {ex.Message}");
                }
            };
        }

        public static void RefreshPreview() => Instance?.Redraw(null, EventArgs.Empty);

        private void PlayerPreviewControl_Loaded(object sender, RoutedEventArgs e)
        {
            
            cmbChamsMode.SelectionChanged += Redraw;
            cmbPlayerType.SelectionChanged += Redraw;
            cmbDisplayMode.SelectionChanged += Redraw;

            cmbChamsMode.ItemsSource = new[]
            {
                ChamsMode.Basic,
                ChamsMode.VisCheckGlow,
                ChamsMode.Visible,
                ChamsMode.VisCheckFlat,
                ChamsMode.Aimbot
            };
            cmbPlayerType.ItemsSource = Enum.GetValues(typeof(ChamsEntityType));

            cmbChamsMode.SelectedIndex = 0;
            cmbPlayerType.SelectedItem = ChamsEntityType.PMC;

            viewport3D.MouseLeftButtonDown += Viewport3D_MouseLeftButtonDown;
            viewport3D.MouseMove += Viewport3D_MouseMove;
            viewport3D.MouseLeftButtonUp += Viewport3D_MouseLeftButtonUp;
            viewport3D.Cursor = Cursors.Hand;
            Redraw(null, null);
            cmbEspType.SelectionChanged += (_, _) => EspRedraw(null, null);
            cmbEspPlayerType.SelectedItem = cmbEspPlayerType.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == "USEC");

            cmbEspPlayerType_SelectionChanged(cmbEspPlayerType, null); // Force trigger            
        }

        private void Redraw(object sender, EventArgs e)
        {
            //if (cmbPlayerType.SelectedItem is ChamsEntityType type && Chams.Config.EntityChams.TryGetValue(type, out var settings))
            //{
            //    var visibleColor = SKColor.Parse(settings.VisibleColor);
            //    var invisibleColor = SKColor.Parse(settings.InvisibleColor);
            //    Load3DModel("SoldierVis.obj",
            //        Color.FromArgb(visibleColor.Alpha, visibleColor.Red, visibleColor.Green, visibleColor.Blue),
            //        Color.FromArgb(invisibleColor.Alpha, invisibleColor.Red, invisibleColor.Green, invisibleColor.Blue));
            //}
        }

        private void GetChamsColors(out SKColor visible, out SKColor invisible)
        {
            visible = SKColors.Green;
            invisible = SKColors.Red;

            if (cmbChamsMode.SelectedItem is ChamsMode && cmbPlayerType.SelectedItem is ChamsEntityType type)
            {
                if (Chams.Config.EntityChams.TryGetValue(type, out var settings))
                {
                    try
                    {
                        visible = SKColor.Parse(settings.VisibleColor);
                        invisible = SKColor.Parse(settings.InvisibleColor);
                    }
                    catch { }
                }
            }
        }

        private void Load3DModel(string path, Color visibleColor, Color invisibleColor)
        {
            //viewport3D.Children.Clear();
//
            //var reader = new ObjReader();
            //var visibleModel = new ObjReader().Read("SoldierVis.obj");
            //var invisibleModel = new ObjReader().Read("SoldierInVis.obj");
//
            //if (visibleModel == null || invisibleModel == null)
            //{
            //    HandyControl.Controls.MessageBox.Show("One or both OBJ models could not be loaded.");
            //    return;
            //}
//
            //var displayMode = (cmbDisplayMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Both";
            //var chamsMode = cmbChamsMode.SelectedItem is ChamsMode m ? m : ChamsMode.Basic;
//
            //_rotateTransform = new RotateTransform3D(_rotation);
            //var transformGroup = new Transform3DGroup
            //{
            //    Children = new Transform3DCollection
            //    {
            //        new ScaleTransform3D(0.33, 0.33, 0.33),
            //        _rotateTransform
            //    }
            //};
            //visibleModel.Transform = transformGroup;
            //invisibleModel.Transform = transformGroup;
//
            //// Determine color application
            //if(cmbChamsMode.SelectedItem is ChamsMode.Visible)
            //{
            //    ApplyMaterialToModel(visibleModel, visibleColor, chamsMode, isInvisible: false);
            //    ApplyMaterialToModel(invisibleModel, visibleColor, chamsMode, isInvisible: true);
            //}
            //else if (displayMode == "Both")
            //{
            //    ApplyMaterialToModel(visibleModel, visibleColor, chamsMode, isInvisible: false);
            //    ApplyMaterialToModel(invisibleModel, invisibleColor, chamsMode, isInvisible: true);
            //}
            //else if (displayMode == "Visible")
            //{
            //    ApplyMaterialToModel(visibleModel, visibleColor, chamsMode, isInvisible: false);
            //    ApplyMaterialToModel(invisibleModel, visibleColor, chamsMode, isInvisible: false);
            //}
            //else if (displayMode == "Invisible")
            //{
            //    ApplyMaterialToModel(visibleModel, invisibleColor, chamsMode, isInvisible: true);
            //    ApplyMaterialToModel(invisibleModel, invisibleColor, chamsMode, isInvisible: true);
            //}
//
            //// Lighting
            //viewport3D.Children.Add(new ModelVisual3D
            //{
            //    Content = new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2))
            //});
//
            //// Always add both models
            //viewport3D.Children.Add(new ModelVisual3D { Content = invisibleModel });
            //viewport3D.Children.Add(new ModelVisual3D { Content = visibleModel });
//
            //_rotation.Angle = 180;
        }
        private void ApplyMaterialToModel(Model3DGroup model, Color color, ChamsMode mode, bool isInvisible)
        {
            Material mat = mode switch
            {
                ChamsMode.Basic => new DiffuseMaterial(new SolidColorBrush(Colors.White)),
        
                ChamsMode.Visible => new DiffuseMaterial(new SolidColorBrush(color)),
        
                ChamsMode.VisCheckFlat => new DiffuseMaterial(new SolidColorBrush(color)),
        
                ChamsMode.VisCheckGlow => new MaterialGroup
                {
                    Children = new MaterialCollection
                    {
                        new DiffuseMaterial(new SolidColorBrush(Darken(color, 0.3))),
                        new EmissiveMaterial(new SolidColorBrush(Darken(color, 0.15)))
                    }
                },
        
                _ => new DiffuseMaterial(new SolidColorBrush(color))
            };
        
            foreach (var geometry in model.Children.OfType<GeometryModel3D>())
            {
                geometry.Material = mat;
                geometry.BackMaterial = mat;
            }
        }

        private Color Darken(Color color, double factor)
        {
            return Color.FromArgb(
                color.A,
                (byte)(color.R * factor),
                (byte)(color.G * factor),
                (byte)(color.B * factor)
            );
        }
        private void DrawWireframe(Model3DGroup model, Color color)
        {
            var lines = new LinesVisual3D
            {
                Color = color,
                Thickness = 0.5
            };
        
            foreach (var geom in model.Children.OfType<GeometryModel3D>())
            {
                if (geom.Geometry is MeshGeometry3D mesh)
                {
                    var transform = geom.Transform ?? Transform3D.Identity;
        
                    for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
                    {
                        var p0 = transform.Transform(mesh.Positions[mesh.TriangleIndices[i]]);
                        var p1 = transform.Transform(mesh.Positions[mesh.TriangleIndices[i + 1]]);
                        var p2 = transform.Transform(mesh.Positions[mesh.TriangleIndices[i + 2]]);
        
                        lines.Points.Add(p0); lines.Points.Add(p1);
                        lines.Points.Add(p1); lines.Points.Add(p2);
                        lines.Points.Add(p2); lines.Points.Add(p0);
                    }
                }
            }
        
            viewport3D.Children.Add(lines);
        }


        #region ESPTab

        private void EspRedraw(object sender, EventArgs e)
        {
            viewportEsp3D.Children.Clear();

            viewportEsp3D.Children.Add(new ModelVisual3D
            {
                Content = new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2))
            });

            var espType = (cmbEspType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Box";

            if (espType == "Box")
                DrawPreviewBox();
            else if (espType == "Skeleton")
                AddSkeletonLines();

            AddSelectedFeatures();
        }

        private void DrawPreviewBox()
        {
            // Flat 2D box: width = 1, height = 2, Z = fixed depth
            var bounds = new Rect(0, 0, 1, 2);
            AddFlatESPBoxOverlay(bounds);
        }

        private void AddFlatESPBoxOverlay(Rect bounds)
        {
            var lines = new LinesVisual3D
            {
                Color = Color.FromArgb(EspColor.Alpha, EspColor.Red, EspColor.Green, EspColor.Blue),
                Thickness = 1.5
            };

            double z = 0; // keep it in flat 2D plane (front)

            var p = new[]
            {
                new Point3D(bounds.Left - 0.5, bounds.Top, z),
                new Point3D(bounds.Right - 0.5, bounds.Top, z),
                new Point3D(bounds.Right - 0.5, bounds.Bottom, z),
                new Point3D(bounds.Left - 0.5, bounds.Bottom, z),
            };

            void AddEdge(int i, int j)
            {
                lines.Points.Add(p[i]);
                lines.Points.Add(p[j]);
            }

            // Outline (rectangle)
            AddEdge(0, 1);
            AddEdge(1, 2);
            AddEdge(2, 3);
            AddEdge(3, 0);

            // Transform it like everything else
            lines.Transform = new Transform3DGroup
            {
                Children = new Transform3DCollection
                {
                    new ScaleTransform3D(0.6, 0.6, 0.6),
                    _rotateTransform,
                    new TranslateTransform3D(0, 0.4, -1.2)
                }
            };

            viewportEsp3D.Children.Add(lines);
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is HandyControl.Controls.TabControl tabControl &&
                tabControl.SelectedItem is TabItem selectedTab)
            {
                if ((selectedTab.Header as string) == "ESP")
                {
                    EspRedraw(null, null);
                }
                else if ((selectedTab.Header as string) == "Chams")
                {
                    Redraw(null, null);
                }
            }
        }

        private void AddSkeletonLines()
        {
            var lines = new LinesVisual3D
            {
                Color = Color.FromArgb(EspColor.Alpha, EspColor.Red, EspColor.Green, EspColor.Blue),
                Thickness = 2.0
            };

            // Core body
            var head = new Point3D(0, 2.7, 0);
            var neck = new Point3D(0, 2.3, 0);
            var spine = new Point3D(0, 1.8, 0);
            var pelvis = new Point3D(0, 1.0, 0);

            // Arms angled downward
            var shoulderLeft = new Point3D(-0.4, 2.2, 0);
            var elbowLeft = new Point3D(-0.6, 1.8, 0);
            var handLeft = new Point3D(-0.45, 1.4, 0);

            var shoulderRight = new Point3D(0.4, 2.2, 0);
            var elbowRight = new Point3D(0.6, 1.8, 0);
            var handRight = new Point3D(0.45, 1.4, 0);

            // Legs slightly angled from pelvis
            var hipLeft = new Point3D(-0.3, 0.9, 0);
            var kneeLeft = new Point3D(-0.4, 0.5, 0);
            footLeft = new Point3D(-0.3, 0.0, 0);

            var hipRight = new Point3D(0.3, 0.9, 0);
            var kneeRight = new Point3D(0.4, 0.5, 0);
            var footRight = new Point3D(0.3, 0.0, 0);

            // Spine
            lines.Points.Add(head); lines.Points.Add(neck);
            lines.Points.Add(neck); lines.Points.Add(spine);
            lines.Points.Add(spine); lines.Points.Add(pelvis);

            // Arms
            lines.Points.Add(neck); lines.Points.Add(shoulderLeft);
            lines.Points.Add(shoulderLeft); lines.Points.Add(elbowLeft);
            lines.Points.Add(elbowLeft); lines.Points.Add(handLeft);

            lines.Points.Add(neck); lines.Points.Add(shoulderRight);
            lines.Points.Add(shoulderRight); lines.Points.Add(elbowRight);
            lines.Points.Add(elbowRight); lines.Points.Add(handRight);

            // Legs
            lines.Points.Add(pelvis); lines.Points.Add(hipLeft);
            lines.Points.Add(hipLeft); lines.Points.Add(kneeLeft);
            lines.Points.Add(kneeLeft); lines.Points.Add(footLeft);

            lines.Points.Add(pelvis); lines.Points.Add(hipRight);
            lines.Points.Add(hipRight); lines.Points.Add(kneeRight);
            lines.Points.Add(kneeRight); lines.Points.Add(footRight);

            var transformGroup = new Transform3DGroup
            {
                Children = new Transform3DCollection
                {
                    new ScaleTransform3D(0.6, 0.6, 0.6),
                    _rotateTransform,
                    new TranslateTransform3D(0, 0.4, -1.2)
                }
            };
            lines.Transform = transformGroup;

            viewportEsp3D.Children.Add(lines);
        }


        private void AddSelectedFeatures()
        {
            var features = lstEspFeatures.ItemsSource?.Cast<string>()?.ToList();
            if (features == null || features.Count == 0) return;
        
            string name = features.FirstOrDefault(f => f.Contains("Name"));
            string weapon = features.FirstOrDefault(f => f.Contains("Weapon"));
            string distance = features.FirstOrDefault(f => f.Contains("Distance"));
        
            string line1 = "";
            if (!string.IsNullOrEmpty(name))
            {
                line1 = name.Replace(" Name", ""); // e.g., "Boss Name" → "Boss"
                if (!string.IsNullOrEmpty(distance))
                    line1 += " (75m)"; // Replace with real distance if needed
            }
        
            string line2 = "";
            if (!string.IsNullOrEmpty(weapon))
                line2 = "AK-47"; // Replace with actual weapon name
        
            // Label position should be just *above* the foot
            double baseY = footLeft.Y + 0.1;
        
            if (!string.IsNullOrEmpty(line2))
            {
                viewportEsp3D.Children.Add(new BillboardTextVisual3D
                {
                    Position = new Point3D(0, baseY, 0),
                    Text = line2,
                    Foreground = new SolidColorBrush(Color.FromArgb(EspColor.Alpha, EspColor.Red, EspColor.Green, EspColor.Blue)),
                    Background = Brushes.Transparent
                });
                baseY += 0.2;
            }
        
            if (!string.IsNullOrEmpty(line1))
            {
                viewportEsp3D.Children.Add(new BillboardTextVisual3D
                {
                    Position = new Point3D(0, baseY, 0),
                    Text = line1,
                    Foreground = new SolidColorBrush(Color.FromArgb(EspColor.Alpha, EspColor.Red, EspColor.Green, EspColor.Blue)),
                    Background = Brushes.Transparent
                });
            }
        }

        public static void RefreshESPPreview()
        {
            if (Instance == null) 
            {
                LoneLogging.WriteLine("[PANELS] PlayerPreviewControl instance is null.");
                return;
            }
            else
                Instance.cmbEspPlayerType_SelectionChanged(Instance.cmbEspPlayerType, null);
        }

        private void cmbEspPlayerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainWindow.Config == null || cmbEspPlayerType.SelectedItem is not ComboBoxItem selected) return;

            var tag = selected.Tag?.ToString();
            var features = new List<string>();
            //switch (tag)
            //{
            //    case "USEC":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Name Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintUSEC.Color;
            //        break;
            //    case "BEAR":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Name Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintBEAR.Color;
            //        break;
            //    case "Boss":
            //        if (_config.ESP.AIRendering.ShowLabels) features.Add("Boss Name");
            //        if (_config.ESP.AIRendering.ShowWeapons) features.Add("Boss Weapon");
            //        if (_config.ESP.AIRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintBoss.Color;
            //        break;
            //    case "Teammate":
            //        features.Add("Always Visible");
            //        EspColor =  SKPaints.PaintTeammate.Color;
            //        break;
            //    case "Scav":
            //        if (_config.ESP.AIRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.AIRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.AIRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintScav.Color;
            //        break;
            //    case "Focused":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintFocused.Color;
            //        break;
            //    case "Streamer":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintStreamer.Color;
            //        break;
            //    case "AimbotTarget":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintAimbotLocked.Color;
            //        break;
            //    case "Special":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintSpecial.Color;
            //        break;
            //    case "PlayerScav":
            //        if (_config.ESP.PlayerRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.PlayerRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.PlayerRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintPlayerScavESP.Color;
            //        break;
            //    case "Raider":
            //        if (_config.ESP.AIRendering.ShowLabels) features.Add("Scav Tag");
            //        if (_config.ESP.AIRendering.ShowWeapons) features.Add("Weapon");
            //        if (_config.ESP.AIRendering.ShowDist) features.Add("Distance");
            //        EspColor =  SKPaints.PaintRaider.Color;
            //        break;
            //}

            //lstEspFeatures.ItemsSource = features;
            //lstEspFeatures.Items.Refresh();
            //EspRedraw(null, null);
        }
        private void Viewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isRotating = true;
            _lastMousePos = e.GetPosition((IInputElement)sender);
            ((UIElement)sender).CaptureMouse();
        }

        private void Viewport3D_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isRotating && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition((IInputElement)sender);
                double deltaX = pos.X - _lastMousePos.X;
                _rotation.Angle += deltaX * 0.5; // adjust rotation speed as needed
                _lastMousePos = pos;
            }
        }

        private void Viewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isRotating = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        #endregion

        #region Events
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

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
                var offset = e.GetPosition(this) - _dragStartPoint;
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
    }
}
