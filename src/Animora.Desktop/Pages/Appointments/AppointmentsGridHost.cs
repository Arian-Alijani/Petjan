using System;
using Animora.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Builds the day/week time grid and the month grid from the
/// <see cref="AppointmentsCalendarViewModel"/>'s rendered columns/cells. The
/// grids are assembled in code (rather than a fixed XAML template) because the
/// column count is data-driven (one column per active doctor in day view, seven
/// in week view) — mirroring the reference's own dynamic <c>grid(tpl, ...)</c>
/// string builder. Every measurable value comes from
/// <see cref="AppointmentsLayout"/> + appointments.css.
/// </summary>
internal static class AppointmentsGridHost
{
    /// <summary>Total scrollable body height (15 hour rows × 78px).</summary>
    internal const double BodyHeight = 1170;

    // ── time grid (day + week) ───────────────────────────────────────────────
    internal static void BuildTimeGrid(Grid host, AppointmentsCalendarViewModel vm)
    {
        host.Children.Clear();
        // The host must own the panel's full width ("*"): an "Auto" column would
        // measure to the content's natural width, so the inner per-column star
        // definitions collapsed and the whole grid shrank to the right edge —
        // leaving dead space on the leading side. "*" lets the time columns
        // stretch to fill the calendar panel exactly like the reference
        // (.tg-wrap / grid-template-columns: 56px repeat(n, minmax(0,1fr))).
        host.ColumnDefinitions = new ColumnDefinitions("*");
        host.RowDefinitions = new RowDefinitions("Auto,*");

        var header = new Grid();
        // The reference grid fills all available height rather than capping at a
        // fixed height (the old 640px cap left the calendar visually compressed
        // with dead space below). Let it stretch to the host's full height.
        var body = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var bodyGrid = new Grid();
        var cols = "56";
        for (var i = 0; i < vm.Columns.Count; i++)
        {
            cols += ",*";
        }

        header.ColumnDefinitions = new ColumnDefinitions(cols);
        bodyGrid.ColumnDefinitions = new ColumnDefinitions(cols);

        // header row — empty gutter cell + one header per column
        var gutterHead = new Border { Classes = { "tg-hcell", "first" } };
        header.Children.Add(gutterHead);
        for (var i = 0; i < vm.Columns.Count; i++)
        {
            var cell = BuildHeaderCell(vm.Columns[i].Header, i == 0);
            Grid.SetColumn(cell, i + 1);
            header.Children.Add(cell);
        }

        // body — gutter + one column per doctor/day
        var gutter = BuildGutter();
        Grid.SetColumn(gutter, 0);
        bodyGrid.Children.Add(gutter);

        for (var i = 0; i < vm.Columns.Count; i++)
        {
            var column = BuildTimeColumn(vm.Columns[i], i == 0, vm);
            Grid.SetColumn(column, i + 1);
            bodyGrid.Children.Add(column);
        }

        body.Content = bodyGrid;
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        host.Children.Add(header);
        host.Children.Add(body);

        if (!vm.HasGridEvents)
        {
            var empty = BuildEmptyState();
            Grid.SetRow(empty, 1);
            host.Children.Add(empty);
        }
    }

