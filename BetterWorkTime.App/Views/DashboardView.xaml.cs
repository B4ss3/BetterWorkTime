using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BetterWorkTime.Data.Sqlite;

namespace BetterWorkTime.App.Views;

public partial class DashboardView : UserControl
{
    private readonly string _dbPath;
    private int _periodIndex;
    private int _periodOffset;
    private bool _showPie = true;

    private string? _drillProjectId;
    private string? _drillProjectName;

    private List<string> _globalProjectOrder = [];

    private static readonly Color[] Palette =
    [
        Color.FromRgb(74,  127, 165),
        Color.FromRgb(61,  139, 94),
        Color.FromRgb(168, 64,  64),
        Color.FromRgb(107, 91,  158),
        Color.FromRgb(161, 130, 30),
        Color.FromRgb(52,  152, 185),
        Color.FromRgb(180, 95,  50),
        Color.FromRgb(100, 100, 160),
    ];

    public DashboardView(string dbPath)
    {
        InitializeComponent();
        _dbPath = dbPath;

        PeriodCombo.Items.Add("Today");
        PeriodCombo.Items.Add("This Week");
        PeriodCombo.Items.Add("This Month");
        PeriodCombo.Items.Add("Last Month");
        PeriodCombo.SelectedIndex = 1;

        Loaded += (_, _) => Refresh();
    }

    // ── Period resolution ─────────────────────────────────────────────────────

    private (long StartUtc, long EndUtc, string Display) GetRange()
    {
        return _periodIndex switch
        {
            0 => GetDayRange(DateTime.Today.AddDays(_periodOffset)),
            1 => GetWeekRange(_periodOffset),
            2 => GetMonthRange(0 + _periodOffset),
            3 => GetMonthRange(-1 + _periodOffset),
            _ => GetWeekRange(0)
        };
    }

    private static (long, long, string) GetDayRange(DateTime day)
    {
        var start = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
        return (start.ToUnixTimeSeconds(), start.AddDays(1).ToUnixTimeSeconds(), day.ToString("ddd, MMM d"));
    }

    private static (long, long, string) GetWeekRange(int weekOffset)
    {
        var today  = DateTime.Today;
        var dow    = (int)today.DayOfWeek;
        var monday = today.AddDays(-(dow == 0 ? 6 : dow - 1)).AddDays(weekOffset * 7);
        var s = new DateTimeOffset(monday, TimeZoneInfo.Local.GetUtcOffset(monday));
        var e = s.AddDays(7);
        var label = weekOffset == 0 ? "This Week" : weekOffset == -1 ? "Last Week" : $"Week of {monday:MMM d}";
        return (s.ToUnixTimeSeconds(), e.ToUnixTimeSeconds(), label);
    }

