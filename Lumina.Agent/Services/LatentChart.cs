using System;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace yanshuai
{
    /// <summary>
    /// S_t 潜状态轨迹绘制器。
    /// 在 Canvas 上绘制 Baseline (红色) vs PLAA (绿色) 的降维轨迹对比。
    /// </summary>
    public class LatentChartDrawer
    {
        /// <summary>在指定 Canvas 上绘制轨迹</summary>
        public void Draw(Canvas canvas, EvaluationResult result)
        {
            canvas.Children.Clear();

            if (result == null) return;
            if (result.BaselineTrajectory.Count == 0 && result.PlaaTrajectory.Count == 0)
            {
                canvas.Children.Add(new TextBlock
                {
                    Text = "无轨迹数据",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                return;
            }

            double chartW = canvas.ActualWidth > 10 ? canvas.ActualWidth - 20 : 500;
            double chartH = canvas.ActualHeight > 10 ? canvas.ActualHeight - 20 : 350;
            double marginL = 40, marginR = 20, marginT = 20, marginB = 30;
            double plotW = chartW - marginL - marginR;
            double plotH = chartH - marginT - marginB;

            // 合并所有点计算坐标范围
            var all = result.BaselineTrajectory.Concat(result.PlaaTrajectory).ToList();
            if (all.Count == 0) return;

            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X);
            double minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            double padX = (maxX - minX) * 0.15 + 0.5;
            double padY = (maxY - minY) * 0.15 + 0.5;
            minX -= padX; maxX += padX;
            minY -= padY; maxY += padY;

            Func<double, double> mx = (x) => marginL + (x - minX) / (maxX - minX) * plotW;
            Func<double, double> my = (y) => marginT + (1 - (y - minY) / (maxY - minY)) * plotH;

            // 网格
            var gridBrush = new SolidColorBrush(Color.FromArgb(25, 200, 200, 200));
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                AddLine(canvas, mx(minX + t * (maxX - minX)), my(minY), mx(minX + t * (maxX - minX)), my(maxY), gridBrush, 0.3);
                AddLine(canvas, mx(minX), my(minY + t * (maxY - minY)), mx(maxX), my(minY + t * (maxY - minY)), gridBrush, 0.3);
            }

            // 轴线
            var axisBrush = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200));
            AddLine(canvas, mx(minX), my(0), mx(maxX), my(0), axisBrush, 0.8);
            AddLine(canvas, mx(0), my(minY), mx(0), my(maxY), axisBrush, 0.8);

            // 绘制轨迹
            DrawOneTrajectory(canvas, result.BaselineTrajectory, Color.FromArgb(220, 220, 80, 80), "Baseline", mx, my);
            DrawOneTrajectory(canvas, result.PlaaTrajectory, Color.FromArgb(220, 80, 220, 80), "PLAA", mx, my);

            // 图例（右下）
            var legend = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(chartW - 180, chartH - 28, 0, 0),
            };
            legend.Children.Add(MakeDot("Baseline", Color.FromArgb(220, 220, 80, 80)));
            legend.Children.Add(MakeDot("PLAA", Color.FromArgb(220, 80, 220, 80)));
            canvas.Children.Add(legend);

            // 说明
            canvas.Children.Add(new TextBlock
            {
                Text = "S_t · PCA 降维 · 圆圈=起点 方块=终点",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(80, 180, 180, 180)),
                Margin = new Thickness(marginL, chartH - 16, 0, 0),
            });
        }

        private void DrawOneTrajectory(Canvas canvas,
            System.Collections.Generic.List<LatentPoint> points,
            Color color, string label,
            Func<double, double> mx, Func<double, double> my)
        {
            if (points.Count < 2) return;
            var brush = new SolidColorBrush(color);

            // 连线和数据点
            for (int i = 1; i < points.Count; i++)
            {
                AddLine(canvas, mx(points[i - 1].X), my(points[i - 1].Y),
                        mx(points[i].X), my(points[i].Y), brush, 0.4);
            }

            // 数据点
            for (int i = 0; i < points.Count; i++)
            {
                var dot = new Ellipse
                {
                    Width = 5, Height = 5,
                    Fill = brush,
                    Opacity = 0.7,
                };
                Canvas.SetLeft(dot, mx(points[i].X) - 2.5);
                Canvas.SetTop(dot, my(points[i].Y) - 2.5);
                canvas.Children.Add(dot);

                // 每 5 轮标序号
                if (i % 5 == 0)
                {
                    canvas.Children.Add(new TextBlock
                    {
                        Text = points[i].Label ?? i.ToString(),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(120, 180, 180, 180)),
                        Margin = new Thickness(mx(points[i].X) + 4, my(points[i].Y) - 5, 0, 0),
                    });
                }
            }

            // 起点：大圆圈
            var first = points.First();
            var startDot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = brush,
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5,
            };
            Canvas.SetLeft(startDot, mx(first.X) - 5);
            Canvas.SetTop(startDot, my(first.Y) - 5);
            canvas.Children.Add(startDot);

            // 终点：大方块
            var last = points.Last();
            var endRect = new Rectangle
            {
                Width = 10, Height = 10,
                Fill = brush,
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5,
            };
            Canvas.SetLeft(endRect, mx(last.X) - 5);
            Canvas.SetTop(endRect, my(last.Y) - 5);
            canvas.Children.Add(endRect);
        }

        private void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, double opacity)
        {
            canvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = brush,
                StrokeThickness = 0.5,
                Opacity = opacity,
            });
        }

        private StackPanel MakeDot(string text, Color color)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
            });
            p.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.LightGray),
            });
            return p;
        }
    }
}
