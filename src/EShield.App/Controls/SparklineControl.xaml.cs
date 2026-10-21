using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EShield.Models;

namespace EShield.Controls;

public partial class SparklineControl : UserControl
{
    private static readonly Brush _barActiveRecv = new SolidColorBrush(Color.FromRgb(56, 182, 255));
    private static readonly Brush _barActiveSent = new SolidColorBrush(Color.FromRgb(255, 100, 180));
    private static readonly Brush _barInactive = new SolidColorBrush(Color.FromRgb(55, 55, 55));

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(IReadOnlyList<(long sent, long recv)>),
        typeof(SparklineControl),
        new PropertyMetadata(null, OnDataChanged));

    public IReadOnlyList<(long sent, long recv)>? Data
    {
        get => (IReadOnlyList<(long sent, long recv)>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public SparklineControl() => InitializeComponent();

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparklineControl ctrl)
            ctrl.DrawBars();
    }

    private void OnSizeChanged(object s, SizeChangedEventArgs e) => DrawBars();

    private void DrawBars()
    {
        ChartCanvas.Children.Clear();
        const int count = 20;
        double w = ActualWidth > 1 ? ActualWidth : 60;
        double h = ActualHeight > 1 ? ActualHeight : 14;
        double barW = w / count;

        var data = Data;
        long maxVal = 1;
        if (data is { Count: > 0 })
            maxVal = Math.Max(1, data.Max(s => s.sent + s.recv));

        for (int i = 0; i < count; i++)
        {
            long sent = 0, recv = 0;
            if (data != null && i < data.Count)
            {
                sent = data[i].sent;
                recv = data[i].recv;
            }
            long total = sent + recv;

            double barH = total > 0 ? Math.Max(2, (double)total / maxVal * h) : 2;
            bool active = total > 0;

            var rect = new Rectangle
            {
                Width = Math.Max(1, barW - 0.5),
                Height = barH,
                Fill = active ? (recv >= sent ? _barActiveRecv : _barActiveSent) : _barInactive,
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            Canvas.SetLeft(rect, i * barW);
            Canvas.SetTop(rect, h - barH);
            ChartCanvas.Children.Add(rect);
        }
    }
}
