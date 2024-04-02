﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopClock.Properties;
using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using Humanizer;
using WpfWindowPlacement;

namespace DesktopClock;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
[ObservableObject]
public partial class MainWindow : Window
{
    private readonly SystemClockTimer _systemClockTimer;
    private TaskbarIcon _trayIcon;
    private TimeZoneInfo _timeZone;
    private SoundPlayer _soundPlayer;

    /// <summary>
    /// The date and time to countdown to, or null if regular clock is desired.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? _countdownTo;

    /// <summary>
    /// The current date and time in the selected time zone or countdown as a formatted string.
    /// </summary>
    [ObservableProperty]
    private string _currentTimeOrCountdownString;

    [ObservableProperty]
    private bool _isMouseDown = false;

    public static readonly double MaxSizeLog = 6.5;
    public static readonly double MinSizeLog = 2.7;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _timeZone = App.GetTimeZone();
        UpdateCountdownEnabled();

        Settings.Default.PropertyChanged += (s, e) => Dispatcher.Invoke(() => Settings_PropertyChanged(s, e));
        if (Settings.Default.Bounce != null)
        {
            Settings.Default.Bounce.PropertyChanged += (s, e) => Dispatcher.Invoke(() => Bounce_PropertyChanged(s, e));
        }

        // Not done through binding due to what's explained in the comment in HideForNow().
        ShowInTaskbar = Settings.Default.ShowInTaskbar;

        CurrentTimeOrCountdownString = Settings.Default.LastDisplay;

        _systemClockTimer = new();
        _systemClockTimer.SecondChanged += SystemClockTimer_SecondChanged;

        ContextMenu = Resources["MainContextMenu"] as ContextMenu;

        ConfigureTrayIcon(!Settings.Default.ShowInTaskbar, true);