    private static (long, long, string) GetMonthRange(int monthOffset)
    {
        var today = DateTime.Today;
        var first = new DateTime(today.Year, today.Month, 1).AddMonths(monthOffset);
        var s = new DateTimeOffset(first, TimeZoneInfo.Local.GetUtcOffset(first));
        var e = new DateTimeOffset(first.AddMonths(1), TimeZoneInfo.Local.GetUtcOffset(first.AddMonths(1)));
        return (s.ToUnixTimeSeconds(), e.ToUnixTimeSeconds(), first.ToString("MMMM yyyy"));
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void Refresh()
    {
        var (startUtc, endUtc, display) = GetRange();
        PeriodLabel.Text = display;

        var query = new ReportQuery
        {
            StartUtc      = startUtc,
            EndUtc        = endUtc,
            IncludeIdle   = false,
            IncludePauses = false,
        };

        var entries  = new ReportRepository(_dbPath).GetEntries(query);
        var projects = new ProjectRepository(_dbPath).GetAll()
            .Where(p => !p.Archived)
            .ToDictionary(p => p.Id, p => p);

        _globalProjectOrder = entries
            .GroupBy(e => e.ProjectId ?? "")
            .OrderByDescending(g => g.Sum(e => e.DurationSec))
            .Select(g => g.Key)
            .ToList();

        RefreshSummaryCards(entries, startUtc, endUtc);
        RenderDailyChart(entries, startUtc, endUtc, projects);
        RenderProjectPanel(entries, projects);
    }

    // ── Summary cards ─────────────────────────────────────────────────────────

    private void RefreshSummaryCards(IReadOnlyList<ReportEntryRow> entries,
                                     long startUtc, long endUtc)
    {
        var totalSec = entries.Sum(e => e.DurationSec);
        CardTotal.Text    = FormatDuration(totalSec);
        CardTotalSub.Text = $"{entries.Count} entries";

        var workedDays = entries
            .Select(e => DateTimeOffset.FromUnixTimeSeconds(e.StartUtc).LocalDateTime.Date)
            .Distinct().Count();
        CardDays.Text = workedDays.ToString();
        var spanDays  = Math.Max(1, (DateTimeOffset.FromUnixTimeSeconds(endUtc).LocalDateTime.Date
                                   - DateTimeOffset.FromUnixTimeSeconds(startUtc).LocalDateTime.Date).Days);
        CardDaysSub.Text = $"of {spanDays} day{(spanDays == 1 ? "" : "s")}";

        var avgSec = workedDays > 0 ? totalSec / workedDays : 0;
        CardAvg.Text    = FormatDuration(avgSec);
        CardAvgSub.Text = workedDays > 0 ? $"over {workedDays} day{(workedDays == 1 ? "" : "s")}" : "no data";

        var topProject = entries
            .Where(e => e.ProjectName != null)
            .GroupBy(e => e.ProjectName!)
            .OrderByDescending(g => g.Sum(e => e.DurationSec))
            .FirstOrDefault();
        CardTopProject.Text    = topProject?.Key ?? "—";
        CardTopProjectSub.Text = topProject != null ? FormatDuration(topProject.Sum(e => e.DurationSec)) : "";
    }

    // ── Daily stacked bar chart ───────────────────────────────────────────────

    private void RenderDailyChart(IReadOnlyList<ReportEntryRow> entries,
                                  long startUtc, long endUtc,
                                  Dictionary<string, ProjectRow> projects)
    {
        DailyChart.Children.Clear();

        var startDate = DateTimeOffset.FromUnixTimeSeconds(startUtc).LocalDateTime.Date;
        var endDate   = DateTimeOffset.FromUnixTimeSeconds(endUtc).LocalDateTime.Date;
        var days      = Enumerable.Range(0, Math.Max(1, (endDate - startDate).Days))
                                  .Select(i => startDate.AddDays(i)).ToList();

        var byDay = entries
            .GroupBy(e => DateTimeOffset.FromUnixTimeSeconds(e.StartUtc).LocalDateTime.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var maxSec = Math.Max(3600,
            days.Select(d => byDay.TryGetValue(d, out var es) ? es.Sum(e => e.DurationSec) : 0)
                .DefaultIfEmpty(0).Max());

        const double chartH  = 240.0;
        const double barW    = 36.0;
        const double barGap  = 12.0;
        const double padLeft = 8.0;
        const double yTickW  = 36.0;

        var totalW = padLeft + yTickW + days.Count * (barW + barGap) + barGap;
        DailyChart.Width  = Math.Max(totalW, DailyChart.ActualWidth);
        DailyChart.Height = chartH + 30 + 8;

        var maxH     = maxSec / 3600.0;
        var tickStep = maxH <= 4 ? 0.5 : maxH <= 8 ? 1.0 : 2.0;
        for (double h = 0; h <= maxH + 0.01; h += tickStep)
        {
            var y = chartH - (h / maxH) * chartH;
            DailyChart.Children.Add(new Line
            {
                X1 = padLeft + yTickW, Y1 = y, X2 = totalW, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection([4, 4])
            });
            var lbl = new TextBlock
            {
                Text = h % 1 == 0 ? $"{(int)h}h" : $"{h:F1}h",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 160))
            };
            Canvas.SetLeft(lbl, padLeft); Canvas.SetTop(lbl, y - 8);
            DailyChart.Children.Add(lbl);
        }

        var colorMap = BuildProjectColorMap(entries, projects);

        for (int i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var x   = padLeft + yTickW + barGap + i * (barW + barGap);

            if (byDay.TryGetValue(day, out var dayEntries) && dayEntries.Count > 0)
            {
                var totalDaySec = dayEntries.Sum(e => e.DurationSec);

                var byProject = _globalProjectOrder
                    .Select(pid => (Id: pid, Entries: dayEntries.Where(e => (e.ProjectId ?? "") == pid).ToList()))
                    .Where(x2 => x2.Entries.Count > 0)
                    .ToList();

                double stackY = chartH;
                foreach (var (pid, projEntries) in byProject)
                {
                    var sec   = projEntries.Sum(e => e.DurationSec);
                    var h     = (sec / (double)maxSec) * chartH;
                    var color = colorMap.TryGetValue(pid, out var c) ? c : Palette[0];

                    var rect = new Rectangle
                    {
                        Width = barW, Height = Math.Max(h, 2),
                        Fill = new SolidColorBrush(color),
                        RadiusX = 3, RadiusY = 3
                    };
                    Canvas.SetLeft(rect, x); Canvas.SetTop(rect, stackY - h);
                    var projName = projects.TryGetValue(pid, out var pr) ? pr.Name : "(Unassigned)";
                    rect.ToolTip = $"{projName}: {FormatDuration(sec)}";
                    DailyChart.Children.Add(rect);
                    stackY -= h;
                }

                var topLabel = new TextBlock
                {
                    Text = FormatDurationShort(totalDaySec),
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 70, 90)),
                    TextAlignment = TextAlignment.Center, Width = barW
                };
                Canvas.SetLeft(topLabel, x); Canvas.SetTop(topLabel, stackY - 16);
                DailyChart.Children.Add(topLabel);
            }
            else
            {
                var rect = new Rectangle
                {
                    Width = barW, Height = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    RadiusX = 2, RadiusY = 2
                };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, chartH - 3);
                DailyChart.Children.Add(rect);
            }

            var dayLbl = new TextBlock
            {
                Text = days.Count <= 7 ? day.ToString("ddd") : day.ToString("M/d"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 130, 150)),
                TextAlignment = TextAlignment.Center, Width = barW
            };
            Canvas.SetLeft(dayLbl, x); Canvas.SetTop(dayLbl, chartH + 6);
            DailyChart.Children.Add(dayLbl);
        }
    }

    // ── Project panel (overview or drill-down) ────────────────────────────────

    private void RenderProjectPanel(IReadOnlyList<ReportEntryRow> entries,
                                    Dictionary<string, ProjectRow> projects)
    {
        if (_drillProjectId != null)
        {
            DrillBreadcrumb.Visibility = Visibility.Visible;
            DrillTitle.Text            = $"/ {_drillProjectName}";
            RenderTaskDrillDown(entries, projects);
        }
        else
        {
            DrillBreadcrumb.Visibility = Visibility.Collapsed;
            RenderProjectChart(entries, projects);
        }
    }

    private void RenderProjectChart(IReadOnlyList<ReportEntryRow> entries,
                                    Dictionary<string, ProjectRow> projects)
    {
        ProjectChart.Children.Clear();

        var byProject = _globalProjectOrder
            .Select(pid => (
                Id:   pid,
                Name: projects.TryGetValue(pid, out var p) ? p.Name : "(Unassigned)",
                Sec:  entries.Where(e => (e.ProjectId ?? "") == pid).Sum(e => e.DurationSec)
            ))
            .Where(x => x.Sec > 0)
            .ToList();

        if (byProject.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No data for this period.", FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 170))
            };
            Canvas.SetLeft(empty, 12); Canvas.SetTop(empty, 20);
            ProjectChart.Children.Add(empty);
            ProjectChart.Height = 60;
            return;
        }

        var colorMap = BuildProjectColorMap(entries, projects);
        var totalSec = byProject.Sum(x => x.Sec);

        if (_showPie)
            RenderPie(byProject, colorMap, totalSec, onClickProjectId: id => DrillInto(id, byProject.First(x => x.Id == id).Name, entries, projects));
        else
            RenderProjectBar(byProject, colorMap, totalSec, onClickProjectId: id => DrillInto(id, byProject.First(x => x.Id == id).Name, entries, projects));
    }

    private void DrillInto(string projectId, string projectName,
                           IReadOnlyList<ReportEntryRow> entries,
                           Dictionary<string, ProjectRow> projects)
    {
        _drillProjectId   = projectId;
        _drillProjectName = projectName;
        DrillBreadcrumb.Visibility = Visibility.Visible;
        DrillTitle.Text            = $"/ {projectName}";
        RenderTaskDrillDown(entries, projects);
    }

    private void RenderTaskDrillDown(IReadOnlyList<ReportEntryRow> entries,
                                     Dictionary<string, ProjectRow> projects)
    {
        ProjectChart.Children.Clear();

        var projectEntries = entries.Where(e => (e.ProjectId ?? "") == _drillProjectId).ToList();

        var byTask = projectEntries
            .GroupBy(e => string.IsNullOrWhiteSpace(e.TaskName) ? "(No task)" : e.TaskName)
            .Select(g => (Name: g.Key, Sec: g.Sum(e => e.DurationSec)))
            .OrderByDescending(x => x.Sec)
            .ToList();

        if (byTask.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No entries for this project.", FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 170))
            };
            Canvas.SetLeft(empty, 12); Canvas.SetTop(empty, 20);
            ProjectChart.Children.Add(empty);
            ProjectChart.Height = 60;
            return;
        }

        var totalSec = byTask.Sum(x => x.Sec);
        var maxSec   = byTask[0].Sec;

        var colorMap = BuildProjectColorMap(entries, projects);
        var baseColor = colorMap.TryGetValue(_drillProjectId!, out var bc) ? bc : Palette[0];

        const double padLeft = 10, padRight = 55, rowH = 30, rowGap = 8;
        ProjectChart.Height = byTask.Count * (rowH + rowGap) + 20;
        double availW = ProjectChart.ActualWidth > 0 ? ProjectChart.ActualWidth - padLeft - padRight : 200;

        for (int i = 0; i < byTask.Count; i++)
        {
            var (name, sec) = byTask[i];
            var share = sec / (double)totalSec;
            var y     = 10 + i * (rowH + rowGap);
            var barW  = (sec / (double)maxSec) * availW;

            var color = LightenColor(baseColor, i * 0.1);

            var rect = new Rectangle
            {
                Width = Math.Max(barW, 4), Height = rowH,
                Fill = new SolidColorBrush(color),
                RadiusX = 4, RadiusY = 4
            };
            rect.ToolTip = $"{name}: {FormatDuration(sec)} ({share:P0})";
            Canvas.SetLeft(rect, padLeft); Canvas.SetTop(rect, y);
            ProjectChart.Children.Add(rect);

            var lbl = new TextBlock
            {
                Text = $"{name}  {FormatDurationShort(sec)}",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Canvas.SetLeft(lbl, padLeft + 8); Canvas.SetTop(lbl, y + (rowH - 14) / 2);
            ProjectChart.Children.Add(lbl);

            var shareLbl = new TextBlock
            {
                Text = $"{share:P0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130))
            };
            Canvas.SetLeft(shareLbl, padLeft + barW + 6); Canvas.SetTop(shareLbl, y + (rowH - 14) / 2);
            ProjectChart.Children.Add(shareLbl);
        }
    }

    // ── Pie chart ─────────────────────────────────────────────────────────────

    private void RenderPie(List<(string Id, string Name, long Sec)> data,
                           Dictionary<string, Color> colorMap, long totalSec,
                           Action<string> onClickProjectId)
    {
        const double cx = 110, cy = 110, r = 95;
        ProjectChart.Height = 240 + data.Count * 22 + 10;

        double startAngle = -Math.PI / 2;
        for (int i = 0; i < data.Count; i++)
        {
            var item  = data[i];
            var share = item.Sec / (double)totalSec;
            var sweep = share * 2 * Math.PI;
            var color = colorMap.TryGetValue(item.Id, out var c) ? c : Palette[i % Palette.Length];

            if (share >= 0.9999)
            {
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    Fill = new SolidColorBrush(color),
                    Cursor = Cursors.Hand
                };
                ellipse.ToolTip = $"{item.Name}: {FormatDuration(item.Sec)} — click to drill down";
                var capturedId = item.Id;
                ellipse.MouseLeftButtonUp += (_, _) => onClickProjectId(capturedId);
                Canvas.SetLeft(ellipse, cx - r); Canvas.SetTop(ellipse, cy - r);
                ProjectChart.Children.Add(ellipse);
            }
            else
            {
                var endAngle = startAngle + sweep;
                var x1 = cx + r * Math.Cos(startAngle); var y1 = cy + r * Math.Sin(startAngle);
                var x2 = cx + r * Math.Cos(endAngle);   var y2 = cy + r * Math.Sin(endAngle);

                var seg = new PathFigure { StartPoint = new Point(cx, cy) };
                seg.Segments.Add(new LineSegment(new Point(x1, y1), true));
                seg.Segments.Add(new ArcSegment(new Point(x2, y2), new Size(r, r), 0,
                    sweep > Math.PI, SweepDirection.Clockwise, true));
                seg.Segments.Add(new LineSegment(new Point(cx, cy), true));

                var path = new System.Windows.Shapes.Path
                {
                    Data = new PathGeometry([seg]),
                    Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand
                };
                path.ToolTip = $"{item.Name}: {FormatDuration(item.Sec)} ({share:P0}) — click to drill down";
                var capturedId = item.Id;
                path.MouseLeftButtonUp += (_, _) => onClickProjectId(capturedId);
                ProjectChart.Children.Add(path);
            }
            startAngle += sweep;
        }

        double legY = cy * 2 + 12;
        for (int i = 0; i < data.Count; i++)
        {
            var item  = data[i];
            var share = item.Sec / (double)totalSec;
            var color = colorMap.TryGetValue(item.Id, out var c) ? c : Palette[i % Palette.Length];

            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(color), Cursor = Cursors.Hand };
            Canvas.SetLeft(dot, 12); Canvas.SetTop(dot, legY + 5);
            var capturedId = item.Id;
            dot.MouseLeftButtonUp += (_, _) => onClickProjectId(capturedId);
            ProjectChart.Children.Add(dot);

            var lbl = new TextBlock
            {
                Text = $"{item.Name}  {share:P0}  ({FormatDuration(item.Sec)})",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 70, 90)),
                Cursor = Cursors.Hand
            };
            lbl.MouseLeftButtonUp += (_, _) => onClickProjectId(capturedId);
            Canvas.SetLeft(lbl, 28); Canvas.SetTop(lbl, legY + 2);
            ProjectChart.Children.Add(lbl);
            legY += 22;
        }
    }

    private void RenderProjectBar(List<(string Id, string Name, long Sec)> data,
                                  Dictionary<string, Color> colorMap, long totalSec,
                                  Action<string> onClickProjectId)
    {
        const double padLeft = 10, padRight = 50, rowH = 30, rowGap = 8;
        var maxSec = data[0].Sec;

        ProjectChart.Height = data.Count * (rowH + rowGap) + 20;
        double availW = ProjectChart.ActualWidth > 0 ? ProjectChart.ActualWidth - padLeft - padRight : 200;

        for (int i = 0; i < data.Count; i++)
        {
            var item  = data[i];
            var share = item.Sec / (double)totalSec;
            var y     = 10 + i * (rowH + rowGap);
            var barW  = (item.Sec / (double)maxSec) * availW;
            var color = colorMap.TryGetValue(item.Id, out var c) ? c : Palette[i % Palette.Length];
            var capturedId = item.Id;

            var rect = new Rectangle
            {
                Width = Math.Max(barW, 4), Height = rowH,
                Fill = new SolidColorBrush(color),
                RadiusX = 4, RadiusY = 4,
                Cursor = Cursors.Hand
            };
            rect.ToolTip = $"{item.Name}: {FormatDuration(item.Sec)} ({share:P0}) — click to drill down";
            rect.MouseLeftButtonUp += (_, _) => onClickProjectId(capturedId);
            Canvas.SetLeft(rect, padLeft); Canvas.SetTop(rect, y);
            ProjectChart.Children.Add(rect);

            var lbl = new TextBlock
            {
                Text = $"{item.Name}  {FormatDurationShort(item.Sec)}",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Cursor = Cursors.Hand
            };
            lbl.MouseLeftButtonUp += (_, _) => onClickProjectId(capturedId);
            Canvas.SetLeft(lbl, padLeft + 8); Canvas.SetTop(lbl, y + (rowH - 14) / 2);
            ProjectChart.Children.Add(lbl);

            var shareLbl = new TextBlock
            {
                Text = $"{share:P0}", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130))
            };
            Canvas.SetLeft(shareLbl, padLeft + barW + 6); Canvas.SetTop(shareLbl, y + (rowH - 14) / 2);
            ProjectChart.Children.Add(shareLbl);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Dictionary<string, Color> BuildProjectColorMap(
        IReadOnlyList<ReportEntryRow> entries,
        Dictionary<string, ProjectRow> projects)
    {
        var map = new Dictionary<string, Color>();
        int fallbackIdx = 0;
        foreach (var id in _globalProjectOrder)
        {
            if (projects.TryGetValue(id, out var p) && !string.IsNullOrEmpty(p.Color))
            {
                try { map[id] = ParseHex(p.Color); continue; } catch { }
            }
            map[id] = Palette[fallbackIdx++ % Palette.Length];
        }
        return map;
    }

    private static Color LightenColor(Color c, double amount)
    {
        static byte Blend(byte val, double amt) => (byte)Math.Min(255, val + (255 - val) * amt);
        return Color.FromRgb(Blend(c.R, amount), Blend(c.G, amount), Blend(c.B, amount));
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromRgb(
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    private static string FormatDuration(long sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m" : $"{t.Minutes}m";
    }

    private static string FormatDurationShort(long sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:D2}" : $"{t.Minutes}m";
    }

    private void SetChartToggle(bool pie)
    {
        _showPie = pie;
        var accentBrush = (Brush)FindResource("AccentBrush");
        var subtleBrush = (Brush)FindResource("SubtleBgBrush");
        var accentFg    = new SolidColorBrush(Colors.White);
        var normalFg    = (Brush)FindResource("TextSecondaryBrush");

        BtnPie.Background = pie ? accentBrush : subtleBrush;
        ((TextBlock)BtnPie.Child).Foreground = pie ? accentFg : normalFg;
        BtnBar.Background = pie ? subtleBrush : accentBrush;
        ((TextBlock)BtnBar.Child).Foreground = pie ? normalFg : accentFg;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _periodIndex  = PeriodCombo.SelectedIndex;
        _periodOffset = 0;
        _drillProjectId = null;
        if (IsLoaded) Refresh();
    }

    private void PrevPeriod_Click(object sender, RoutedEventArgs e)
    {
        _periodOffset--;
        _drillProjectId = null;
        Refresh();
    }

    private void NextPeriod_Click(object sender, RoutedEventArgs e)
    {
        _periodOffset++;
        _drillProjectId = null;
        Refresh();
    }

    private void BtnPie_Click(object sender, MouseButtonEventArgs e)
    {
        SetChartToggle(true);
        Refresh();
    }

    private void BtnBar_Click(object sender, MouseButtonEventArgs e)
    {
        SetChartToggle(false);
        Refresh();
    }

    private void DrillBack_Click(object sender, MouseButtonEventArgs e)
    {
        _drillProjectId = null;
        Refresh();
    }
}
