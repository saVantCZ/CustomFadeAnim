using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using static CustomFadeAnim.CustomFadeAnim;

namespace CustomFadeAnim
{
    public class CustomFadeAnimSettings : ObservableObject
    {
        private string selectedAnim = "Default";

        public string SelectedAnim
        {
            get => selectedAnim;
            set => SetValue(ref selectedAnim, value);
        }

        private double sourceUpdateDelay = 300;
        public double SourceUpdateDelay
        {
            get => sourceUpdateDelay;
            set => SetValue(ref sourceUpdateDelay, value);
        }

        [DontSerialize]
        public List<string> AvailableAnimations { get; set; } = new List<string>();

        public Dictionary<string, AnimationParams> AnimParams { get; set; } = new Dictionary<string, AnimationParams>();

    }

    [Flags]
    public enum SlideDirection
    {
        FromRight,
        FromLeft,
        FromBottom,
        FromTop
    }

    public enum AnimParamFlags
    {
        None = 0,
        Duration = 1,
        ZoomStart = 2,
        SlideDistance = 4,
        SlideDuration = 8,
        RotateAngle = 16,
        SkewAngle = 32,
        BlurRadius = 64,
        SlideDirection = 128
    }

    public class AnimationParams : ObservableObject
    {
        public AnimParamFlags UsedParams { get; set; } = AnimParamFlags.Duration;

        private double duration = 0;
        public double Duration
        {
            get => duration;
            set => SetValue(ref duration, value);
        }

        private double zoomStart = 0;
        public double ZoomStart
        {
            get => zoomStart;
            set => SetValue(ref zoomStart, value);
        }

        private double slideDistance = 0.0;
        public double SlideDistance
        {
            get => slideDistance;
            set => SetValue(ref slideDistance, value);
        }

        private double slideDuration = 0;
        public double SlideDuration
        {
            get => slideDuration;
            set => SetValue(ref slideDuration, value);
        }

        private double rotateAngle = 0;
        public double RotateAngle
        {
            get => rotateAngle;
            set => SetValue(ref rotateAngle, value);
        }

        private double skewAngle = 0;
        public double SkewAngle
        {
            get => skewAngle;
            set => SetValue(ref skewAngle, value);
        }

        private double blurRadius = 0;
        public double BlurRadius
        {
            get => blurRadius;
            set => SetValue(ref blurRadius, value);
        }

        private SlideDirection slideDirection = SlideDirection.FromRight;
        public SlideDirection SlideDirection
        {
            get => slideDirection;
            set => SetValue(ref slideDirection, value);
        }

    }

    public class CustomFadeAnimSettingsViewModel : ObservableObject, ISettings
    {
        private readonly CustomFadeAnim plugin;
        private CustomFadeAnimSettings editingClone;
        private static readonly ILogger logger = LogManager.GetLogger();

        public CustomFadeAnimSettings Settings { get; set; }