    private static Control BuildHeaderCell(AppointmentColumnHeaderViewModel header, bool isFirst)
    {
        var border = new Border { Classes = { "tg-hcell" } };
        if (isFirst)
        {
            border.Classes.Add("first");
        }

        if (header.IsToday)
        {
            border.Classes.Add("today");
        }

        if (header.IsFriday)
        {
            border.Classes.Add("off");
        }

        if (header.IsDoctor && header.Doctor is { } doc)
        {
            var avatar = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = AppointmentsPresentation.Accent(doc.Accent),
                Child = Centered(Text(doc.Initial, 10, FontWeight.ExtraBold, Brushes.White)),
            };
            var texts = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Right };
            texts.Children.Add(Text(doc.ShortName, 12.5, FontWeight.Bold, Ink(), ellipsis: true));
            texts.Children.Add(Text(header.CountText, 10.5, FontWeight.Normal, Muted()));
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            row.Children.Add(avatar);
            row.Children.Add(texts);
            border.Child = row;
        }
        else
        {
            var stack = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(Text(header.WeekdayText, 10.5, FontWeight.Normal, Muted()));
            if (header.IsToday)
            {
                stack.Children.Add(new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = AppointmentsPresentation.Accent("#0d9488"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = Centered(Text(header.DayNumberText, 14, FontWeight.ExtraBold, Brushes.White)),
                });
            }
            else
            {
                stack.Children.Add(Text(header.DayNumberText, 16, FontWeight.ExtraBold, Ink()));
            }

            border.Child = stack;
        }

        return border;
    }

    private static Control BuildGutter()
    {
        var border = new Border { Classes = { "tg-gutter" } };
        var stack = new StackPanel();
        for (var h = 7; h < 22; h++)
        {
            var row = new Border { Classes = { "tg-gutter-hour" }, Height = 78 };
            var label = Text(AppointmentsPresentation.HourLabel(h * 60), 10.5, FontWeight.Normal, Muted2());
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.Margin = new Thickness(8, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Top;
            row.Child = label;
            stack.Children.Add(row);
        }

        border.Child = stack;
        return border;
    }

    private static Control BuildTimeColumn(
        AppointmentColumnViewModel column,
        bool isFirst,
        AppointmentsCalendarViewModel vm)
    {
        var colBorder = new Border { Classes = { "tg-col" }, MinWidth = 0 };
        if (isFirst)
        {
            colBorder.Classes.Add("first");
        }

        if (column.IsClosed)
        {
            colBorder.Classes.Add("off");
        }

        var panel = new Panel { Height = BodyHeight };

        // hour separator rows
        var hours = new StackPanel();
        for (var h = 0; h < AppointmentsLayout.HourCount; h++)
        {
            var row = new Border { Classes = { "tg-hour" }, Height = 78 };
            if (h == 0)
            {
                row.Classes.Add("first");
            }

            hours.Children.Add(row);
        }

        panel.Children.Add(hours);

        // click-to-create layer (beneath the event cards) — Phase 14 placeholder
        var pick = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Cross),
            Command = vm.OpenBookingCommand,
        };
        panel.Children.Add(pick);

        // event cards arranged by the layout panel
        var eventsPanel = new TimeGridEventsPanel { Height = BodyHeight };
        foreach (var ev in column.Events)
        {
            eventsPanel.Children.Add(BuildEventCard(ev, vm));
        }

        panel.Children.Add(eventsPanel);

        // now line
        if (column.HasNow && column.NowTop is { } top)
        {
            var nowLine = new Panel { Height = 0, VerticalAlignment = VerticalAlignment.Top };
            nowLine.Margin = new Thickness(0, top, 0, 0);
            nowLine.Children.Add(new Border
            {
                Height = 2,
                Background = AppointmentsPresentation.Accent("#e0564a"),
                VerticalAlignment = VerticalAlignment.Top,
            });
            nowLine.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = AppointmentsPresentation.Accent("#e0564a"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -3, -4, 0),
            });
            panel.Children.Add(nowLine);
        }

        colBorder.Child = panel;
        return colBorder;
    }

    private static Control BuildEventCard(AppointmentEventViewModel ev, AppointmentsCalendarViewModel vm)
    {
        var button = new Button { Classes = { "ev-card" }, DataContext = ev, CommandParameter = ev };
        button.Classes.Add(ev.TierClass);
        if (ev.IsCancelled)
        {
            button.Classes.Add("cancelled");
        }

        button.MinHeight = ev.Height;
        button.Background = ev.BackgroundBrush;
        button.BorderBrush = ev.AccentBrush;
        button.BorderThickness = new Thickness(0, 0, 0, 0);
        button.Command = vm.OpenBookingCommand;

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,3") };
        var body = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
        };

        if (ev.IsExtraSmall)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            line.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = ev.AccentBrush, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(Text(ev.PetName, 11.5, FontWeight.ExtraBold, ev.ForegroundBrush, ellipsis: true));
            line.Children.Add(Text(ev.StartText, 10, FontWeight.Normal, ev.ForegroundBrush, opacity: 0.9));
            body.Children.Add(line);
        }
        else if (ev.IsSmall)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            line.Children.Add(Text(ev.PetName, 12, FontWeight.ExtraBold, ev.ForegroundBrush, ellipsis: true));
            AddFlag(line, ev);
            body.Children.Add(line);
            var range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            range.Children.Add(Text(ev.RangeText, 10.5, FontWeight.SemiBold, ev.ForegroundBrush, opacity: 0.82));
            range.Children.Add(Text("· " + ev.TypeLabel, 10.5, FontWeight.Normal, ev.ForegroundBrush, opacity: 0.72));
            body.Children.Add(range);
        }
        else
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            line.Children.Add(Text(ev.PetName, 12, FontWeight.ExtraBold, ev.ForegroundBrush, ellipsis: true));
            AddFlag(line, ev);
            body.Children.Add(line);
            body.Children.Add(Text(ev.RangeText, 10.5, FontWeight.SemiBold, ev.ForegroundBrush, opacity: 0.82));
            body.Children.Add(Text(ev.TypeLabel + " · " + ev.DoctorShort, 10.5, FontWeight.Normal, ev.ForegroundBrush, opacity: 0.72, ellipsis: true));
        }

        var strip = new Border { Background = ev.AccentBrush, Width = 3 };
        Grid.SetColumn(body, 0);
        Grid.SetColumn(strip, 1);
        root.Children.Add(body);
        root.Children.Add(strip);
        button.Content = root;

        ToolTip.SetTip(button,
            $"{ev.PetName} · {ev.TypeLabel} · {ev.DoctorName} · " +
            $"{AppointmentsPresentation.Time(ev.Model.StartMinutes)}-" +
            $"{AppointmentsPresentation.Time(ev.Model.EndMinutes)} · {ev.StatusLabel}");
        return button;
    }

    private static void AddFlag(StackPanel line, AppointmentEventViewModel ev)
    {
        if (!ev.ShowFlag)
        {
            return;
        }

        line.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = ev.FlagBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    // ── month grid ───────────────────────────────────────────────────────────
    internal static void BuildMonthGrid(Grid host, AppointmentsCalendarViewModel vm)
    {
        host.Children.Clear();
        host.ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*");

        var rows = Math.Max(6, (vm.MonthCells.Count + 6) / 7) + 1;
        var rowDefs = new RowDefinitions("Auto");
        for (var r = 1; r < rows; r++)
        {
            rowDefs.Add(new RowDefinition(GridLength.Auto) { MinHeight = 132 });
        }

        host.RowDefinitions = rowDefs;

        for (var i = 0; i < 7; i++)
        {
            var h = new Border
            {
                Padding = new Thickness(6, 11),
                BorderBrush = Lines(),
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 1),
                Child = Centered(Text(AppointmentsCalendarMath.DayNames[i], 12, FontWeight.Bold, Ink2())),
            };
            Grid.SetColumn(h, i);
            Grid.SetRow(h, 0);
            host.Children.Add(h);
        }

        for (var i = 0; i < vm.MonthCells.Count; i++)
        {
            var cell = BuildMonthCell(vm.MonthCells[i], i % 7 == 0, vm);
            Grid.SetColumn(cell, i % 7);
            Grid.SetRow(cell, 1 + (i / 7));
            host.Children.Add(cell);
        }

        if (!vm.HasMonthEvents)
        {
            var empty = BuildEmptyState();
            Grid.SetColumnSpan(empty, 7);
            Grid.SetRow(empty, 1);
            Grid.SetRowSpan(empty, rows - 1);
            host.Children.Add(empty);
        }
    }

    private static Control BuildMonthCell(MonthCellViewModel cell, bool firstInRow, AppointmentsCalendarViewModel vm)
    {
        var border = new Border
        {
            MinHeight = 132,
            Padding = new Thickness(8, 8, 8, 7),
            BorderBrush = Lines(),
            BorderThickness = new Thickness(firstInRow ? 0 : 1, 0, 0, 1),
        };

        if (cell.IsOut)
        {
            border.Background = AppointmentsPresentation.Accent("#fafbfa");
        }
        else if (cell.IsFriday)
        {
            border.Background = AppointmentsPresentation.Accent("#fbfcfb");
        }

        var stack = new StackPanel { Spacing = 4 };
        if (cell.IsToday)
        {
            stack.Children.Add(new Border
            {
                Padding = new Thickness(6, 2),
                CornerRadius = new CornerRadius(7),
                Background = AppointmentsPresentation.Accent("#0d9488"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Text(cell.DayNumberText, 13, FontWeight.ExtraBold, Brushes.White),
            });
        }
        else
        {
            var dayText = Text(cell.DayNumberText, 13, FontWeight.ExtraBold, Ink2());
            dayText.HorizontalAlignment = HorizontalAlignment.Left;
            dayText.Opacity = cell.IsOut ? 0.55 : 1;
            stack.Children.Add(dayText);
        }

        foreach (var ev in cell.Events)
        {
            stack.Children.Add(BuildMonthChip(ev, vm));
        }

        if (cell.HasMore)
        {
            var more = Text(cell.MoreText, 11, FontWeight.Bold, AppointmentsPresentation.Accent("#0e7566"));
            more.Margin = new Thickness(6, 1, 6, 1);
            stack.Children.Add(more);
        }

        border.Child = stack;
        return border;
    }

    private static Control BuildMonthChip(MonthEventViewModel ev, AppointmentsCalendarViewModel vm)
    {
        var button = new Button
        {
            Classes = { "mv-chip" },
            DataContext = ev,
            CommandParameter = ev,
            Command = vm.OpenBookingCommand,
            Background = AppointmentsPresentation.TypeBackground(ev.Type.ClassKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,3") };
        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 4, 0) };
        body.Children.Add(Text(ev.TimeText, 11, FontWeight.SemiBold, AppointmentsPresentation.TypeForeground(ev.Type.ClassKey), opacity: 0.78));
        body.Children.Add(Text(ev.PetName, 11.5, FontWeight.ExtraBold, AppointmentsPresentation.TypeForeground(ev.Type.ClassKey), ellipsis: true));
        var strip = new Border { Background = AppointmentsPresentation.TypeAccent(ev.Type.ClassKey), Width = 3 };

        if (ev.IsCancelled)
        {
            root.Opacity = 0.55;
        }

        Grid.SetColumn(body, 0);
        Grid.SetColumn(strip, 1);
        root.Children.Add(body);
        root.Children.Add(strip);
        button.Content = root;
        return button;
    }

    // ── empty state ──────────────────────────────────────────────────────────
    private static Control BuildEmptyState()
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 70),
        };

        panel.Children.Add(new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(16),
            Background = Bg2(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            Child = Centered(new Avalonia.Controls.Shapes.Path
            {
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                Stroke = Muted2(),
                StrokeThickness = 2,
                Data = IconGeometry("IconCalendar"),
            }),
        });
        panel.Children.Add(Centered(Text("نوبتی برای نمایش نیست", 14, FontWeight.Bold, Ink2())));
        panel.Children.Add(Centered(Text("فیلترها را تغییر دهید یا برای این بازه نوبت جدیدی ثبت کنید.", 12, FontWeight.Normal, Muted())));
        panel.Children.Add(new Border
        {
            Background = Bg2(),
            CornerRadius = new CornerRadius(99),
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Text("می‌توانید روی تقویم کلیک کنید یا در بازه دلخواه بکشید تا مستقیم نوبت‌دهی کنید.", 11.5, FontWeight.Normal, Muted2()),
        });

        return panel;
    }

    // ── small factory helpers ────────────────────────────────────────────────
    private static TextBlock Text(
        string? value,
        double size,
        FontWeight weight,
        IBrush brush,
        double opacity = 1,
        bool ellipsis = false)
    {
        var t = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (ellipsis)
        {
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.TextWrapping = TextWrapping.NoWrap;
        }

        return t;
    }

    private static Control Centered(Control child) =>
        new Panel { Children = { child } };

    private static IBrush Ink() => AppointmentsPresentation.Accent("#101828");
    private static IBrush Ink2() => AppointmentsPresentation.Accent("#344054");
    private static IBrush Muted() => AppointmentsPresentation.Accent("#667085");
    private static IBrush Muted2() => AppointmentsPresentation.Accent("#98a2b3");
    private static IBrush Bg2() => AppointmentsPresentation.Accent("#f1f5f6");
    private static IBrush Lines() => AppointmentsPresentation.Accent("#eef2f5");

    private static Geometry? IconGeometry(string key)
    {
        if (Application.Current is { } app && app.TryFindResource(key, out var res) && res is Geometry geo)
        {
            return geo;
        }

        return null;
    }
}