        UpdateSoundPlayerEnabled();
    }

    /// <summary>
    /// Copies the current time string to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyToClipboard() => Clipboard.SetText(CurrentTimeOrCountdownString);

    /// <summary>
    /// Minimizes the window.
    /// </summary>
    [RelayCommand]
    public void HideForNow()
    {
        if (!Settings.Default.TipsShown.HasFlag(TeachingTips.HideForNow))
        {
            MessageBox.Show(this, "Clock will be minimized and can be opened again from the taskbar or system tray (if enabled).",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);

            Settings.Default.TipsShown |= TeachingTips.HideForNow;
        }

        // https://stackoverflow.com/a/28239057.
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
        ShowInTaskbar = Settings.Default.ShowInTaskbar;
    }

    /// <summary>
    /// Sets app's theme to given value.
    /// </summary>
    [RelayCommand]
    public void SetTheme(Theme theme) => Settings.Default.Theme = theme;

    /// <summary>
    /// Sets format string in settings to given string.
    /// </summary>
    [RelayCommand]
    public void SetFormat(string format) => Settings.Default.Format = format;

    /// <summary>
    /// Explains how to write a format, then asks user if they want to view a website and Advanced settings to do so.
    /// </summary>
    [RelayCommand]
    public void FormatWizard()
    {
        var result = MessageBox.Show(this,
            $"In advanced settings: edit \"{nameof(Settings.Default.Format)}\" using special \"Custom date and time format strings\", then save." +
            "\n\nOpen advanced settings and a tutorial now?",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK);

        if (result != MessageBoxResult.OK)
            return;

        Process.Start("https://learn.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings");
        OpenSettings();
    }

    /// <summary>
    /// Sets time zone ID in settings to given time zone ID.
    /// </summary>
    [RelayCommand]
    public void SetTimeZone(TimeZoneInfo tzi) => App.SetTimeZone(tzi);

    /// <summary>
    /// Creates a new clock executable and starts it.
    /// </summary>
    [RelayCommand]
    public void NewClock()
    {
        if (!Settings.Default.TipsShown.HasFlag(TeachingTips.NewClock))
        {
            var result = MessageBox.Show(this,
                "This will copy the executable and start it with new settings.\n\n" +
                "Continue?",
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK);

            if (result != MessageBoxResult.OK)
                return;

            Settings.Default.TipsShown |= TeachingTips.NewClock;
        }

        var newExePath = Path.Combine(App.MainFileInfo.DirectoryName, App.MainFileInfo.GetFileAtNextIndex().Name);

        // Copy and start the new clock.
        File.Copy(App.MainFileInfo.FullName, newExePath);
        Process.Start(newExePath);
    }

    /// <summary>
    /// Explains how to enable countdown mode, then asks user if they want to view Advanced settings to do so.
    /// </summary>
    [RelayCommand]
    public void CountdownWizard()
    {
        var result = MessageBox.Show(this,
            $"In advanced settings: change \"{nameof(Settings.Default.CountdownTo)}\" in the format of \"{default(DateTime)}\", then save." +
            "\n\nOpen advanced settings now?",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK);

        if (result != MessageBoxResult.OK)
            return;

        OpenSettings();
    }

    /// <summary>
    /// Opens the settings file in Notepad.
    /// </summary>
    [RelayCommand]
    public void OpenSettings()
    {
        // Teach user how it works.
        if (!Settings.Default.TipsShown.HasFlag(TeachingTips.AdvancedSettings))
        {
            MessageBox.Show(this,
                "Settings are stored in JSON format and will be opened in Notepad. Simply save the file to see your changes appear on the clock. To start fresh, delete your '.settings' file.",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);

            Settings.Default.TipsShown |= TeachingTips.AdvancedSettings;
        }

        // Save first if we can so it's up-to-date.
        if (Settings.CanBeSaved)
            Settings.Default.Save();

        // If it doesn't even exist then it's probably somewhere that requires special access and we shouldn't even be at this point.
        if (!Settings.Exists)
        {
            MessageBox.Show(this,
                "Settings file doesn't exist and couldn't be created.",
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Open settings file in notepad.
        try
        {
            Process.Start("notepad", Settings.FilePath);
        }
        catch (Exception ex)
        {
            // Lazy scammers on the Microsoft Store may reupload without realizing it gets sandboxed, making it unable to start the Notepad process (#1, #12).
            MessageBox.Show(this,
                "Couldn't open settings file.\n\n" +
                "This app may have be reuploaded without permission. If you paid for it, ask for a refund and download it for free from the original source: https://github.com/danielchalmers/DesktopClock.\n\n" +
                $"If it still doesn't work, create a new Issue at that link with details on what happened and include this error: \"{ex.Message}\"",
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens the GitHub Releases page.
    /// </summary>
    [RelayCommand]
    public void CheckForUpdates()
    {
        if (!Settings.Default.TipsShown.HasFlag(TeachingTips.CheckForUpdates))
        {
            var result = MessageBox.Show(this,
                "This will take you to a website to view the latest release.\n\n" +
                "Continue?",
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK);

            if (result != MessageBoxResult.OK)
                return;

            Settings.Default.TipsShown |= TeachingTips.CheckForUpdates;
        }

        Process.Start("https://github.com/danielchalmers/DesktopClock/releases");
    }

    /// <summary>
    /// Exits the program.
    /// </summary>
    [RelayCommand]
    public void Exit()
    {
        Close();
    }

    private void ConfigureTrayIcon(bool showIcon, bool firstLaunch)
    {
        if (showIcon)
        {
            if (_trayIcon == null)
            {
                _trayIcon = Resources["TrayIcon"] as TaskbarIcon;
                _trayIcon.ContextMenu = Resources["MainContextMenu"] as ContextMenu;
                _trayIcon.ContextMenu.DataContext = this;
                _trayIcon.ForceCreate(enablesEfficiencyMode: false);
                _trayIcon.TrayLeftMouseDoubleClick += (_, _) =>
                {
                    WindowState = WindowState.Normal;
                    Activate();
                };
            }

            if (!firstLaunch)
                _trayIcon.ShowNotification("Hidden from taskbar", "Icon was moved to the tray");
        }
        else
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Settings.Default.TimeZone):
                _timeZone = App.GetTimeZone();
                UpdateTimeString();
                break;

            case nameof(Settings.Default.Format):
            case nameof(Settings.Default.CountdownFormat):
                UpdateTimeString();
                break;

            case nameof(Settings.Default.ShowInTaskbar):
                ShowInTaskbar = Settings.Default.ShowInTaskbar;
                ConfigureTrayIcon(!Settings.Default.ShowInTaskbar, false);
                break;

            case nameof(Settings.Default.CountdownTo):
                UpdateCountdownEnabled();
                UpdateTimeString();
                break;

            case nameof(Settings.Default.WavFilePath):
            case nameof(Settings.Default.WavFileInterval):
                UpdateSoundPlayerEnabled();
                break;
        }
    }

    private void SystemClockTimer_SecondChanged(object sender, EventArgs e)
    {
        UpdateTimeString();

        UpdateBounce();

        TryPlaySound();
    }

    private void UpdateCountdownEnabled()
    {
        if (Settings.Default.CountdownTo == null || Settings.Default.CountdownTo == default(DateTime))
        {
            CountdownTo = null;
            return;
        }

        CountdownTo = Settings.Default.CountdownTo.Value.ToDateTimeOffset(_timeZone.BaseUtcOffset);
    }

    private void UpdateSoundPlayerEnabled()
    {
        var soundPlayerEnabled =
            !string.IsNullOrWhiteSpace(Settings.Default.WavFilePath) &&
            Settings.Default.WavFileInterval != default &&
            File.Exists(Settings.Default.WavFilePath);

        _soundPlayer = soundPlayerEnabled ? new() : null;
    }

    private void TryPlaySound()
    {
        if (_soundPlayer == null)
            return;

        var isOnInterval = CountdownTo == null ?
            (int)DateTimeOffset.Now.TimeOfDay.TotalSeconds % (int)Settings.Default.WavFileInterval.TotalSeconds == 0 :
            (int)(CountdownTo.Value - DateTimeOffset.Now).TotalSeconds % (int)Settings.Default.WavFileInterval.TotalSeconds == 0;

        if (!isOnInterval)
            return;

        try
        {
            _soundPlayer.SoundLocation = Settings.Default.WavFilePath;
            _soundPlayer.Play();
        }
        catch
        {
            // Ignore errors.
        }
    }

    private void UpdateTimeString()
    {
        string GetTimeString()
        {
            var timeInSelectedZone = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, _timeZone);

            if (CountdownTo == null)
            {
                return Tokenizer.FormatWithTokenizerOrFallBack(timeInSelectedZone, Settings.Default.Format, CultureInfo.DefaultThreadCurrentCulture);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Settings.Default.CountdownFormat))
                    return CountdownTo.Humanize(timeInSelectedZone);

                return Tokenizer.FormatWithTokenizerOrFallBack(Settings.Default.CountdownTo - timeInSelectedZone, Settings.Default.CountdownFormat, CultureInfo.DefaultThreadCurrentCulture);
            }
        }

        CurrentTimeOrCountdownString = GetTimeString();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        this.IsMouseDown = true;
        if (e.ChangedButton == MouseButton.Left && Settings.Default.DragToMove)
        {
            _systemClockTimer.Stop();
            DragMove();
            UpdateTimeString();
            _systemClockTimer.Start();
        }
    }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        this.IsMouseDown = false;
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CopyToClipboard();
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Amount of scroll that occurred and whether it was positive or negative.
            var steps = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;

            // Convert the height, adjust it, then convert back in the same way as the slider.
            var newHeightLog = Math.Log(Settings.Default.Height) + (steps * 0.15);
            var newHeightLogClamped = Math.Min(Math.Max(newHeightLog, MinSizeLog), MaxSizeLog);
            var exp = Math.Exp(newHeightLogClamped);

            // Apply the new height as an integer to make it easier for the user.
            Settings.Default.Height = (int)exp;
        }
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        this.SetPlacement(Settings.Default.Placement);

        UpdateTimeString();
        _systemClockTimer.Start();

        // Now that everything's been initially rendered and laid out, we can start listening for changes to the size to keep the window right-aligned.
        SizeChanged += Window_SizeChanged;
    }

    private void Window_ContentRendered(object sender, EventArgs e)
    {
        if (!Settings.CanBeSaved)
        {
            MessageBox.Show(this,
                "Settings can't be saved because of an access error.\n\n" +
                $"Make sure {Title} is in a folder that doesn't require admin privileges, " +
                "and that you got it from the original source: https://github.com/danielchalmers/DesktopClock.\n\n" +
                "If the problem still persists, feel free to create a new Issue at the above link with as many details as possible.",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        Settings.Default.LastDisplay = CurrentTimeOrCountdownString;
        Settings.Default.Placement = this.GetPlacement();

        // Stop the file watcher before saving.
        Settings.Default.Dispose();

        if (Settings.CanBeSaved)
            Settings.Default.Save();

        App.SetRunOnStartup(Settings.Default.RunOnStartup);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && Settings.Default.RightAligned)
        {
            var widthChange = e.NewSize.Width - e.PreviousSize.Width;
            Left -= widthChange;
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _systemClockTimer.Stop();
            EfficiencyModeUtilities.SetEfficiencyMode(true);
        }
        else
        {
            UpdateTimeString();
            _systemClockTimer.Start();
            EfficiencyModeUtilities.SetEfficiencyMode(false);
        }
    }

    //#region "Bounce - to prevent burn in"

    [ObservableProperty]
    private Thickness _bounceMargin = new(0);


    //Random.Shared is not available in .NET48
    private readonly Random _rnd = new();
    private DateTime _lastBounce;

    private void UpdateBounce()
    {

        if (
            (!(Settings.Default.Bounce?.Enabled).GetValueOrDefault()) ||
            Settings.Default.Bounce.Interval.TotalSeconds < 1
            )
        {
            //if Bounce is not enabled or the interval is invalid, effectively disbable the bounce
            this.BounceMargin = new Thickness(0);
        }
        else
        {

            if (this._lastBounce.Add(Settings.Default.Bounce.Interval) < DateTime.UtcNow)
            {
                //- note: it would be more elegant to use a modulus based interval detection
                //-       but that introduces a whole set of issues around rounding and possibly missing the exact second


                var h = Math.Max(10.0, Settings.Default.Bounce.HorizontalBounce);
                var v = Math.Max(10.0, Settings.Default.Bounce.VerticalBounce);

                var l = this._rnd.NextDouble() * h;
                var t = this._rnd.NextDouble() * v;

                this.BounceMargin = new Thickness(l, t, h - l, v - t);
                Debug.WriteLine($"Bounce {this.BounceMargin.ToString()}");

                this._lastBounce= DateTime.UtcNow;
            }
        }
            }
    private void Bounce_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        this._lastBounce = new DateTime(0);
        UpdateBounce();
    }

    //#endregion
}