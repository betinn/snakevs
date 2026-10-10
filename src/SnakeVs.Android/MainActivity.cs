using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Window;
using Microsoft.Xna.Framework;
using SnakeVs.Game;
using SnakeVs.Game.Platform;

namespace SnakeVs.Droid;

[Activity(
    Label = "@string/app_name",
    MainLauncher = true,
    Exported = true,
    Icon = "@mipmap/ic_launcher",
    RoundIcon = "@mipmap/ic_launcher_round",
    Theme = "@style/AppTheme",
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.SensorPortrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
                           ConfigChanges.UiMode | ConfigChanges.Navigation | ConfigChanges.Density)]
public class MainActivity : AndroidGameActivity, IPlatformServices
{
    private SnakeGame? _game;
    private View? _view;
    private BackCallback? _backCallback;
    private volatile bool _backRequested;
    private volatile int _insetLeft;
    private volatile int _insetTop;
    private volatile int _insetRight;
    private volatile int _insetBottom;

    public bool IsMobile => true;

    public string DataDirectory => FilesDir?.AbsolutePath ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && Window?.Attributes is { } attributes)
        {
            attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            Window.Attributes = attributes;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Android 13+ (and mandatory when targeting 36): back gestures arrive through this callback.
            _backCallback = new BackCallback(() => _backRequested = true);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, _backCallback);
        }

        _game = new SnakeGame(this);
        _view = (View)_game.Services.GetService(typeof(View))!;
        SetContentView(_view);
        HideSystemBars();
        _game.Run();
    }

    protected override void OnResume()
    {
        base.OnResume();
        HideSystemBars();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (!hasFocus) return;
        HideSystemBars();
        Window?.DecorView.Post(UpdateInsets);
    }

    // Only called below Android 13; newer versions use OnBackInvokedDispatcher.
#pragma warning disable CA1422, CS0672
    public override void OnBackPressed()
    {
        // Never let the system close the activity: the game decides (pause, previous screen, or leave).
        _backRequested = true;
    }
#pragma warning restore CA1422, CS0672

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back)
        {
            _backRequested = true;
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }

    protected override void OnDestroy()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && _backCallback is not null)
        {
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backCallback);
        }

        base.OnDestroy();
    }

    public SafeInsets GetSafeInsets() => new(_insetLeft, _insetTop, _insetRight, _insetBottom);

    public bool ConsumeBackRequest()
    {
        if (!_backRequested) return false;
        _backRequested = false;
        return true;
    }

    public void Vibrate(int milliseconds)
    {
        try
        {
            Vibrator? vibrator;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                var manager = (VibratorManager?)GetSystemService(VibratorManagerService);
                vibrator = manager?.DefaultVibrator;
            }
            else
            {
#pragma warning disable CA1422 // Legacy service is the only option before Android 12.
                vibrator = (Vibrator?)GetSystemService(VibratorService);
#pragma warning restore CA1422
            }

            if (vibrator is null || !vibrator.HasVibrator) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                vibrator.Vibrate(VibrationEffect.CreateOneShot(milliseconds, VibrationEffect.DefaultAmplitude));
            }
            else
            {
#pragma warning disable CA1422
                vibrator.Vibrate(milliseconds);
#pragma warning restore CA1422
            }
        }
        catch (Exception)
        {
            // Haptics are a nice-to-have; never crash because of them.
        }
    }

    public void ExitApp() => RunOnUiThread(() => MoveTaskToBack(true));

    private void HideSystemBars()
    {
        var window = Window;
        if (window is null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(35))
            {
#pragma warning disable CA1422 // Edge-to-edge is already enforced from Android 15.
                window.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1422
            }

            var controller = window.InsetsController;
            if (controller is not null)
            {
                controller.Hide(WindowInsets.Type.SystemBars());
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
        }
        else
        {
#pragma warning disable CA1422, CS0618
            window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation |
                SystemUiFlags.LayoutFullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen);
#pragma warning restore CA1422, CS0618
        }
    }

    private void UpdateInsets()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) return;
        var cutout = Window?.DecorView.RootWindowInsets?.DisplayCutout;
        _insetLeft = cutout?.SafeInsetLeft ?? 0;
        _insetTop = cutout?.SafeInsetTop ?? 0;
        _insetRight = cutout?.SafeInsetRight ?? 0;
        _insetBottom = cutout?.SafeInsetBottom ?? 0;
    }

    private sealed class BackCallback(Action onBack) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked() => onBack();
    }
}
