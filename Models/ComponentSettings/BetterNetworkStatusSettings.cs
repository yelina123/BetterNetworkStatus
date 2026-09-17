using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterNetworkStatus.Models.ComponentSettings;

public enum NetworkDetectMode
{
    Auto,
    Icmp,
    Http
}

public enum SignalDisplayStyle
{
    BarsOnly,
    BarsWithNumber,
    NumberOnly
}

public partial class BetterNetworkStatusSettings : ObservableObject
{
    [ObservableProperty]
    private string _pingUrl = "https://www.baidu.com";

    [ObservableProperty]
    private NetworkDetectMode _detectMode = NetworkDetectMode.Auto;

    [ObservableProperty]
    private int _refreshIntervalSeconds = 2;

    [ObservableProperty]
    private SignalDisplayStyle _displayStyle = SignalDisplayStyle.BarsOnly;

    [ObservableProperty]
    private bool _useMonochrome = false;

    [ObservableProperty]
    private int _goodThresholdMs = 50;

    [ObservableProperty]
    private int _normalThresholdMs = 100;

    [ObservableProperty]
    private int _badThresholdMs = 300;
}
