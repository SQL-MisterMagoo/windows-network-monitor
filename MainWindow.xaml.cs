using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MMNetMon;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
	private const string CategoryName = "Network Interface";
	public event PropertyChangedEventHandler PropertyChanged;
	private ObservableCollection<string> _networks;
	private string _selectedNetwork;
	PerformanceCounter netSent = null!;
	PerformanceCounter netReceived = null!;
	private PeriodicTimer timer;
	private string _upText;
	private string _downText;
	private LimitedQueue<DataPoint> _statsDown = new(30);
	private LimitedQueue<DataPoint> _statsUp = new(30);
	float MaximumValue => _statsDown?.MaxBy(d => d.Value)?.Value ?? 100;
	float AverageValue => _statsDown?.Reverse().Take(3).Average(d => d.Value) ?? 100;
	float HalfHeight => (float)itemsRepeater.ActualHeight / 2;
	float GetMax() => (float)(MaximumValue > 0 ? MaximumValue : itemsRepeater.ActualHeight);
	float MaxHeight() => (float)itemsRepeater.ActualHeight;
	DataPoint[] GridData => _statsDown.ToArray();
	public ObservableCollection<string> Networks
	{
		get => _networks;
		set
		{
			if (_networks != value)
			{
				_networks = value;
				OnPropertyChanged(nameof(Networks));
			}
		}
	}

	public string SelectedNetwork
	{
		get => _selectedNetwork;
		set
		{
			if (_selectedNetwork != value)
			{
				_selectedNetwork = value;
				OnPropertyChanged(nameof(SelectedNetwork));
			}
		}
	}
	public string UpText
	{
		get => _upText;
		set
		{
			if (_upText != value)
			{
				_upText = value;
				OnPropertyChanged(nameof(UpText));
			}
		}
	}
	public string DownText
	{
		get => _downText;
		set
		{
			if (_downText != value)
			{
				_downText = value;
				OnPropertyChanged(nameof(DownText));
			}
		}
	}

	public MainWindow()
	{
		this.InitializeComponent();
		this.AppWindow.IsShownInSwitchers = false;
		this.AppWindow.MoveInZOrderAtTop();
		this.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		this.AppWindow.SetIcon(null); // Hide the app in the taskbar
		AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
		AppWindow.Changed += (s, e) =>
		{
			AppWindow.TitleBar.SetDragRectangles([new(0, 0, (int)AppWindow.Size.Width, 32)]);
		};
		ConfigureRenderer();
		AppWindow.Resize(new(118, 65));
		cb1.DataContext = this;
		Networks = [];
		tbAverageDown.PointerPressed += (s, e) =>
		{
			cb1.IsDropDownOpen = true;
		};
		DiscoverNetworks();
	}

	private void ConfigureRenderer()
	{
		if (AppWindow.Presenter is OverlappedPresenter presenter)
		{
			presenter.IsMinimizable = false;
			presenter.IsMaximizable = false;
			presenter.IsAlwaysOnTop = true;
		}
	}

	async Task DiscoverNetworks()
	{
		await Task.Yield();
		UpText = "Warming up...";
		DownText = "Not long now...";
		await Task.Yield();
		if (Networks is null || Networks.Count == 0)
		{
			PerformanceCounterCategory category = new PerformanceCounterCategory(CategoryName);
			Networks = new(category.GetInstanceNames());
		}
	}
	private async void cb1_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (cb1.SelectedItem != null)
		{
			if (timer is PeriodicTimer t) t.Dispose();
			netSent = new(CategoryName, "Bytes Sent/sec", cb1.SelectedItem.ToString());
			netReceived = new(CategoryName, "Bytes Received/sec", cb1.SelectedItem.ToString());
			timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
			//Windows.Graphics.RectInt32 windowRect = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
			//var screenHeight = DisplayArea.GetFromRect(windowRect, DisplayAreaFallback.Nearest).OuterBounds.Height;
			while (await timer.WaitForNextTickAsync())
			{
				float upload = netSent.NextValue();
				_statsUp.Enqueue(new((int)upload,GetMax,MaxHeight));
				float download = netReceived.NextValue();
				_statsDown.Enqueue(new((int)download, GetMax, MaxHeight));
				tbAverageDown.Text = FormatSpeed(AverageValue);
				itemsRepeater.ItemsSource = GridData;
				itemsRepeater.UpdateLayout();
				ConfigureRenderer();
			}
		}
	}
	private void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	// function to convert a number representing bytes/sec to a human-readable string
	private string FormatSpeed(float value)
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
}
public class DataPoint(int Value, Func<float> getMax, Func<float> containerHeight)
{
	private readonly Func<float> getMax = getMax;
	private readonly float gotMax = (getMax() is { } m && m > 0f) ? containerHeight() * Value / getMax() : 0f;
	private readonly Func<float> containerHeight = containerHeight;

	public float Value { get; } = Value;
	public float ScaledValue => (getMax() is { } m && m > 0f) ? containerHeight() * Value / getMax() : 0f;
	public float ScaledY1 => containerHeight();
	public float ScaledY2 => containerHeight() - ScaledValue;
}
