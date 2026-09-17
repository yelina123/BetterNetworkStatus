using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using BetterNetworkStatus.Models.ComponentSettings;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace BetterNetworkStatus.Controls.Components;

[ComponentInfo(
    "28257767-8DEE-4F0B-8543-2527F837BF87",
    "迷你网络延迟",
    "\uEBE0",
    "用信号格小巧直观地显示网络延迟状态"
)]
public partial class BetterNetworkStatusComponent : ComponentBase<BetterNetworkStatusSettings>, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
    private bool _autoModeForceHttpUntilRestart;

    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#80808080"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Colors.Gray);

    private IBrush _bar1Brush = InactiveBrush;
    private IBrush _bar2Brush = InactiveBrush;
    private IBrush _bar3Brush = InactiveBrush;
    private IBrush _signalBrush = ErrorBrush;
    private string _delayText = "--";
    private bool _showBars = true;
    private bool _showNumber;
    private bool _isOffline;

    public IBrush Bar1Brush
    {
        get => _bar1Brush;
        set { _bar1Brush = value; OnPropertyChanged(nameof(Bar1Brush)); }
    }

    public IBrush Bar2Brush
    {
        get => _bar2Brush;
        set { _bar2Brush = value; OnPropertyChanged(nameof(Bar2Brush)); }
    }

    public IBrush Bar3Brush
    {
        get => _bar3Brush;
        set { _bar3Brush = value; OnPropertyChanged(nameof(Bar3Brush)); }
    }

    public IBrush SignalBrush
    {
        get => _signalBrush;
        set { _signalBrush = value; OnPropertyChanged(nameof(SignalBrush)); }
    }

    public string DelayText
    {
        get => _delayText;
        set { _delayText = value; OnPropertyChanged(nameof(DelayText)); }
    }

    public bool ShowBars
    {
        get => _showBars;
        set { _showBars = value; OnPropertyChanged(nameof(ShowBars)); }
    }

    public bool ShowNumber
    {
        get => _showNumber;
        set { _showNumber = value; OnPropertyChanged(nameof(ShowNumber)); }
    }

    public bool IsOffline
    {
        get => _isOffline;
        set { _isOffline = value; OnPropertyChanged(nameof(IsOffline)); }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public BetterNetworkStatusComponent()
    {
        InitializeComponent();

        _timer = new DispatcherTimer();
        _timer.Tick += OnTimerTicked;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "BetterNetworkStatus/1.0");
    }

    private void Component_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        ApplyDisplayStyle();
        RestartTimer();
        _ = CheckNetworkStatusAsync();
    }

    private void Component_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _timer.Stop();
        try { _httpClient.Dispose(); } catch { }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.DisplayStyle))
        {
            ApplyDisplayStyle();
            return;
        }

        if (e.PropertyName is nameof(Settings.DetectMode)
            or nameof(Settings.PingUrl)
            or nameof(Settings.GoodThresholdMs)
            or nameof(Settings.NormalThresholdMs)
            or nameof(Settings.BadThresholdMs)
            or nameof(Settings.UseMonochrome))
        {
            _ = CheckNetworkStatusAsync();
            return;
        }

        if (e.PropertyName == nameof(Settings.RefreshIntervalSeconds))
        {
            RestartTimer();
        }
    }

    private void ApplyDisplayStyle()
    {
        ShowBars = Settings.DisplayStyle != SignalDisplayStyle.NumberOnly;
        ShowNumber = Settings.DisplayStyle != SignalDisplayStyle.BarsOnly;
    }

    private void RestartTimer()
    {
        _timer.Stop();
        var sec = Math.Clamp(Settings.RefreshIntervalSeconds, 1, 60);
        _timer.Interval = TimeSpan.FromSeconds(sec);
        _timer.Start();
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        _ = CheckNetworkStatusAsync();
    }

    private async Task CheckNetworkStatusAsync()
    {
        if (!await _checkSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            var url = string.IsNullOrWhiteSpace(Settings.PingUrl)
                ? "https://www.baidu.com"
                : Settings.PingUrl;

            long delay;
            switch (Settings.DetectMode)
            {
                case NetworkDetectMode.Icmp:
                {
                    var icmpResult = await TryIcmpPingAsync(url);
                    if (!icmpResult.Success)
                    {
                        SetErrorStatus();
                        return;
                    }
                    delay = icmpResult.Delay;
                    break;
                }
                case NetworkDetectMode.Http:
                    delay = await TryHttpPingAsync(url);
                    break;
                case NetworkDetectMode.Auto:
                default:
                    if (!_autoModeForceHttpUntilRestart)
                    {
                        var autoIcmpResult = await TryIcmpPingAsync(url);
                        if (autoIcmpResult.Success)
                        {
                            delay = autoIcmpResult.Delay;
                            break;
                        }
                        _autoModeForceHttpUntilRestart = true;
                    }
                    delay = await TryHttpPingAsync(url);
                    break;
            }

            UpdateStatus(delay);
        }
        catch (TaskCanceledException)
        {
            SetErrorStatus();
        }
        catch (HttpRequestException)
        {
            SetErrorStatus();
        }
        catch
        {
            SetErrorStatus();
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    private async Task<IcmpProbeResult> TryIcmpPingAsync(string url)
    {
        try
        {
            var uri = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url)
                : new Uri($"https://{url}");

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(uri.Host, 2000);

            if (reply.Status == IPStatus.Success && reply.RoundtripTime > 0)
            {
                return IcmpProbeResult.Ok(reply.RoundtripTime);
            }

            return IcmpProbeResult.Fail();
        }
        catch (PingException)
        {
            return IcmpProbeResult.Fail();
        }
        catch
        {
            return IcmpProbeResult.Fail();
        }
    }

    private async Task<long> TryHttpPingAsync(string url)
    {
        var httpUrl = url;
        if (!httpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !httpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            httpUrl = "https://" + httpUrl;
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, httpUrl),
            HttpCompletionOption.ResponseHeadersRead);
        stopwatch.Stop();
        response.EnsureSuccessStatusCode();
        return stopwatch.ElapsedMilliseconds;
    }

    private void UpdateStatus(long delay)
    {
        var good = Math.Max(1, Settings.GoodThresholdMs);
        var normal = Math.Max(good + 1, Settings.NormalThresholdMs);
        var bad = Math.Max(normal + 1, Settings.BadThresholdMs);

        int level;
        IBrush activeBrush;

        if (delay < good)
        {
            level = 3;
            activeBrush = Settings.UseMonochrome
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.LimeGreen);
        }
        else if (delay < normal)
        {
            level = 3;
            activeBrush = Settings.UseMonochrome
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.GreenYellow);
        }
        else if (delay < bad)
        {
            level = 2;
            activeBrush = Settings.UseMonochrome
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Orange);
        }
        else
        {
            level = 1;
            activeBrush = Settings.UseMonochrome
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.OrangeRed);
        }

        Bar1Brush = level >= 1 ? activeBrush : InactiveBrush;
        Bar2Brush = level >= 2 ? activeBrush : InactiveBrush;
        Bar3Brush = level >= 3 ? activeBrush : InactiveBrush;
        SignalBrush = activeBrush;
        DelayText = $"{delay}ms";
        IsOffline = false;
    }

    private void SetErrorStatus()
    {
        Bar1Brush = InactiveBrush;
        Bar2Brush = InactiveBrush;
        Bar3Brush = InactiveBrush;
        SignalBrush = ErrorBrush;
        DelayText = "--";
        IsOffline = true;
    }

    private sealed record IcmpProbeResult(bool Success, long Delay)
    {
        public static IcmpProbeResult Ok(long delay) => new(true, delay);
        public static IcmpProbeResult Fail() => new(false, -1);
    }
}
