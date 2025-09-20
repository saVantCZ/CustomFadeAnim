using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace CustomFadeAnim
{
    public class CustomFadeAnim : GenericPlugin
    {
        private CustomFadeAnimSettingsViewModel settings;
        private static readonly ILogger logger = LogManager.GetLogger();
        private FrameworkElement lastFadeImage;
        private SlideDirection lastNavDirection = SlideDirection.FromRight;
        private SlideDirection lockedSlideDirection = SlideDirection.FromRight;

        private readonly HashSet<Control> hookedDesktopViews = new HashSet<Control>();
        private readonly HashSet<Window> hookedFsWindows = new HashSet<Window>();
        private readonly HashSet<ContentControl> watchedBgChangers = new HashSet<ContentControl>();
        private readonly HashSet<FrameworkElement> watchedFadeImages = new HashSet<FrameworkElement>();

        private Dictionary<string, Func<Image, Border, Grid, AnimationParams, Storyboard>> inAnimations;
        private Dictionary<string, Func<Image, Border, Grid, AnimationParams, Storyboard>> outAnimations;


        public override Guid Id { get; } = Guid.Parse("155b27bc-6c8c-47dc-ae41-e74568b2fe9f");

        public CustomFadeAnim(IPlayniteAPI api) : base(api)
        {
            settings = new CustomFadeAnimSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
            settings.Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(settings.Settings.SourceUpdateDelay))
                {
                    TryApplySourceUpdateDelayToLast();
                }
            };
        }

        private void TryApplySourceUpdateDelayToLast()
        {
            if (lastFadeImage != null)
            {
                ApplySourceUpdateDelay(lastFadeImage, settings.Settings.SourceUpdateDelay);
            }
        }

        private void HookFsInputAndSelection(Window win)
        {
            if (win == null) return;

            if (!hookedFsWindows.Contains(win))
            {
                win.PreviewKeyDown += MainWindow_PreviewKeyDown;
                hookedFsWindows.Add(win);
            }
        }

        private void WatchFadeImageSourceChanges(FrameworkElement fade)
        {
            if (fade == null || watchedFadeImages.Contains(fade))
                return;

            try
            {
                var dpField = fade.GetType().GetField("SourceProperty",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (dpField == null) return;

                var dp = dpField.GetValue(null) as DependencyProperty;
                if (dp == null) return;

                var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(dp, fade.GetType());
                if (dpd == null) return;

                dpd.AddValueChanged(fade, (s, e) =>
                {
                    ApplyCustomAnimations(fade);
                });

                fade.Unloaded += (s, e) =>
                {
                    watchedFadeImages.Remove(fade);
                    if (ReferenceEquals(lastFadeImage, fade))
                    {
                        lastFadeImage = null;
                    }
                };

                watchedFadeImages.Add(fade);
            }
            catch
            {
                
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    lastNavDirection = SlideDirection.FromLeft;
                    break;
                case Key.Right:
                    lastNavDirection = SlideDirection.FromRight;
                    break;
                case Key.Up:
                    lastNavDirection = SlideDirection.FromTop;
                    break;
                case Key.Down:
                    lastNavDirection = SlideDirection.FromBottom;
                    break;
            }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            InitAnimations();
            settings.Settings.AvailableAnimations = inAnimations.Keys.ToList();
            settings.EnsureDefaults(settings.Settings.AvailableAnimations);

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TryHookWithRetries();
                TryHookDesktopModeOnce();
                StartDesktopModeWatcher();

                var mw = Application.Current?.MainWindow;
                if (mw != null)
                {
                    mw.PreviewKeyDown += MainWindow_PreviewKeyDown;
                }

            }, DispatcherPriority.Background);
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            var mw = Application.Current?.MainWindow;
            if (mw != null)
            {
                mw.PreviewKeyDown -= MainWindow_PreviewKeyDown;
                mw.LayoutUpdated -= MainWindow_LayoutUpdated;
            }

            foreach (var w in hookedFsWindows.ToList())
            {
                if (w != null) w.PreviewKeyDown -= MainWindow_PreviewKeyDown;
            }
            hookedFsWindows.Clear();

            UnwatchAllBackgroundChangers();
        }

        // ---------------------- Inicializace animací ----------------------

        public static class AnimationNames
        {
            public const string Default = "Default";
            public const string CustomFade = "Custom Fade";
            public const string BlurFade = "Blur Fade";
            public const string FadeZoom = "Fade + Zoom";
            public const string PS5Slide = "PS5 Simple Slide";
            public const string CustomSlide = "Custom Slide";
            public const string ZoomRotate = "Zoom + Rotate";
            public const string PageCurl = "Wave Curl";
            public const string SmartSlide = "Smart Slide";
        }


        private void InitAnimations()
        {
            inAnimations = new Dictionary<string, Func<Image, Border, Grid, AnimationParams, Storyboard>>
            {
                { AnimationNames.Default,       (img, border, holder, p) => SbDefaultIn(img, border, p)  },
                { AnimationNames.CustomFade,    (img, border, holder, p) => SbFadeIn(img, border, p) },
                { AnimationNames.FadeZoom,      (img, border, holder, p) => SbFadeZoomIn(img, border, p) },
                { AnimationNames.PS5Slide,      (img, border, holder, p) => SbSlideIn(img, border, p) },
                { AnimationNames.CustomSlide,   (img, border, holder, p) => SbCustomSlideIn(img, border, p) },
                { AnimationNames.SmartSlide,    (img, border, holder, p) => SbSmartSlideIn(img, border, p) },
                { AnimationNames.ZoomRotate,    (img, border, holder, p) => SbZoomRotateIn(img, border, p) },
                { AnimationNames.PageCurl,      (img, border, holder, p) => SbPageCurlIn(img, border, p) },
                { AnimationNames.BlurFade,      (img, border, holder, p) => SbBlurFadeIn(img, border, p) },

            };

            outAnimations = new Dictionary<string, Func<Image, Border, Grid, AnimationParams, Storyboard>>
            {
                { AnimationNames.Default,       (img, border, holder, p) => SbDefaultOut(img, p)  },
                { AnimationNames.CustomFade,    (img, border, holder, p) => SbFadeOut(img, p) },
                { AnimationNames.FadeZoom,      (img, border, holder, p) => SbFadeZoomOut(img, p) },
                { AnimationNames.PS5Slide,      (img, border, holder, p) => SbSlideOut(img, p) },
                { AnimationNames.CustomSlide,   (img, border, holder, p) => SbCustomSlideOut(img, p) },
                { AnimationNames.SmartSlide,    (img, border, holder, p) => SbSmartSlideOut(img, p) },
                { AnimationNames.ZoomRotate,    (img, border, holder, p) => SbZoomRotateOut(img, p) },
                { AnimationNames.PageCurl,      (img, border, holder, p) => SbPageCurlOut(img, p) },
                { AnimationNames.BlurFade,      (img, border, holder, p) => SbBlurFadeOut(img, p) },
            };
        }

        // ---------------------- Hook do Fullscreen UI ----------------------

        private void TryHookWithRetries()
        {
            const int maxAttempts = 10;
            int attempt = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };

            timer.Tick += (s, e) =>
            {
                attempt++;
                string reason;
                if (TryHookOnce(out reason))
                {
                    timer.Stop();
                }
                else if (attempt >= maxAttempts)
                {
                    timer.Stop();
                }
            };

            timer.Start();
        }

        private bool TryHookOnce(out string failReason)
        {
            failReason = null;

            var win = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsVisible);
            if (win == null)
            {
                failReason = "No visible window.";
                return false;
            }

            HookFsInputAndSelection(win);
            TryHookBackgroundChanger(win);
            List<FrameworkElement> fadeImages = new List<FrameworkElement>();

            var part = FindChildByName(win, "PART_ImageBackground") as FrameworkElement;
            if (part != null)
            {
                fadeImages.Add(part);
                logger.Info("[CustomFadeAnim] Found PART_ImageBackground (Universal hook).");
            }

            FindAllFadeImages(win, fadeImages);

            fadeImages = fadeImages.Distinct().ToList();
            logger.Info($"[CustomFadeAnim] Animating {fadeImages.Count} FadeImage element(s) (Universal hook).");


            if (fadeImages.Count == 0)
            {
                failReason = "No FadeImage found.";
                return false;
            }

            // Fix Smart Slide… mrdka jedna
            lastFadeImage = fadeImages[0];

            foreach (var fi in fadeImages)
            {
                
                WatchFadeImageSourceChanges(fi);

                if (!fi.IsLoaded)
                {
                    RoutedEventHandler onLoaded = null;
                    onLoaded = (s, e2) =>
                    {
                        fi.Loaded -= onLoaded;
                        ApplyCustomAnimations(fi);
                    };
                    fi.Loaded += onLoaded;
                }
                else
                {
                    ApplyCustomAnimations(fi);
                }
            }

            return true;
        }

        // ---------------------- Hook do Desktop UI ----------------------
        private void StartDesktopModeWatcher()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            TryHookDesktopModeOnce();
            TryHookBackgroundChanger(Application.Current.MainWindow);

            mainWindow.LayoutUpdated -= MainWindow_LayoutUpdated;
            mainWindow.LayoutUpdated += MainWindow_LayoutUpdated;
        }

        private void MainWindow_LayoutUpdated(object sender, EventArgs e)
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            HookIfNew(mainWindow, "Playnite.DesktopApp.Controls.Views.DetailsViewGameOverview");
            HookIfNew(mainWindow, "Playnite.DesktopApp.Controls.Views.GridViewGameOverview");
            HookIfNew(mainWindow, "Playnite.DesktopApp.Controls.Views.Library");
            TryHookBackgroundChanger(Application.Current.MainWindow);

        }

        private void HookIfNew(DependencyObject root, string typeName)
        {
            var view = FindVisualChildByTypeName(root, typeName) as Control;
            if (view == null) return;

            if (hookedDesktopViews.Contains(view))
            {
                return;
            }

            hookedDesktopViews.Add(view);

            if (view.IsLoaded)
            {
                HookFadeImageFromTemplate(view);
                HookAllFadeImagesFromView(view);
                TryHookBackgroundChanger(view);
            }
            else
            {
                RoutedEventHandler onLoaded = null;
                onLoaded = (s, e) =>
                {
                    view.Loaded -= onLoaded;
                    HookFadeImageFromTemplate(view);
                    HookAllFadeImagesFromView(view);
                    TryHookBackgroundChanger(view);
                };
                view.Loaded += onLoaded;
            }
        }

        private void TryHookDesktopModeOnce()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            var typeNames = new[]
            {
                "Playnite.DesktopApp.Controls.Views.DetailsViewGameOverview",
                "Playnite.DesktopApp.Controls.Views.GridViewGameOverview",
                "Playnite.DesktopApp.Controls.Views.Library"
            };

            foreach (var typeName in typeNames)
            {
                var view = FindVisualChildByTypeName(mainWindow, typeName) as Control;
                if (view == null) continue;

                if (!hookedDesktopViews.Contains(view))
                {
                    hookedDesktopViews.Add(view);
                }

                if (view.IsLoaded)
                {
                    HookFadeImageFromTemplate(view);
                    HookAllFadeImagesFromView(view);
                    TryHookBackgroundChanger(view);
                }
                else
                {
                    RoutedEventHandler onLoaded = null;
                    onLoaded = (s, e) =>
                    {
                        view.Loaded -= onLoaded;
                        HookFadeImageFromTemplate(view);
                        HookAllFadeImagesFromView(view);
                        TryHookBackgroundChanger(view);
                    };
                    view.Loaded += onLoaded;
                }

            }

        }

        private void HookFadeImageFromTemplate(Control view)
        {
            try
            {
                view.ApplyTemplate();

                var fadeImage = view.Template?.FindName("PART_ImageBackground", view) as FrameworkElement;

                if (fadeImage == null)
                {
                    fadeImage = FindChildByName(view, "PART_ImageBackground") as FrameworkElement;
                }

                if (fadeImage != null)
                {
                    HookFadeImageWithLoadedCheck(fadeImage);
                }
            }
            catch
            {
               
            }
        }

        private void HookAllFadeImagesFromView(Control view)
        {
            try
            {
                view.ApplyTemplate();

                List<FrameworkElement> fadeImages = new List<FrameworkElement>();

                var part = view.Template?.FindName("PART_ImageBackground", view) as FrameworkElement;
                if (part != null)
                {
                    fadeImages.Add(part);
                    logger.Info("[CustomFadeAnim] Found PART_ImageBackground (Desktop hook).");
                }

                FindAllFadeImages(view, fadeImages);
                fadeImages = fadeImages.Distinct().ToList();

                foreach (var fi in fadeImages)
                {
                    WatchFadeImageSourceChanges(fi);
                }

                logger.Info($"[CustomFadeAnim] Animating {fadeImages.Count} FadeImage(s) (Desktop hook).");

                var visibleFadeImages = fadeImages.Where(IsEffectivelyVisible).ToList();

                if (visibleFadeImages.Count > 0)
                {
                    HookFadeImageWithLoadedCheck(visibleFadeImages[0]);
                }
                else
                {
                    // taky dobra mrdka, pitomej backgroundchanger
                    var bgChanger = FindChildByName(view, "BackgroundChanger_PluginBackgroundImage") as ContentControl;
                    if (bgChanger != null)
                    {
                        WatchBackgroundChanger(bgChanger); 
                        HookBackgroundChangerContent(bgChanger.Content);
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex, "[CustomFadeAnim] Failed to hook desktop fade images.");
            }
        }

        // ---------------------- Pokus o Hook na BackgroundChanger, jen kdyz je FadeImage -------------------------

        private void BackgroundChanger_ContentValueChanged(object sender, EventArgs e)
        {
            var cc = sender as ContentControl;
            if (cc != null)
            {
                HookBackgroundChangerContent(cc.Content);
            }
        }

        private void HookBackgroundChangerContent(object content)
        {
            var fe = content as FrameworkElement;
            if (fe == null)
                return;

            // přímo FadeImage
            if (IsFadeImageType(fe.GetType()))
            {
                logger.Info("[CustomFadeAnim] Hooking FadeImage from BackgroundChanger content.");
                HookFadeImageWithLoadedCheck(fe);
                return;
            }

            // PluginBackgroundImage - nedelej nic
            if (fe.GetType().Name == "PluginBackgroundImage")
            {
                logger.Info("[CustomFadeAnim] Detected BackgroundChanger as ContentControl. No animation hook applied. Disable BackgroundChanger for this Theme.");
                lastFadeImage = fe;
                return;
            }

            // wrapper, jestli nekdo ma FadeImage uvnitr, kokotina to je
            var nestedFade = FindVisualChildren<FrameworkElement>(fe)
                .FirstOrDefault(c => IsFadeImageType(c.GetType()));
            if (nestedFade != null)
            {
                logger.Info("[CustomFadeAnim] Hooking nested FadeImage inside BackgroundChanger content.");
                HookFadeImageWithLoadedCheck(nestedFade);
                return;
            }

            // kdyz nic, tak nasrat
            logger.Info("[CustomFadeAnim] No FadeImage or PluginBackgroundImage found in BackgroundChanger content.");
        }


        private void WatchBackgroundChanger(ContentControl cc)
        {
            if (cc == null || watchedBgChangers.Contains(cc))
                return;

            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
            if (dpd != null)
            {
                dpd.RemoveValueChanged(cc, BackgroundChanger_ContentValueChanged);
                dpd.AddValueChanged(cc, BackgroundChanger_ContentValueChanged);
                watchedBgChangers.Add(cc);
            }

            HookBackgroundChangerContent(cc.Content);
        }

        private void UnwatchAllBackgroundChangers()
        {
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
            if (dpd != null)
            {
                foreach (var cc in watchedBgChangers.ToList())
                {
                    dpd.RemoveValueChanged(cc, BackgroundChanger_ContentValueChanged);
                }
            }
            watchedBgChangers.Clear();
        }
        private void TryHookBackgroundChanger(FrameworkElement root)
        {
            try
            {
                if (root == null) return;

                var bc = FindChildByName(root, "BackgroundChanger_PluginBackgroundImage") as ContentControl;
                if (bc == null)
                {
                    return;
                }

                root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    HookBackgroundChangerBoundFadeImages(root, bc);
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[CustomFadeAnim] Failed to hook BackgroundChanger.");
            }
        }

        private void HookBackgroundChangerBoundFadeImages(FrameworkElement root, ContentControl bc)
        {
            try
            {
                if (bc.Content is FrameworkElement contentFe && IsFadeImageType(contentFe.GetType()))
                {
                    HookFadeImageWithLoadedCheck(contentFe);
                }

                var allFadeImages = new List<FrameworkElement>();
                FindAllFadeImages(root, allFadeImages);

                foreach (var fi in allFadeImages)
                {
                    if (IsBoundToBackgroundChangerContentSource(fi, "BackgroundChanger_PluginBackgroundImage"))
                    {
                        HookFadeImageWithLoadedCheck(fi);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[CustomFadeAnim] Failed during BackgroundChanger binding hook.");
            }
        }

        private bool IsBoundToBackgroundChangerContentSource(FrameworkElement fadeImage, string elementName)
        {
            try
            {
                var dpField = fadeImage.GetType().GetField("SourceProperty",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (dpField == null) return false;

                var dp = dpField.GetValue(null) as DependencyProperty;
                if (dp == null) return false;

                var exprBase = BindingOperations.GetBindingExpressionBase(fadeImage, dp);
                if (exprBase == null) return false;

                //  Binding
                var be = exprBase as BindingExpression;
                if (be != null)
                {
                    var b = be.ParentBinding;
                    return b != null && b.ElementName == elementName && b.Path != null && b.Path.Path == "Content.Source";
                }

                // PriorityBinding
                var pbe = exprBase as PriorityBindingExpression;
                if (pbe != null && pbe.ParentPriorityBinding != null)
                {
                    foreach (var b in pbe.ParentPriorityBinding.Bindings.OfType<Binding>())
                    {
                        if (b.ElementName == elementName && b.Path != null && b.Path.Path == "Content.Source")
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }



        // ---------------------- Animace (Hook do FadeImage.cs animace) ----------------------

        private void ApplyCustomAnimations(FrameworkElement fadeImage)
        {
            try
            {
                lastFadeImage = fadeImage;

                ApplySourceUpdateDelay(fadeImage, settings.Settings.SourceUpdateDelay);


                var type = fadeImage.GetType();

                var img1Field = type.GetField("Image1", BindingFlags.Instance | BindingFlags.NonPublic);
                var img2Field = type.GetField("Image2", BindingFlags.Instance | BindingFlags.NonPublic);
                var borderDarkenField = type.GetField("BorderDarken", BindingFlags.Instance | BindingFlags.NonPublic);
                var imageHolderField = type.GetField("ImageHolder", BindingFlags.Instance | BindingFlags.NonPublic);

                var img1 = img1Field != null ? img1Field.GetValue(fadeImage) as Image : null;
                var img2 = img2Field != null ? img2Field.GetValue(fadeImage) as Image : null;
                var borderDarken = borderDarkenField != null ? borderDarkenField.GetValue(fadeImage) as Border : null;
                var imageHolder = imageHolderField != null ? imageHolderField.GetValue(fadeImage) as Grid : null;

                if (img1 == null || img2 == null)
                {
                    return;
                }

                EnsureTransforms(img1);
                EnsureTransforms(img2);

                if (settings.Settings.SelectedAnim == AnimationNames.SmartSlide)
                {
                    lockedSlideDirection = lastNavDirection;
                }

                var slideType = settings.Settings.SelectedAnim;
                if (slideType == AnimationNames.PS5Slide ||
                    slideType == AnimationNames.CustomSlide ||
                    slideType == AnimationNames.SmartSlide)
                {
                    if (settings.Settings.AnimParams.TryGetValue(slideType, out var ap) && ap != null && ap.EnableSlideZoom)
                    {
                        ApplyFixedSlideZoom(img1, ap.SlideDistance);
                        ApplyFixedSlideZoom(img2, ap.SlideDistance);
                    }
                }

                ResetTranslate(img1);
                ResetTranslate(img2);

                var img1FadeInField = type.GetField("Image1FadeIn", BindingFlags.Instance | BindingFlags.NonPublic);
                var img2FadeInField = type.GetField("Image2FadeIn", BindingFlags.Instance | BindingFlags.NonPublic);
                var img1FadeOutField = type.GetField("Image1FadeOut", BindingFlags.Instance | BindingFlags.NonPublic);
                var img2FadeOutField = type.GetField("Image2FadeOut", BindingFlags.Instance | BindingFlags.NonPublic);
                var borderDarkenFadeOutField = type.GetField("BorderDarkenFadeOut", BindingFlags.Instance | BindingFlags.NonPublic);

                var animType = settings.Settings.SelectedAnim ?? "Default";
                if (!inAnimations.ContainsKey(animType) || !outAnimations.ContainsKey(animType))
                {
                    animType = "Default";
                }

                AnimationParams p;
                if (!settings.Settings.AnimParams.TryGetValue(animType, out p) || p == null)
                {
                    p = settings.GetDefaultParamsFor(animType);
                    settings.Settings.AnimParams[animType] = p;
                }

                var in1 = inAnimations[animType](img1, borderDarken, imageHolder, p);
                var in2 = inAnimations[animType](img2, borderDarken, imageHolder, p);
                var out1 = outAnimations[animType](img1, borderDarken, imageHolder, p);
                var out2 = outAnimations[animType](img2, borderDarken, imageHolder, p);
                var borderOut = CreateBorderDarkenOut(borderDarken);

                if (img1FadeInField != null) img1FadeInField.SetValue(fadeImage, in1);
                if (img2FadeInField != null) img2FadeInField.SetValue(fadeImage, in2);
                if (img1FadeOutField != null) img1FadeOutField.SetValue(fadeImage, out1);
                if (img2FadeOutField != null) img2FadeOutField.SetValue(fadeImage, out2);
                if (borderDarkenFadeOutField != null && borderOut != null) borderDarkenFadeOutField.SetValue(fadeImage, borderOut);
            }
            catch
            {
               
            }
        }

        // ---------------------- Animace (implementace) ----------------------


        private Storyboard CreateBorderDarkenOut(Border borderDarken)
        {
            if (borderDarken == null) return null;
            var sb = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5),
                FillBehavior = FillBehavior.Stop
            };
            Storyboard.SetTarget(fade, borderDarken);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
            return sb;
        }

        private void EnsureTransforms(Image img)
        {
            var group = img.RenderTransform as TransformGroup;
            if (group == null)
            {
                group = new TransformGroup();
                group.Children.Add(new ScaleTransform(1.0, 1.0));         
                group.Children.Add(new TranslateTransform(0, 0));         
                group.Children.Add(new RotateTransform(0.0));             
                group.Children.Add(new SkewTransform(0.0, 0.0));          
                img.RenderTransform = group;
                img.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else
            {
                bool hasScale = false, hasTranslate = false, hasRotate = false, hasSkew = false;
                foreach (var t in group.Children)
                {
                    if (t is ScaleTransform) hasScale = true;
                    if (t is TranslateTransform) hasTranslate = true;
                    if (t is RotateTransform) hasRotate = true;
                    if (t is SkewTransform) hasSkew = true;
                }
                if (!hasScale) group.Children.Insert(0, new ScaleTransform(1.0, 1.0));
                if (!hasTranslate) group.Children.Insert(1, new TranslateTransform(0, 0));
                if (!hasRotate) group.Children.Insert(2, new RotateTransform(0.0));
                if (!hasSkew) group.Children.Insert(3, new SkewTransform(0.0, 0.0));
                img.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }

        // Default
        private Storyboard SbDefaultIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(p.Duration));
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
            if (borderDarken != null) borderDarken.Opacity = 1.0;
            return sb;
        }

        private Storyboard SbDefaultOut(Image target, AnimationParams p)
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(p.Duration));
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
            return sb;
        }

        // Custom Fade
        private Storyboard SbFadeIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(p.Duration));
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
            if (borderDarken != null) borderDarken.Opacity = 1.0;
            return sb;
        }

        private Storyboard SbFadeOut(Image target, AnimationParams p)
        {
            var sb = new Storyboard();
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(p.Duration));
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
            return sb;
        }

        // Fade + Zoom
        private Storyboard SbFadeZoomIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = SbFadeIn(target, borderDarken, p);
            var sx = new DoubleAnimation(p.ZoomStart, 1.0, TimeSpan.FromSeconds(p.Duration))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var sy = new DoubleAnimation(p.ZoomStart, 1.0, TimeSpan.FromSeconds(p.Duration))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(sx, target);
            Storyboard.SetTarget(sy, target);
            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            sb.Children.Add(sx);
            sb.Children.Add(sy);
            return sb;
        }

        private Storyboard SbFadeZoomOut(Image target, AnimationParams p)
        {
            var sb = SbFadeOut(target, p);
            var endZoom = p.ZoomStart > 1.0 ? Math.Max(1.0, p.ZoomStart - 0.06) : 1.02;
            var dur = Math.Max(0.05, p.Duration - 0.1);

            var sx = new DoubleAnimation(1.0, endZoom, TimeSpan.FromSeconds(dur))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var sy = new DoubleAnimation(1.0, endZoom, TimeSpan.FromSeconds(dur))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(sx, target);
            Storyboard.SetTarget(sy, target);
            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            sb.Children.Add(sx);
            sb.Children.Add(sy);
            return sb;
        }

        // PS5 Slide
        private Storyboard SbSlideIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = SbFadeIn(target, borderDarken, p);
            var tx = new DoubleAnimation(p.SlideDistance, 0, TimeSpan.FromSeconds(p.SlideDuration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(tx, target);
            Storyboard.SetTargetProperty(tx, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
            sb.Children.Add(tx);
            return sb;
        }

        private Storyboard SbSlideOut(Image target, AnimationParams p)
        {
            var sb = SbFadeOut(target, p);
            var tx = new DoubleAnimation(0, -Math.Abs(p.SlideDistance) * 0.5, TimeSpan.FromSeconds(Math.Max(0.05, p.SlideDuration - 0.1)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(tx, target);
            Storyboard.SetTargetProperty(tx, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
            sb.Children.Add(tx);
            return sb;
        }

        // Zoom Rotate
        private Storyboard SbZoomRotateIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = SbFadeIn(target, borderDarken, p);

            var sx = new DoubleAnimation(p.ZoomStart, 1.0, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var sy = new DoubleAnimation(p.ZoomStart, 1.0, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var rot = new DoubleAnimation(p.RotateAngle, 0.0, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

            Storyboard.SetTarget(sx, target);
            Storyboard.SetTarget(sy, target);
            Storyboard.SetTarget(rot, target);

            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            Storyboard.SetTargetProperty(rot, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(RotateTransform.Angle)"));

            sb.Children.Add(sx);
            sb.Children.Add(sy);
            sb.Children.Add(rot);
            return sb;
        }

        private Storyboard SbZoomRotateOut(Image target, AnimationParams p)
        {
            var sb = SbFadeOut(target, p);

            var endZoom = p.ZoomStart > 1.0 ? Math.Max(1.0, p.ZoomStart - 0.06) : 1.02;
            var sx = new DoubleAnimation(1.0, endZoom, TimeSpan.FromSeconds(Math.Max(0.05, p.Duration - 0.1)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var sy = new DoubleAnimation(1.0, endZoom, TimeSpan.FromSeconds(Math.Max(0.05, p.Duration - 0.1)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var rot = new DoubleAnimation(0.0, -Math.Sign(p.RotateAngle) * Math.Abs(p.RotateAngle) * 0.6, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };

            Storyboard.SetTarget(sx, target);
            Storyboard.SetTarget(sy, target);
            Storyboard.SetTarget(rot, target);

            Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            Storyboard.SetTargetProperty(rot, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[2].(RotateTransform.Angle)"));

            sb.Children.Add(sx);
            sb.Children.Add(sy);
            sb.Children.Add(rot);
            return sb;
        }

        // Curl
        private Storyboard SbPageCurlIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = SbFadeIn(target, borderDarken, p);

            var skewIn = new DoubleAnimation(-p.SkewAngle, 0.0, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

            Storyboard.SetTarget(skewIn, target);
            Storyboard.SetTargetProperty(skewIn, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[3].(SkewTransform.AngleX)"));
            sb.Children.Add(skewIn);

            return sb;
        }

        private Storyboard SbPageCurlOut(Image target, AnimationParams p)
        {
            var sb = SbFadeOut(target, p);

            var skewOut = new DoubleAnimation(0.0, p.SkewAngle, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };

            Storyboard.SetTarget(skewOut, target);
            Storyboard.SetTargetProperty(skewOut, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[3].(SkewTransform.AngleX)"));
            sb.Children.Add(skewOut);

            return sb;
        }

        // Blur Fade
        private Storyboard SbBlurFadeIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = SbFadeIn(target, borderDarken, p);

            EnsureBlurEffect(target);
            var blurIn = new DoubleAnimation(p.BlurRadius, 0.0, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

            Storyboard.SetTarget(blurIn, target);
            Storyboard.SetTargetProperty(blurIn, new PropertyPath("(UIElement.Effect).(BlurEffect.Radius)"));
            sb.Children.Add(blurIn);

            return sb;
        }

        private Storyboard SbBlurFadeOut(Image target, AnimationParams p)
        {
            var sb = SbFadeOut(target, p);

            EnsureBlurEffect(target);
            var blurOut = new DoubleAnimation(0.0, p.BlurRadius, TimeSpan.FromSeconds(p.Duration))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };

            Storyboard.SetTarget(blurOut, target);
            Storyboard.SetTargetProperty(blurOut, new PropertyPath("(UIElement.Effect).(BlurEffect.Radius)"));
            sb.Children.Add(blurOut);

            return sb;
        }

        // Custom Slide
        private Storyboard SbCustomSlideIn(Image target, Border borderDarken, AnimationParams p)
        {
            var sb = SbFadeIn(target, borderDarken, p);

            PropertyPath path;
            double from, to = 0;

            switch (p.SlideDirection)
            {
                case SlideDirection.FromLeft:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                    from = -Math.Abs(p.SlideDistance);
                    break;
                case SlideDirection.FromRight:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                    from = Math.Abs(p.SlideDistance);
                    break;
                case SlideDirection.FromTop:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)");
                    from = -Math.Abs(p.SlideDistance);
                    break;
                case SlideDirection.FromBottom:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)");
                    from = Math.Abs(p.SlideDistance);
                    break;
                default:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                    from = Math.Abs(p.SlideDistance);
                    break;
            }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(p.SlideDuration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, path);
            sb.Children.Add(anim);

            return sb;
        }

        private Storyboard SbCustomSlideOut(Image target, AnimationParams p)
        {
            var sb = SbFadeOut(target, p);

            PropertyPath path;
            double from = 0, to;

            switch (p.SlideDirection)
            {
                case SlideDirection.FromLeft:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                    to = Math.Abs(p.SlideDistance) * 0.5;
                    break;
                case SlideDirection.FromRight:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                    to = -Math.Abs(p.SlideDistance) * 0.5;
                    break;
                case SlideDirection.FromTop:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)");
                    to = Math.Abs(p.SlideDistance) * 0.5;
                    break;
                case SlideDirection.FromBottom:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)");
                    to = -Math.Abs(p.SlideDistance) * 0.5;
                    break;
                default:
                    path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                    to = -Math.Abs(p.SlideDistance) * 0.5;
                    break;
            }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(Math.Max(0.05, p.SlideDuration - 0.1)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, path);
            sb.Children.Add(anim);

            return sb;
        }

        // Smart Slide
        private Storyboard SbSmartSlideIn(Image target, Border borderDarken, AnimationParams p)
        {
            ResetTranslate(target);

            var dir = lockedSlideDirection;
            var sb = SbFadeIn(target, borderDarken, p);

            PropertyPath path;
            double from, to = 0;

            if (dir == SlideDirection.FromLeft || dir == SlideDirection.FromRight)
            {
                path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                from = (dir == SlideDirection.FromLeft ? -1 : 1) * Math.Abs(p.SlideDistance);
            }
            else
            {
                path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)");
                from = (dir == SlideDirection.FromTop ? -1 : 1) * Math.Abs(p.SlideDistance);
            }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(p.SlideDuration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, e) => ResetTranslate(target);

            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, path);
            sb.Children.Add(anim);

            return sb;
        }

        private Storyboard SbSmartSlideOut(Image target, AnimationParams p)
        {
            ResetTranslate(target);

            var dir = lockedSlideDirection;
            var sb = SbFadeOut(target, p);

            PropertyPath path;
            double from = 0, to;

            if (dir == SlideDirection.FromLeft || dir == SlideDirection.FromRight)
            {
                path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)");
                to = (dir == SlideDirection.FromLeft ? 1 : -1) * Math.Abs(p.SlideDistance) * 0.5;
            }
            else
            {
                path = new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)");
                to = (dir == SlideDirection.FromTop ? 1 : -1) * Math.Abs(p.SlideDistance) * 0.5;
            }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(Math.Max(0.05, p.SlideDuration - 0.1)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, e) => ResetTranslate(target);

            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, path);
            sb.Children.Add(anim);

            return sb;
        }

        


        // ---------------------- Pomocné metody ----------------------

        private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                    yield return t;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }
        private static bool IsFadeImageType(Type t)
        {
            while (t != null)
            {
                if (t.Name == "FadeImage")
                    return true;
                t = t.BaseType;
            }
            return false;
        }

        private static bool IsEffectivelyVisible(FrameworkElement fe)
        {
            return fe != null && fe.IsVisible && fe.ActualWidth > 0 && fe.ActualHeight > 0;
        }

        private static void FindAllFadeImages(DependencyObject root, List<FrameworkElement> sink)
        {
            if (root == null)
                return;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is FrameworkElement fe && IsFadeImageType(child.GetType()))
                {
                    sink.Add(fe);
                }

                FindAllFadeImages(child, sink);
            }
        }

        private void HookFadeImageWithLoadedCheck(FrameworkElement fadeImage)
        {
            // Nastav aktuální cíl a připoj Source watcher
            lastFadeImage = fadeImage;
            WatchFadeImageSourceChanges(fadeImage);


            if (!fadeImage.IsLoaded)
            {
                RoutedEventHandler onLoaded = null;
                onLoaded = (s, e) =>
                {
                    fadeImage.Loaded -= onLoaded;
                    ApplyCustomAnimations(fadeImage);
                };
                fadeImage.Loaded += onLoaded;
            }
            else
            {
                ApplyCustomAnimations(fadeImage);
            }
        }

        private static FrameworkElement FindChildByName(DependencyObject parent, string name)
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var fe = child as FrameworkElement;
                if (fe != null && fe.Name == name) return fe;
                var result = FindChildByName(child, name);
                if (result != null) return result;
            }
            return null;
        }
        private DependencyObject FindVisualChildByTypeName(DependencyObject parent, string typeName)
        {
            if (parent == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child.GetType().FullName == typeName)
                {
                    return child;
                }

                var deeper = FindVisualChildByTypeName(child, typeName);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void ResetTranslate(Image target)
        {
            if (target?.RenderTransform is TransformGroup group)
            {
                foreach (var t in group.Children)
                {
                    if (t is TranslateTransform tt)
                    {
                        tt.X = 0;
                        tt.Y = 0;
                    }
                }
            }
        }

        private void ApplyFixedSlideZoom(Image target, double slideDistance)
        {
            if (target == null)
                return;

            void apply()
            {
                if (target.RenderTransform is TransformGroup group &&
                    group.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform st &&
                    target.ActualWidth > 0 && target.ActualHeight > 0)
                {
                    double scaleFactor = (target.ActualWidth + 2 * slideDistance) / target.ActualWidth;
                    st.ScaleX = scaleFactor;
                    st.ScaleY = scaleFactor;
                }
            }

            if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
            {
                SizeChangedEventHandler once = null;
                once = (s, e) =>
                {
                    target.SizeChanged -= once;
                    apply();
                };
                target.SizeChanged += once;
            }
            else
            {
                apply();
            }
        }

        private void EnsureBlurEffect(Image img)
        {
            if (img.Effect is BlurEffect) return;
            img.Effect = new BlurEffect { Radius = 0.0, RenderingBias = RenderingBias.Performance };
        }

        private void ApplySourceUpdateDelay(FrameworkElement fadeImage, double delayMs)
        {
            try
            {
                var type = fadeImage.GetType();
                var prop = type.GetProperty("SourceUpdateDelay", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(fadeImage, delayMs);
                }
                else
                {
                    logger.Warn("[CustomFadeAnim] SourceUpdateDelay property not found or not writable on FadeImage.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[CustomFadeAnim] Failed to set SourceUpdateDelay.");
            }
        }


        // ---------------------- Settings UI ----------------------

        public override ISettings GetSettings(bool firstRunSettings) => settings;
        public override UserControl GetSettingsView(bool firstRunSettings) => new CustomFadeAnimSettingsView { DataContext = settings };
    }
}
