using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Data;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

public partial class ContentView : UserControl
{
    private bool pulsing;
    private bool wide;
    private readonly Dictionary<Panel, UIElement[]> columnized = [];
    private IInputElement? folderFocusReturnTarget, sheetFocusReturnTarget;
    private string? folderFocusReturnId, sheetFocusReturnId;
    private bool folderViewportFrozen;
    private bool galleryTransitioning;
    private int galleryTransitionVersion;
    private string? pendingGallerySelection;
    private WidgetSlot? pendingGalleryPreview;
    private int pendingGalleryDirection;
    private bool pendingGalleryCardMotion;
    private StackPanel galleryAddButtonContent = null!;
    private TranslateTransform galleryAddButtonShift = null!;
    private Border galleryPreviousVisual = null!, galleryCenterVisual = null!, galleryNextVisual = null!;
    private sealed class GalleryMotionCard(Border frame, Viewport2DVisual3D visual, AxisAngleRotation3D rotation,
        RotateTransform3D orbit, ScaleTransform3D scale, TranslateTransform3D translation, MeshGeometry3D mesh, double radius)
    {
        public Border Frame { get; } = frame;
        public Viewport2DVisual3D Visual { get; } = visual;
        public AxisAngleRotation3D Rotation { get; } = rotation;
        public RotateTransform3D Orbit { get; } = orbit;
        public ScaleTransform3D Scale { get; } = scale;
        public TranslateTransform3D Translation { get; } = translation;
        public MeshGeometry3D Mesh { get; } = mesh;
        public double Radius { get; set; } = radius;
    }
    private GalleryMotionCard[] galleryMotionCards = [];
    private bool galleryMotionSceneReady, galleryMotionSceneUpdating;
    private const double GallerySideAngle = 32;
    private const double GallerySideScale = 0.92;

    /// En el dock ancho, cada vista se parte en dos columnas: lo marcado con esto y todo lo que le sigue pasa a la segunda.
    public static readonly DependencyProperty BreakBeforeProperty =
        DependencyProperty.RegisterAttached("BreakBefore", typeof(bool), typeof(ContentView), new PropertyMetadata(false));
    public static bool GetBreakBefore(DependencyObject d) => (bool)d.GetValue(BreakBeforeProperty);
    public static void SetBreakBefore(DependencyObject d, bool value) => d.SetValue(BreakBeforeProperty, value);

