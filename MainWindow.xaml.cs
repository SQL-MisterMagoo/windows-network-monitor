using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MMNetMon;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
	const string CategoryName = "Network Interface";
	const int firstColWidth = 90;
	readonly ApplicationDataContainer localSettings;

	DispatcherQueueTimer statsTimer;
	DispatcherQueueTimer configTimer;
	LimitedQueue<DataPoint> _statsDown = new(40);
	LimitedQueue<DataPoint> _statsUp = new(40);
	PerformanceCounter netReceived = null!;
	PerformanceCounter netSent = null!;
	ToolTip toolTip;
	string[] networks = [];
	readonly JsonSerializerOptions jsonOptions = new() { IncludeFields = true };

	string SelectedNetwork { get; set; }

	public MainWindow()
	{
		this.InitializeComponent();
		localSettings = ApplicationData.Current.LocalSettings;

		ConfigureRenderer();
		ConfigureUIElements();
		ConfigureWindow();
		ConfigureTimers();
		ReadSettings();
	}

	void ConfigureRenderer()
	{
		if (AppWindow.Presenter is OverlappedPresenter presenter)
		{
			presenter.IsMinimizable = false;
			presenter.IsMaximizable = false;
			presenter.IsAlwaysOnTop = true;
			presenter.IsResizable = true;
		}
	}

	void ConfigureUIElements()
	{
		networkSelector.DataContext = this;
		tbAverageDown.PointerPressed += (s, e) =>
		{
			networkSelector.IsDropDownOpen = true;
		};
		if (toolTip is null)
		{
			toolTip = new ToolTip();
			ToolTipService.SetToolTip(Content, toolTip);
		}
	}

	void ConfigureWindow()
	{
		AppWindow.SetIcon("favicon.ico");
		AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
		SystemBackdrop = new MicaBackdrop()
		{
			Kind = MicaKind.Base
		};
		AppWindow.Changed += (s, e) =>
		{
			if (e.DidSizeChange)
			{
				AppWindow.TitleBar.SetDragRectangles([new(0, 0, (int)AppWindow.Size.Width, (int)AppWindow.Size.Height)]);
				localSettings.Values["WindowSize"] = JsonSerializer.Serialize(AppWindow.Size, jsonOptions);
			}
			if (e.DidPositionChange)
				localSettings.Values["WindowPosition"] = JsonSerializer.Serialize(AppWindow.Position, jsonOptions);
		};
		if (localSettings.Values.TryGetValue("WindowSize", out object size))
		{
			SizeInt32 savedSize = JsonSerializer.Deserialize<SizeInt32>(size.ToString(), jsonOptions);
			if (savedSize.Width > 0 && savedSize.Height > 0)
			{
				AppWindow.Resize(savedSize);
			}
			else
			{
				AppWindow.Resize(new(GetWindowPreferredWidth(), (int)tbAverageDown.ActualHeight + 20));
			}
		}
		else
		{
			AppWindow.Resize(new(GetWindowPreferredWidth(), (int)tbAverageDown.ActualHeight + 20));
		}
		if (localSettings.Values.TryGetValue("WindowPosition", out object position))
		{
			PointInt32 savedPosition = JsonSerializer.Deserialize<PointInt32>(position.ToString(), jsonOptions);
			if (savedPosition.X > 0 && savedPosition.Y > 0)
			{
				AppWindow.Move(savedPosition);
			}
		}
	}

	void ConfigureTimers()
	{
		statsTimer = DispatcherQueue.CreateTimer();
		statsTimer.Interval = TimeSpan.FromMilliseconds(1000);
		statsTimer.Tick += HandleTimerUpdates;
		statsTimer.Start();
		configTimer = DispatcherQueue.CreateTimer();
		configTimer.Interval = TimeSpan.FromMilliseconds(500);
		configTimer.Tick += DiscoverNetworks;
		configTimer.Start();
	}

	void ReadSettings()
	{
		if (localSettings.Values.TryGetValue("Network", out object network))
		{
			SelectedNetwork = network.ToString();
		}
	}

	void HandleTimerUpdates(DispatcherQueueTimer sender, object args)
	{
		if (SelectedNetwork is string { Length: > 0 } network)
		{
			if (netReceived?.InstanceName == network)
			{
				float download = netReceived.NextValue();
				_statsDown.Enqueue(new((int)download, GetMaximumValueOrDefault, GetMaxHeightToBeRendered));
				tbAverageDown.Text = FormatSpeed(AverageValueDown);
				chartDown.ItemsSource = ChartDownData;
				chartDown.UpdateLayout();
				toolTip.Content = $"Chart Scale: {FormatSpeed(MaximumValueDown)}";
			}
			else
			{
				tbAverageDown.Text = "Connecting...";
				tbAverageDown.UpdateLayout();
				netReceived = new(CategoryName, "Bytes Received/sec", network);
			}
			if (netSent?.InstanceName == network)
			{
				float upload = netSent.NextValue();
				_statsUp.Enqueue(new((int)upload, GetMaximumValueOrDefault, GetMaxHeightToBeRendered));
				tbAverageUp.Text = FormatSpeed(AverageValueUp);
				chartUp.ItemsSource = ChartUpData;
				chartUp.UpdateLayout();
			}
			else
			{
				netSent = new(CategoryName, "Bytes Sent/sec", network);
			}
			ConfigureRenderer(); // make sure we really are on top as much as possible
		}
	}

	void DiscoverNetworks(DispatcherQueueTimer timer, object args)
	{
		timer.Stop();
		if (networks is null || networks.Length == 0)
		{
			PerformanceCounterCategory category = new PerformanceCounterCategory(CategoryName);
			networks = category.GetInstanceNames();
			networkSelector.ItemsSource = networks;
			networkSelector.UpdateLayout();
		}
	}
	void NetworkSelected(object sender, SelectionChangedEventArgs e)
	{
		SelectedNetwork = networkSelector.SelectedItem.ToString();
		localSettings.Values["Network"] = SelectedNetwork;
	}

	string FormatSpeed(float value)
	{
		if (value >= 1_000_000)
		{
			return (value / 1_000_000).ToString("F2") + " MB/s";
		}
		else if (value >= 1_000)
		{
			return (value / 1_000).ToString("F2") + " KB/s";
		}
		else
		{
			return value.ToString("F2") + " B/s";
		}
	}

	float MaximumValueDown => _statsDown?.MaxBy(d => d.Value)?.Value ?? 100;
	float MaximumValueUp => _statsUp?.MaxBy(d => d.Value)?.Value ?? 100;
	float AverageValueDown => _statsDown?.Reverse().Take(3).Average(d => d.Value) ?? 100;
	float AverageValueUp => _statsUp?.Reverse().Take(3).Average(d => d.Value) ?? 100;
	float GetMaximumDownValueOrDefault() => (float)(MaximumValueDown > 0 ? MaximumValueDown : chartDown.ActualHeight);
	float GetMaximumUpValueOrDefault() => (float)(MaximumValueUp > 0 ? MaximumValueUp : chartUp.ActualHeight);
	float GetMaximumValueOrDefault() => (float)Math.Max(GetMaximumDownValueOrDefault(), GetMaximumUpValueOrDefault());
	float GetMaxHeightToBeRendered() => (float)chartDown.ActualHeight;
	int GetWindowPreferredWidth() => (int)(firstColWidth + _statsDown.Limit * 4 + 6);
	DataPoint[] ChartDownData => _statsDown.ToArray();
	DataPoint[] ChartUpData => _statsUp.ToArray();
}