        public CustomFadeAnimSettingsViewModel(CustomFadeAnim plugin)
        {
            this.plugin = plugin;
            var saved = plugin.LoadPluginSettings<CustomFadeAnimSettings>();
            Settings = saved ?? new CustomFadeAnimSettings();

            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Settings.SelectedAnim))
                {
                    OnPropertyChanged(nameof(CurrentParams));
                    OnPropertyChanged(nameof(DurationVisibility));
                    OnPropertyChanged(nameof(ZoomStartVisibility));
                    OnPropertyChanged(nameof(SlideDistanceVisibility));
                    OnPropertyChanged(nameof(SlideDurationVisibility));
                    OnPropertyChanged(nameof(RotateAngleVisibility));
                    OnPropertyChanged(nameof(SkewAngleVisibility));
                    OnPropertyChanged(nameof(BlurRadiusVisibility));
                    OnPropertyChanged(nameof(SlideDirectionVisibility));
                }
            };
        }

        public AnimationParams CurrentParams
        {
            get
            {
                if (Settings == null || Settings.AnimParams == null || string.IsNullOrEmpty(Settings.SelectedAnim))
                {
                    return null;
                }

                AnimationParams p;
                if (Settings.AnimParams.TryGetValue(Settings.SelectedAnim, out p))
                {
                    return p;
                }
                return null;
            }
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(CurrentParams));
        }

        public void EndEdit()
        {
            ClampAll();
            plugin.SavePluginSettings(Settings);

            
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }

        // Defaultni parametry bro
        public void EnsureDefaults(List<string> animNames)
        {
            if (Settings.AnimParams == null)
            {
                Settings.AnimParams = new Dictionary<string, AnimationParams>();
            }

            foreach (var name in animNames)
            {
                AnimationParams p;
                if (!Settings.AnimParams.TryGetValue(name, out p))
                {
                    Settings.AnimParams[name] = GetDefaultParamsFor(name);
                }
                else
                {
                    var defaults = GetDefaultParamsFor(name);
                    if (p.UsedParams == AnimParamFlags.None || p.UsedParams != defaults.UsedParams)
                    {
                        p.UsedParams = defaults.UsedParams;
                    }
                }
            }

            if (string.IsNullOrEmpty(Settings.SelectedAnim) || !Settings.AnimParams.ContainsKey(Settings.SelectedAnim))
            {
                Settings.SelectedAnim = animNames.FirstOrDefault() ?? "Default";
            }

            OnPropertyChanged(nameof(CurrentParams));
        }

        public AnimationParams GetDefaultParamsFor(string name)
        {
            var key = name ?? string.Empty;
            AnimationParams result = null;

            if (key == AnimationNames.Default)
            {
                result = new AnimationParams
                {
                    Duration = 0.5,
                    UsedParams = AnimParamFlags.Duration
                };
            }
            else if (key == AnimationNames.CustomFade)
            {
                result = new AnimationParams
                {
                    Duration = 0.5,
                    UsedParams = AnimParamFlags.Duration
                };
            }
            else if (key == AnimationNames.FadeZoom)
            {
                result = new AnimationParams
                {
                    Duration = 0.5,
                    ZoomStart = 1.08,
                    UsedParams = AnimParamFlags.Duration | AnimParamFlags.ZoomStart
                };
            }
            else if (key == AnimationNames.PS5Slide)
            {
                result = new AnimationParams
                {
                    Duration = 0.4,
                    SlideDistance = 40.0,
                    SlideDuration = 0.4,
                    UsedParams = AnimParamFlags.Duration | AnimParamFlags.SlideDistance | AnimParamFlags.SlideDuration
                };
            }
            else if (key == AnimationNames.ZoomRotate)
            {
                result = new AnimationParams
                {
                    Duration = 0.5,
                    ZoomStart = 1.08,
                    RotateAngle = 8.0,
                    UsedParams = AnimParamFlags.Duration | AnimParamFlags.ZoomStart | AnimParamFlags.RotateAngle
                };
            }
            else if (key == AnimationNames.PageCurl)
            {
                result = new AnimationParams
                {
                    Duration = 0.5,
                    SkewAngle = 18.0,
                    UsedParams = AnimParamFlags.Duration | AnimParamFlags.SkewAngle
                };
            }
            else if (key == AnimationNames.BlurFade)
            {
                result = new AnimationParams
                {
                    Duration = 0.5,
                    BlurRadius = 40.0,
                    UsedParams = AnimParamFlags.Duration | AnimParamFlags.BlurRadius
                };
            }
            else if (key == AnimationNames.CustomSlide)
            {
                result = new AnimationParams
                {
                    Duration = 0.4,
                    SlideDistance = 40,
                    SlideDuration = 0.4,
                    SlideDirection = SlideDirection.FromRight,
                    UsedParams = AnimParamFlags.Duration | AnimParamFlags.SlideDistance | AnimParamFlags.SlideDuration | AnimParamFlags.SlideDirection
                };
            }

            return result ?? new AnimationParams { Duration = 0.6, UsedParams = AnimParamFlags.Duration };
        }

        public void ResetCurrentToDefaults()
        {
            if (string.IsNullOrEmpty(Settings.SelectedAnim))
                return;

            var defaults = GetDefaultParamsFor(Settings.SelectedAnim);
            if (Settings.AnimParams.ContainsKey(Settings.SelectedAnim))
            {
                Settings.AnimParams[Settings.SelectedAnim].Duration = defaults.Duration;
                Settings.AnimParams[Settings.SelectedAnim].ZoomStart = defaults.ZoomStart;
                Settings.AnimParams[Settings.SelectedAnim].SlideDistance = defaults.SlideDistance;
                Settings.AnimParams[Settings.SelectedAnim].SlideDuration = defaults.SlideDuration;
                Settings.AnimParams[Settings.SelectedAnim].RotateAngle = defaults.RotateAngle;
                Settings.AnimParams[Settings.SelectedAnim].SkewAngle = defaults.SkewAngle;
                Settings.AnimParams[Settings.SelectedAnim].BlurRadius = defaults.BlurRadius;
                Settings.AnimParams[Settings.SelectedAnim].UsedParams = defaults.UsedParams;
            }
            else
            {
                Settings.AnimParams[Settings.SelectedAnim] = defaults;
            }

            OnPropertyChanged(nameof(CurrentParams));
            OnPropertyChanged(nameof(DurationVisibility));
            OnPropertyChanged(nameof(ZoomStartVisibility));
            OnPropertyChanged(nameof(SlideDistanceVisibility));
            OnPropertyChanged(nameof(SlideDurationVisibility));
            OnPropertyChanged(nameof(RotateAngleVisibility));
            OnPropertyChanged(nameof(SkewAngleVisibility));
            OnPropertyChanged(nameof(BlurRadiusVisibility));
            OnPropertyChanged(nameof(SlideDirectionVisibility));
        }

        private void ClampAll()
        {
            foreach (var kv in Settings.AnimParams)
            {
                var p = kv.Value;
                p.Duration = Clamp(p.Duration, 0.05, 1.0);
                p.ZoomStart = Clamp(p.ZoomStart, 0.5, 2.0);
                p.SlideDistance = Clamp(p.SlideDistance, -200.0, 200.0);
                p.SlideDuration = Clamp(p.SlideDuration, 0.05, 1.0);
                p.RotateAngle = Clamp(p.RotateAngle, -180.0, 180.0);
                p.SkewAngle = Clamp(p.SkewAngle, -89.0, 89.0);
                p.BlurRadius = Clamp(p.BlurRadius, 0.0, 100.0);
            }
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static class SlideDirectionEnumValues
        {
            public static Array All => Enum.GetValues(typeof(SlideDirection));
        }

        public Visibility DurationVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.Duration))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public Visibility ZoomStartVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.ZoomStart))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public Visibility SlideDistanceVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.SlideDistance))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public Visibility SlideDurationVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.SlideDuration))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public Visibility RotateAngleVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.RotateAngle))
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public Visibility SkewAngleVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.SkewAngle))
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public Visibility BlurRadiusVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.BlurRadius))
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public Visibility SlideDirectionVisibility
        {
            get
            {
                var p = CurrentParams;
                return (p != null && p.UsedParams.HasFlag(AnimParamFlags.SlideDirection))
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }


    }

}