    public ContentView(PanelViewModel model)
    {
        InitializeComponent();
        galleryPreviousVisual = GalleryPreviousFrame;
        galleryCenterVisual = GalleryPreviewFrame;
        galleryNextVisual = GalleryNextFrame;
        DataContext = model;
        model.ChatLines.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(ChatScroll.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
        PropertyChangedEventManager.AddHandler(model, OnModelChanged, string.Empty);
        GalleryViewportHost.SizeChanged += (_, _) => { if (galleryMotionSceneReady) UpdateGalleryMotionScene(); else EnsureGalleryMotionScene(); };
        GalleryMotionViewport.SizeChanged += (_, _) => { if (galleryMotionSceneReady) UpdateGalleryMotionScene(); else EnsureGalleryMotionScene(); };
        foreach (var frame in new[] { galleryPreviousVisual, galleryCenterVisual, galleryNextVisual })
            frame.SizeChanged += (_, _) => { if (galleryMotionSceneReady) UpdateGalleryMotionScene(); };
        IsVisibleChanged += (_, _) => model.SetPanelVisible(IsVisible);
        Loaded += (_, _) =>
        {
            model.GallerySelectionRequested += AnimateGallerySelection;
            model.HomePageChanged += SlideHomePage;
            GalleryAddButton.ApplyTemplate();
            galleryAddButtonContent = (StackPanel)GalleryAddButton.Template.FindName("GalleryAddButtonContent", GalleryAddButton);
            galleryAddButtonShift = (TranslateTransform)GalleryAddButton.Template.FindName("GalleryAddButtonShift", GalleryAddButton);
            EnsureGalleryMotionScene();
        };
        Unloaded += (_, _) =>
        {
            model.GallerySelectionRequested -= AnimateGallerySelection;
            model.HomePageChanged -= SlideHomePage;
            CancelGalleryTransition(commitPending: true);
            ResetGalleryCopyAnimations();
        };
        ApplyWide(model.WideLayout);
    }

    /// Al pasar de página el contenido entra deslizándose desde el lado al que se va, con un fundido; sin animaciones del sistema, cambia sin más.
    private void SlideHomePage(int direction)
    {
        if (DataContext is not PanelViewModel model || !IsLoaded || model.ReducedMotionOn || !SystemParameters.ClientAreaAnimation) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(direction * 56, 0);
        HomeItems.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        HomeItems.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    /// En la ventana de la app la cabecera sobra: la ventana ya tiene título y su propia navegación.
    public void HideHeader() => HeaderBar.Visibility = HeaderDivider.Visibility = Visibility.Collapsed;
    public void ScrollToTop()
    {
        BodyScroll.ScrollToTop();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(BodyScroll.ScrollToTop));
    }

    private void AnimateGallerySelection(string id, int direction)
    {
        if (DataContext is not PanelViewModel model || galleryTransitioning) return;
        bool next = direction > 0 && model.GalleryNextCard?.Slot.Id == id;
        bool previous = direction < 0 && model.GalleryPreviousCard?.Slot.Id == id;
        var incoming = next ? model.GalleryNextCard?.Slot : previous ? model.GalleryPreviousCard?.Slot : null;
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            model.CommitGallerySelection(id);
            BindGalleryCardRoles(model);
            UpdateGalleryMotionScene();
            return;
        }

        EnsureGalleryMotionScene();
        if (!galleryMotionSceneReady)
        {
            model.CommitGallerySelection(id, incoming);
            BindGalleryCardRoles(model);
            return;
        }

        if (!next && !previous)
        {
            BeginGalleryCopyOnlySelection(id);
            return;
        }

        galleryTransitioning = true;
        pendingGallerySelection = id;
        pendingGalleryPreview = incoming;
        pendingGalleryDirection = direction;
        pendingGalleryCardMotion = true;
        int version = ++galleryTransitionVersion;
        GalleryMotionViewport.IsHitTestVisible = false;
        var duration = TimeSpan.FromMilliseconds(520);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var step = -Math.Sign(direction) * GallerySideAngle;
        double distance = CameraDistance();
        var incomingFrame = next ? galleryNextVisual : galleryPreviousVisual;
        double previousRadius = GalleryRadius(galleryPreviousVisual, distance);
        double nextRadius = GalleryRadius(galleryNextVisual, distance);
        foreach (var card in galleryMotionCards)
        {
            double targetAngle = card.Rotation.Angle + step;
            if (ReferenceEquals(card.Frame, galleryCenterVisual))
                SetGalleryOrbit(card, direction > 0 ? previousRadius : nextRadius);
            double targetRoleScale = ReferenceEquals(card.Frame, incomingFrame) ? 1 : GallerySideScale;
            double widthRatio = 1, heightRatio = 1;
            if (ReferenceEquals(card.Frame, galleryCenterVisual))
            {
                var current = GalleryFrameContent(card.Frame).Content as WidgetSlot;
                if (current is not null)
                {
                    var minimum = WidgetLayout.Sizes(current.Id).OrderBy(size => size.Cols * size.Rows).ThenBy(size => size.Rows).First();
                    widthRatio = minimum.Cols * GridPanel.CellWidth / Math.Max(1, card.Frame.ActualWidth);
                    heightRatio = minimum.Rows * GridPanel.RowHeight / Math.Max(1, card.Frame.ActualHeight);
                }
            }
            double scale = GalleryMotionScale(targetAngle, card.Radius, distance, targetRoleScale);
            double targetY = GalleryVerticalOffset(card.Frame, targetAngle, card.Radius, distance);
            var angleAnimation = new DoubleAnimation
            {
                From = card.Rotation.Angle,
                To = targetAngle,
                Duration = new Duration(duration),
                EasingFunction = easing,
            };
            if (ReferenceEquals(card.Frame, incomingFrame))
                angleAnimation.Completed += (_, _) => CompleteGalleryTransition(version);
            card.Rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, angleAnimation, HandoffBehavior.SnapshotAndReplace);
            Animate(card.Scale, ScaleTransform3D.ScaleXProperty, scale * widthRatio, duration, easing, card.Scale.ScaleX);
            Animate(card.Scale, ScaleTransform3D.ScaleYProperty, scale * heightRatio, duration, easing, card.Scale.ScaleY);
            Animate(card.Translation, TranslateTransform3D.OffsetYProperty, targetY, duration, easing, card.Translation.OffsetY);
        }
        AnimateGalleryCopyOut(duration, alignWithCarousel: true);
    }

    private void BeginGalleryCopyOnlySelection(string id)
    {
        galleryTransitioning = true;
        pendingGallerySelection = id;
        pendingGalleryPreview = null;
        pendingGalleryDirection = 0;
        pendingGalleryCardMotion = false;
        int version = ++galleryTransitionVersion;
        GalleryMotionViewport.IsHitTestVisible = false;
        AnimateGalleryCopyOut(TimeSpan.FromMilliseconds(150), alignWithCarousel: false,
            (_, _) => CompleteGalleryCopyOnlySelection(version));
    }

    private void AnimateGalleryCopyOut(TimeSpan duration, bool alignWithCarousel, EventHandler? completed = null)
    {
        var titleDuration = TimeSpan.FromMilliseconds(alignWithCarousel ? 170 : 140);
        var descriptionDuration = TimeSpan.FromMilliseconds(alignWithCarousel ? 150 : 150);
        var actionDuration = TimeSpan.FromMilliseconds(alignWithCarousel ? 190 : 130);
        var titleDelay = alignWithCarousel ? duration - titleDuration : TimeSpan.Zero;
        var descriptionDelay = alignWithCarousel ? duration - descriptionDuration : TimeSpan.Zero;
        var actionDelay = alignWithCarousel ? duration - actionDuration - TimeSpan.FromMilliseconds(18) : TimeSpan.Zero;
        AnimateGalleryElementOut(GalleryWidgetTitle, GalleryWidgetTitleShift, -6, titleDuration, titleDelay);
        AnimateGalleryElementOut(GalleryWidgetDescription, GalleryWidgetDescriptionShift, -5, descriptionDuration, descriptionDelay, completed);
        AnimateGalleryElementOut(galleryAddButtonContent, galleryAddButtonShift, 5, actionDuration, actionDelay);
    }

    private static void AnimateGalleryElementOut(FrameworkElement element, TranslateTransform shift, double offset,
        TimeSpan duration, TimeSpan delay, EventHandler? completed = null)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(element.Opacity, 0, new Duration(duration)) { BeginTime = delay, EasingFunction = easing };
        var slide = new DoubleAnimation(shift.Y, offset, new Duration(duration)) { BeginTime = delay, EasingFunction = easing };
        if (completed is not null) fade.Completed += completed;
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        shift.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateGalleryCopyIn()
    {
        AnimateGalleryElementIn(GalleryWidgetTitle, GalleryWidgetTitleShift, 6, TimeSpan.Zero, 210);
        AnimateGalleryElementIn(GalleryWidgetDescription, GalleryWidgetDescriptionShift, 5, TimeSpan.FromMilliseconds(35), 190);
        AnimateGalleryElementIn(galleryAddButtonContent, galleryAddButtonShift, 5, TimeSpan.FromMilliseconds(18), 205);
    }

    private static void AnimateGalleryElementIn(FrameworkElement element, TranslateTransform shift, double offset,
        TimeSpan delay, double durationMs)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        element.Opacity = 0;
        shift.Y = offset;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = delay,
            EasingFunction = easing,
        }, HandoffBehavior.SnapshotAndReplace);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = delay,
            EasingFunction = easing,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void ResetGalleryCopyAnimations()
    {
        foreach (var (element, shift) in new (FrameworkElement, TranslateTransform)[]
        {
            (GalleryWidgetTitle, GalleryWidgetTitleShift),
            (GalleryWidgetDescription, GalleryWidgetDescriptionShift),
            (galleryAddButtonContent, galleryAddButtonShift),
        })
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            element.Opacity = 1;
            shift.Y = 0;
        }
    }

    private void EnsureGalleryMotionScene()
    {
        if (galleryMotionSceneReady || !IsLoaded || DataContext is not PanelViewModel model) return;
        GalleryViewportHost.UpdateLayout();
        GalleryMotionViewport.UpdateLayout();
        if (GalleryMotionViewport.ActualWidth <= 0 || GalleryMotionViewport.ActualHeight <= 0) return;
        var frames = new[] { galleryPreviousVisual, galleryCenterVisual, galleryNextVisual };
        if (frames.Any(frame => frame.ActualWidth <= 0 || frame.ActualHeight <= 0)) return;

        BindGalleryCardRoles(model);
        GalleryMotionViewport.Children.Clear();
        GalleryMotionViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        foreach (var frame in frames)
        {
            GalleryCarouselCanvas.Children.Remove(frame);
            frame.RenderTransform = Transform.Identity;
            frame.RenderTransformOrigin = new Point(0.5, 0.5);
            frame.Opacity = 1;
            var card = CreateGalleryMotionCard(frame, GalleryRoleAngle(frame));
            galleryMotionCards = [.. galleryMotionCards, card];
            GalleryMotionViewport.Children.Add(card.Visual);
        }
        galleryMotionSceneReady = true;
        GalleryMotionViewport.Visibility = Visibility.Visible;
        GalleryMotionViewport.Opacity = 1;
        GalleryMotionViewport.IsHitTestVisible = true;
        UpdateGalleryMotionScene();
    }

    private GalleryMotionCard CreateGalleryMotionCard(Border source, double angle)
    {
        double distance = CameraDistance();
        double fov = GalleryMotionViewport.Camera is PerspectiveCamera camera ? camera.FieldOfView : 32;
        double unitPerDip = 2 * distance * Math.Tan(fov * Math.PI / 360) / GalleryMotionViewport.ActualWidth;
        double radius = ReferenceEquals(source, galleryCenterVisual)
            ? (GalleryRadius(galleryPreviousVisual, distance) + GalleryRadius(galleryNextVisual, distance)) / 2
            : GalleryRadius(source, distance);
        var material = new DiffuseMaterial(Brushes.White);
        Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
        var mesh = GalleryCardMesh(new Size(source.ActualWidth, source.ActualHeight), unitPerDip);
        var axis = new AxisAngleRotation3D(new Vector3D(0, 1, 0), angle);
        var orbit = new RotateTransform3D(axis, 0, 0, -radius);
        double scale = GalleryMotionScale(angle, radius, distance, ReferenceEquals(source, galleryCenterVisual) ? 1 : GallerySideScale);
        var translation = new TranslateTransform3D(0, GalleryVerticalOffset(source, angle, radius, distance), 0);
        var transforms = new Transform3DGroup();
        var scaleTransform = new ScaleTransform3D(scale, scale, 1);
        transforms.Children.Add(scaleTransform);
        transforms.Children.Add(orbit);
        transforms.Children.Add(translation);
        var visual = new Viewport2DVisual3D
        {
            Visual = source,
            Geometry = mesh,
            Material = material,
            Transform = transforms,
        };
        return new(source, visual, axis, orbit, scaleTransform, translation, mesh, radius);
    }

    private static MeshGeometry3D GalleryCardMesh(Size size, double unitPerDip) => new()
    {
        Positions = new Point3DCollection
        {
            new(-size.Width * unitPerDip / 2, size.Height * unitPerDip / 2, 0),
            new(size.Width * unitPerDip / 2, size.Height * unitPerDip / 2, 0),
            new(size.Width * unitPerDip / 2, -size.Height * unitPerDip / 2, 0),
            new(-size.Width * unitPerDip / 2, -size.Height * unitPerDip / 2, 0),
        },
        TextureCoordinates = new PointCollection { new(0, 0), new(1, 0), new(1, 1), new(0, 1) },
        TriangleIndices = new Int32Collection { 0, 2, 1, 0, 3, 2 },
    };

    private double GalleryLeft(Border frame) => double.IsFinite(Canvas.GetLeft(frame)) ? Canvas.GetLeft(frame) : 0;
    private double GalleryTop(Border frame) => double.IsFinite(Canvas.GetTop(frame)) ? Canvas.GetTop(frame) : 0;
    private double GalleryCenterX(Border frame) => GalleryLeft(frame) + frame.ActualWidth / 2;
    private double GalleryCenterY(Border frame) => GalleryTop(frame) + frame.ActualHeight / 2;

    private double GalleryRadius(Border frame, double distance)
    {
        double fov = GalleryMotionViewport.Camera is PerspectiveCamera camera ? camera.FieldOfView : 32;
        double unitPerDip = 2 * distance * Math.Tan(fov * Math.PI / 360) / Math.Max(1, GalleryMotionViewport.ActualWidth);
        double offset = Math.Abs(GalleryCenterX(frame) - GalleryCenterX(galleryCenterVisual)) * unitPerDip;
        return OrbitRadius(offset, distance, GallerySideAngle);
    }

    private double GalleryVerticalOffset(Border frame, double angle, double radius, double distance)
    {
        double fov = GalleryMotionViewport.Camera is PerspectiveCamera camera ? camera.FieldOfView : 32;
        double unitPerDip = 2 * distance * Math.Tan(fov * Math.PI / 360) / Math.Max(1, GalleryMotionViewport.ActualWidth);
        double depth = radius * (1 - Math.Cos(angle * Math.PI / 180));
        return (GalleryMotionViewport.ActualHeight / 2 - GalleryCenterY(frame)) * unitPerDip * (distance + depth) / distance;
    }

    private static double GalleryRoleAngle(Border frame, Border previous, Border center, Border next) =>
        ReferenceEquals(frame, previous) ? -GallerySideAngle : ReferenceEquals(frame, next) ? GallerySideAngle : 0;

    private double GalleryRoleAngle(Border frame) => GalleryRoleAngle(frame, galleryPreviousVisual, galleryCenterVisual, galleryNextVisual);

    private static void SetGalleryAnimationBase(Animatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    private void SetGalleryOrbit(GalleryMotionCard card, double radius)
    {
        card.Radius = radius;
        card.Orbit.CenterZ = -radius;
    }

    private void UpdateGalleryMotionScene()
    {
        if (!galleryMotionSceneReady || galleryMotionSceneUpdating || galleryTransitioning) return;
        galleryMotionSceneUpdating = true;
        try
        {
            GalleryViewportHost.UpdateLayout();
            GalleryMotionViewport.UpdateLayout();
            double distance = CameraDistance();
            double fov = GalleryMotionViewport.Camera is PerspectiveCamera camera ? camera.FieldOfView : 32;
            double unitPerDip = 2 * distance * Math.Tan(fov * Math.PI / 360) / Math.Max(1, GalleryMotionViewport.ActualWidth);
            double previousRadius = GalleryRadius(galleryPreviousVisual, distance);
            double nextRadius = GalleryRadius(galleryNextVisual, distance);
            double centerRadius = (previousRadius + nextRadius) / 2;
            foreach (var card in galleryMotionCards)
            {
                var size = new Size(card.Frame.ActualWidth, card.Frame.ActualHeight);
                if (size.Width <= 0 || size.Height <= 0) continue;
                card.Mesh.Positions = GalleryCardMesh(size, unitPerDip).Positions;
                double angle = GalleryRoleAngle(card.Frame);
                double radius = ReferenceEquals(card.Frame, galleryPreviousVisual) ? previousRadius
                    : ReferenceEquals(card.Frame, galleryNextVisual) ? nextRadius : centerRadius;
                SetGalleryOrbit(card, radius);
                SetGalleryAnimationBase(card.Rotation, AxisAngleRotation3D.AngleProperty, angle);
                double roleScale = ReferenceEquals(card.Frame, galleryCenterVisual) ? 1 : GallerySideScale;
                double scale = GalleryMotionScale(angle, radius, distance, roleScale);
                SetGalleryAnimationBase(card.Scale, ScaleTransform3D.ScaleXProperty, scale);
                SetGalleryAnimationBase(card.Scale, ScaleTransform3D.ScaleYProperty, scale);
                SetGalleryAnimationBase(card.Translation, TranslateTransform3D.OffsetYProperty, GalleryVerticalOffset(card.Frame, angle, radius, distance));
            }
        }
        finally { galleryMotionSceneUpdating = false; }
    }

    private static ContentControl GalleryFrameContent(Border frame) => Descendants(frame).OfType<ContentControl>().First();

    private void RotateGalleryCardRoles(int direction)
    {
        if (direction > 0)
        {
            (galleryPreviousVisual, galleryCenterVisual, galleryNextVisual) =
                (galleryCenterVisual, galleryNextVisual, galleryPreviousVisual);
        }
        else
        {
            (galleryPreviousVisual, galleryCenterVisual, galleryNextVisual) =
                (galleryNextVisual, galleryPreviousVisual, galleryCenterVisual);
        }
    }

    private void BindGalleryCardRoles(PanelViewModel model)
    {
        BindGalleryFrame(galleryPreviousVisual, GalleryCardRole.Previous, model);
        BindGalleryFrame(galleryCenterVisual, GalleryCardRole.Center, model);
        BindGalleryFrame(galleryNextVisual, GalleryCardRole.Next, model);
    }

    private enum GalleryCardRole { Previous, Center, Next }

    private static void BindGalleryFrame(Border frame, GalleryCardRole role, PanelViewModel model)
    {
        string width, height, left, content;
        if (role == GalleryCardRole.Center)
        {
            width = "GalleryWidth"; height = "GalleryHeight"; left = "GalleryCarouselCenterLeft"; content = "GalleryPreview";
            BindingOperations.ClearBinding(frame, Canvas.TopProperty);
            Canvas.SetTop(frame, 0);
        }
        else if (role == GalleryCardRole.Previous)
        {
            width = "GalleryPreviousCard.Width"; height = "GalleryPreviousCard.Height"; left = "GalleryPreviousCard.Left"; content = "GalleryPreviousCard.Slot";
            BindingOperations.SetBinding(frame, Canvas.TopProperty, new Binding("GalleryPreviousCard.Top") { Source = model });
        }
        else
        {
            width = "GalleryNextCard.Width"; height = "GalleryNextCard.Height"; left = "GalleryNextCard.Left"; content = "GalleryNextCard.Slot";
            BindingOperations.SetBinding(frame, Canvas.TopProperty, new Binding("GalleryNextCard.Top") { Source = model });
        }
        BindingOperations.SetBinding(frame, FrameworkElement.WidthProperty, new Binding(width) { Source = model });
        BindingOperations.SetBinding(frame, FrameworkElement.HeightProperty, new Binding(height) { Source = model });
        BindingOperations.SetBinding(frame, Canvas.LeftProperty, new Binding(left) { Source = model });
        BindingOperations.SetBinding(GalleryFrameContent(frame), ContentControl.ContentProperty, new Binding(content) { Source = model });
        GalleryFrameContent(frame).HorizontalContentAlignment = HorizontalAlignment.Stretch;
        GalleryFrameContent(frame).VerticalContentAlignment = VerticalAlignment.Stretch;
        frame.SetResourceReference(Border.BackgroundProperty, role == GalleryCardRole.Center ? "GalleryTileFill" : "GallerySideTileFill");
        frame.RenderTransformOrigin = new Point(0.5, 0.5);
        frame.RenderTransform = Transform.Identity;
        if (role == GalleryCardRole.Center) frame.ClearValue(UIElement.OpacityProperty);
        else frame.SetResourceReference(UIElement.OpacityProperty, "GallerySideOpacity");
        frame.IsHitTestVisible = role == GalleryCardRole.Center;
        frame.IsEnabled = role == GalleryCardRole.Center;
        frame.Focusable = role == GalleryCardRole.Center;
        KeyboardNavigation.SetTabNavigation(frame, role == GalleryCardRole.Center ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
        if (frame.Effect is DropShadowEffect shadow)
        {
            shadow.BlurRadius = role == GalleryCardRole.Center ? 18 : 16;
            shadow.ShadowDepth = role == GalleryCardRole.Center ? 5 : 4;
        }
    }

    private static double OrbitRadius(double screenOffset, double distance, double degrees)
    {
        double angle = degrees * Math.PI / 180;
        double denominator = distance * Math.Sin(angle) - screenOffset * (1 - Math.Cos(angle));
        return denominator > 0 ? screenOffset * distance / denominator : distance * 2;
    }

    private static double GalleryMotionScale(double angle, double radius, double distance, double roleScale)
    {
        double depth = radius * (1 - Math.Cos(angle * Math.PI / 180));
        return Math.Clamp((distance + depth) / distance * roleScale, 0.1, 1.8);
    }

    private double CameraDistance() => GalleryMotionViewport.Camera is PerspectiveCamera camera ? camera.Position.Z : 4.5;

    private static void Animate(Animatable target, DependencyProperty property, double to, TimeSpan duration, IEasingFunction easing, double? from = null) =>
        target.BeginAnimation(property, new DoubleAnimation { From = from, To = to, Duration = new Duration(duration), EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);

    private void StopGalleryMotionAnimations()
    {
        foreach (var card in galleryMotionCards)
        {
            card.Rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            card.Scale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
            card.Scale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
            card.Translation.BeginAnimation(TranslateTransform3D.OffsetYProperty, null);
        }
        galleryTransitioning = false;
    }

    private void CancelGalleryTransition(bool commitPending)
    {
        if (!galleryTransitioning) return;
        galleryTransitionVersion++;
        var id = pendingGallerySelection;
        var preview = pendingGalleryPreview;
        int direction = pendingGalleryDirection;
        bool rotateCards = pendingGalleryCardMotion;
        StopGalleryMotionAnimations();
        if (commitPending && id is { } selection && DataContext is PanelViewModel model)
        {
            model.CommitGallerySelection(selection, preview);
            if (rotateCards) RotateGalleryCardRoles(direction);
            BindGalleryCardRoles(model);
        }
        if (!commitPending && DataContext is PanelViewModel currentModel) BindGalleryCardRoles(currentModel);
        ResetGalleryCopyAnimations();
        UpdateGalleryMotionScene();
        GalleryMotionViewport.IsHitTestVisible = true;
        pendingGallerySelection = null;
        pendingGalleryPreview = null;
        pendingGalleryCardMotion = false;
    }

    private void CompleteGalleryTransition(int version)
    {
        if (version != galleryTransitionVersion || !galleryTransitioning || !pendingGalleryCardMotion || DataContext is not PanelViewModel model
            || pendingGallerySelection is not { } selection) return;
        var preview = pendingGalleryPreview;
        int direction = pendingGalleryDirection;
        StopGalleryMotionAnimations();
        model.CommitGallerySelection(selection, preview);
        RotateGalleryCardRoles(direction);
        BindGalleryCardRoles(model);
        GalleryViewportHost.UpdateLayout();
        UpdateGalleryMotionScene();
        GalleryMotionViewport.IsHitTestVisible = true;
        pendingGallerySelection = null;
        pendingGalleryPreview = null;
        pendingGalleryCardMotion = false;
        AnimateGalleryCopyIn();
    }

    private void CompleteGalleryCopyOnlySelection(int version)
    {
        if (version != galleryTransitionVersion || !galleryTransitioning || pendingGalleryCardMotion
            || DataContext is not PanelViewModel model || pendingGallerySelection is not { } selection) return;
        model.CommitGallerySelection(selection);
        BindGalleryCardRoles(model);
        GalleryViewportHost.UpdateLayout();
        galleryTransitioning = false;
        UpdateGalleryMotionScene();
        GalleryMotionViewport.IsHitTestVisible = true;
        pendingGallerySelection = null;
        pendingGalleryPreview = null;
        pendingGalleryCardMotion = false;
        AnimateGalleryCopyIn();
    }

    private void OnWidgetRowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWidgetHomeDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not PanelViewModel model || e.Data.GetData(typeof(string)) is not string source) return;
        if (sender is FrameworkElement { DataContext: WidgetSlot target }) model.MoveWidgetBefore(source, target.Id);
        else model.AddWidgetToDraft(source);
        e.Handled = true;
    }

    /// La confirmación de finalizar procesos sale debajo de la lista: se lleva a la vista para que no pase desapercibida.
    private void OnConfirmationVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) ((FrameworkElement)sender).BringIntoView();
    }

    private void OnWidgetOverlayVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            folderFocusReturnTarget = Keyboard.FocusedElement;
            folderFocusReturnId = AutomationIdOf(folderFocusReturnTarget);
            Dispatcher.BeginInvoke(FreezeFolderViewport, System.Windows.Threading.DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(() => { if (!WidgetSheet.IsVisible) FocusFirst(WidgetOverlay); }, System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            RestoreFolderViewport();
            RestoreFocus(folderFocusReturnTarget, folderFocusReturnId);
            folderFocusReturnTarget = null; folderFocusReturnId = null;
        }
    }

    private void FreezeFolderViewport()
    {
        if (folderViewportFrozen || !WidgetOverlay.IsVisible
            || WidgetFolderViewport.ActualHeight <= 0) return;
        folderViewportFrozen = true;
        WidgetFolderViewport.ClearValue(FrameworkElement.MaxHeightProperty);
        WidgetFolderViewport.MaxHeight = WidgetFolderViewport.ActualHeight;
    }

    private void RestoreFolderViewport()
    {
        if (!folderViewportFrozen) return;
        folderViewportFrozen = false;
        BindingOperations.SetBinding(WidgetFolderViewport, FrameworkElement.MaxHeightProperty,
            new Binding(nameof(PanelViewModel.BodyMaxHeight)) { Source = DataContext });
    }

    /// Un permiso de un agente aparece: el foco va a la tarjeta para que Y / N / Esc funcionen sin tocar el ratón.
    private void OnApprovalVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(() => { if (ApprovalOverlay.IsVisible) FocusFirst(ApprovalOverlay); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnWidgetSheetVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            sheetFocusReturnTarget = Keyboard.FocusedElement;
            sheetFocusReturnId = AutomationIdOf(sheetFocusReturnTarget);
            Dispatcher.BeginInvoke(() => { if (WidgetSheet.IsVisible) FocusFirst(WidgetSheet); }, System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            if (WidgetOverlay.IsVisible) RestoreFocus(sheetFocusReturnTarget, sheetFocusReturnId);
            sheetFocusReturnTarget = null; sheetFocusReturnId = null;
        }
    }

    private static void FocusFirst(DependencyObject root)
    {
        if (root is FrameworkElement element) element.UpdateLayout();
        var target = Descendants(root).OfType<UIElement>().FirstOrDefault(e => e.IsVisible && e.IsEnabled && e.Focusable
            && KeyboardNavigation.GetIsTabStop(e));
        target?.Focus();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string? AutomationIdOf(IInputElement? target) => target is DependencyObject element
        ? AutomationProperties.GetAutomationId(element) : null;

    private void RestoreFocus(IInputElement? target, string? automationId) => Dispatcher.BeginInvoke(() =>
    {
        var replacement = automationId is { Length: > 0 } id
            ? Descendants(this).OfType<UIElement>().FirstOrDefault(e => e.IsVisible && e.IsEnabled && e.Focusable
                && AutomationProperties.GetAutomationId(e) == id)
            : null;
        if (replacement is not null) replacement.Focus();
        else if (target is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } ui) ui.Focus();
        else if (target is ContentElement { Focusable: true } content) Keyboard.Focus(content);
    }, System.Windows.Threading.DispatcherPriority.Input);

    /// Al abrir el editor el cursor ya está en el texto: se escribe sin hacer clic antes.
    private void OnEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(() => EditorBox.TextArea.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// Ancho (640) en dos columnas, o estrecho (340) en una: los mismos elementos, reacomodados.
    private void ApplyWide(bool on)
    {
        if (on == wide) return;
        wide = on;
        Width = on ? 640 : 340;
        foreach (var panel in new[] { MainPanel })
        {
            if (on) Columnize(panel);
            else Restore(panel);
        }
    }

    private void Columnize(StackPanel panel)
    {
        var items = panel.Children.Cast<UIElement>().ToArray();
        columnized[panel] = items;
        panel.Children.Clear();
        var left = new StackPanel { Margin = new Thickness(0, 0, 7, 0) };
        var right = new StackPanel { Margin = new Thickness(7, -10, 0, 0) };   // -10: sin el aire que lleva arriba cada bloque
        bool second = false;
        foreach (var item in items)
        {
            second |= GetBreakBefore(item);
            (second ? right : left).Children.Add(item);
        }
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        panel.Children.Add(grid);
    }

    private void Restore(StackPanel panel)
    {
        if (!columnized.Remove(panel, out var items)) return;
        foreach (var column in ((Grid)panel.Children[0]).Children.OfType<StackPanel>()) column.Children.Clear();
        panel.Children.Clear();
        foreach (var item in items) panel.Children.Add(item);
    }

    // Checked cubre ratón, teclado y lectores de pantalla; una actualización del enlace no vuelve a guardar.
    private void OnPetChoiceChecked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: PetChoice choice } && !choice.IsSelected)
            choice.Pick.Execute(null);
    }

    /// La cabecera mueve el panel: arrastrar lo deja donde lo sueltes y el doble clic
    /// lo devuelve junto a la bandeja. Los botones de la cabecera se quedan su clic.
    private void OnHeaderMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (System.Windows.Window.GetWindow(this) is not PanelWindow window) return;
        e.Handled = true;
        if (e.ClickCount == 2) { window.ResetPosition(); return; }
        window.BeginDrag();
        ((System.Windows.UIElement)sender).CaptureMouse();
    }

    private void OnHeaderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (((System.Windows.UIElement)sender).IsMouseCaptured && System.Windows.Window.GetWindow(this) is PanelWindow window)
            window.DragTo();
    }

    private void OnHeaderMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var header = (System.Windows.UIElement)sender;
        if (!header.IsMouseCaptured) return;
        header.ReleaseMouseCapture();
        if (System.Windows.Window.GetWindow(this) is PanelWindow window) window.EndDrag();
    }

    /// El halo del botón late mientras Ollama arranca o se apaga.
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var model = (PanelViewModel)DataContext;
        ApplyWide(model.WideLayout);
        var transition = model.IsTransition;
        if (transition == pulsing) return;
        pulsing = transition;
        var pulse = transition
            ? new DoubleAnimation(1, 1.10, TimeSpan.FromSeconds(0.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            }
            : null;
        HaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        HaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

}
