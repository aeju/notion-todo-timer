using System.Windows;
using System.Windows.Media;

namespace FocusBar.Controls;

// 타임타이머처럼 12시 방향에서 반시계 방향으로 남은 시간만큼 빨간 부채꼴을 그린다.
public sealed class TimerDial : FrameworkElement
{
    private static readonly Brush Face = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)));
    private static readonly Pen Rim = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xA8)), 1));
    private static readonly Pen Tick = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x88)), 1));

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(TimerDial),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = size / 2 - 1;

        dc.DrawEllipse(Face, Rim, center, r, r);

        // 5분 간격 눈금 12개
        for (var i = 0; i < 12; i++)
        {
            var a = (i * 30 - 90) * Math.PI / 180;
            var outer = new Point(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
            var inner = new Point(center.X + r * 0.85 * Math.Cos(a), center.Y + r * 0.85 * Math.Sin(a));
            dc.DrawLine(Tick, inner, outer);
        }

        var f = Math.Clamp(Fraction, 0, 1);
        if (f <= 0) return;
        if (f >= 0.9999)
        {
            dc.DrawEllipse(Red, null, center, r * 0.92, r * 0.92);
            return;
        }

        var rr = r * 0.92;
        var sweep = f * 360;
        var endRad = (-90 - sweep) * Math.PI / 180;
        var top = new Point(center.X, center.Y - rr);
        var end = new Point(center.X + rr * Math.Cos(endRad), center.Y + rr * Math.Sin(endRad));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(center, isFilled: true, isClosed: true);
            ctx.LineTo(top, isStroked: false, isSmoothJoin: false);
            ctx.ArcTo(end, new Size(rr, rr), 0, isLargeArc: sweep > 180,
                      SweepDirection.Counterclockwise, isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(Red, null, geometry);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
