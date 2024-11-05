using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MMNetMon;

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
