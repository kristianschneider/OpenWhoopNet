using Microsoft.Maui.Controls;
using OpenWhoop.App.Services;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using OpenWhoop.Core.Entities;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using SkiaSharp;

namespace OpenWhoop.MauiApp.Pages;
public partial class DataExplorerPage : ContentPage
{
    private readonly DbService _dbService;
    private bool _isTableViewVisible = false;
    public ObservableCollection<HeartRateViewModel> HeartRateSamples { get; set; } = new();
    private ObservableCollection<ISeries> _series = new();
    public ObservableCollection<ISeries> Series
    {
        get => _series;
        set
        {
            _series = value;
            OnPropertyChanged();
        }
    }


    public DataExplorerPage(DbService dbService)
    {
        InitializeComponent();
        _dbService = dbService;
        BindingContext = this; // Add this line
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Clear collections to help GC
        HeartRateSamples.Clear();
        Series.Clear();

        // Force GC collection when leaving the page
        GC.Collect();
    }

    //private async void OnViewToggleClicked(object sender, EventArgs e)
    //{
    //    // Toggle visibility
    //    _isTableViewVisible = !_isTableViewVisible;

    //    // Update UI elements
    //    HeartRateSamplesView.IsVisible = _isTableViewVisible;
    //    HrChart.IsVisible = !_isTableViewVisible;

    //    // Update button text
    //    ViewToggleButton.Text = _isTableViewVisible
    //        ? "Switch to Graph View"
    //        : "Switch to Table View";

    //    // Load data for the newly visible view
    //    if (_isTableViewVisible)
    //    {
    //        await LoadTableData();
    //    }
    //    else // Only reload if needed
    //    {
    //        await LoadGraphData();
    //    }
    //}

    public ICartesianAxis[] XAxes { get; set; } = [
        new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MMMM dd"))
    ];

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var context = _dbService.Context;
            int count = await context.HeartRateSamples.CountAsync();
            // SampleCountLabel.Text = count.ToString();

            // Get timestamp ranges for labels
            //await LoadTimestampRangeData();
          ConfigureChartAxes();

            // Only load data for the visible view
            if (_isTableViewVisible)
            {
                await LoadTableData();
            }
            else
            {
                await LoadGraphData();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    private async Task LoadTimestampRangeData()
    {
        var context = _dbService.Context;
        var oldest = await context.HeartRateSamples
            .OrderBy(h => h.TimestampUtc)
            .Select(h => h.TimestampUtc)
            .FirstOrDefaultAsync();
        var newest = await context.HeartRateSamples
            .OrderByDescending(h => h.TimestampUtc)
            .Select(h => h.TimestampUtc)
            .FirstOrDefaultAsync();
        //  OldestLabel.Text = oldest == default ? "N/A" : oldest.ToString("u");
        // NewestLabel.Text = newest == default ? "N/A" : newest.ToString("u");
    }
    private async Task LoadTableData()
    {
        // Only load data if the view is visible
        if (!_isTableViewVisible) return;

        var samples = await _dbService.Context.HeartRateSamples
            .OrderByDescending(h => h.TimestampUtc)
            .Take(100) // Reduced from 300 to 100
            .AsNoTracking() // Important for reducing EF Core memory usage
            .ToListAsync();

        HeartRateSamples.Clear();
        foreach (var s in samples)
        {
            HeartRateSamples.Add(new HeartRateViewModel
            {
                TimestampUtc = s.TimestampUtc,
                Value = s.Value,
                ActivityId = s.ActivityId,
                RrCount = s.RrIntervals?.Count ?? 0
            });
        }
    }
    private async Task LoadGraphData()
    {
        // Only load data if the view is visible
        if (_isTableViewVisible) return;

        var samples = await _dbService.Context.HeartRateSamples
            .OrderByDescending(h => h.TimestampUtc)
            //.Take(200)
            .AsNoTracking() // Important for reducing EF Core memory usage
            .ToListAsync();

        await LoadChartData(samples);
    }

    private void ConfigureChartAxes()
    {
        var xAxis = new DateTimeAxis(TimeSpan.FromMinutes(10),date=>date.ToString("dd/MM HH:mm"))
        {
            Name = "Time",
            MinStep = TimeSpan.FromMinutes(10).Ticks,
            LabelsRotation = -45
        };

        HrChart.XAxes = [xAxis];

        

        //HrChart.YAxes = new[]
        //{
        //    new LiveChartsCore.SkiaSharpView.Axis
        //    {
        //        Name = "Heart Rate (BPM)",
        //        Position = AxisPosition.Start,
        //        MinLimit = 40,
        //        MaxLimit = 200,
        //        MinStep = 20
        //    }
        //};

        //// Set animation options for the entire chart
        //HrChart.EasingFunction = EasingFunctions.ExponentialOut;
        //HrChart.AnimationsSpeed = TimeSpan.FromMilliseconds(1500);
    }


    private async Task LoadChartData(List<HeartRateSample> samples)
    {
        // Group heart rate samples by minute and calculate the average for each minute
        var groupedByMinute = samples
            .GroupBy(s => new DateTime(
                s.TimestampUtc.Year,
                s.TimestampUtc.Month,
                s.TimestampUtc.Day,
                s.TimestampUtc.Hour,
                s.TimestampUtc.Minute,
                0) // Truncate to minute precision
            )
            .Select(g => new
            {
                Timestamp = g.Key,
                AverageValue = (int)Math.Round(g.Average(s => s.Value))
            })
            .OrderBy(x => x.Timestamp).Where(x=>x.Timestamp>DateTime.UtcNow.AddDays(-1))
            .ToList();

        // Convert to DateTimePoint for the chart
        var heartRatePoints = groupedByMinute
            .Select(g => new DateTimePoint(g.Timestamp, g.AverageValue))
            .ToList();

        // Get the start and end time for the chart
        var startTime = heartRatePoints.Count > 0 ? heartRatePoints.Min(p => p.DateTime) : DateTime.UtcNow.AddHours(-1);
        var endTime = heartRatePoints.Count > 0 ? heartRatePoints.Max(p => p.DateTime).AddHours(1) : DateTime.UtcNow.AddHours(1);

        var newSeries = new ObservableCollection<ISeries>();

        // Create the animated heart rate series
        newSeries.Add(new ColumnSeries<DateTimePoint>
        {
            Name = "Heart Rate",
            Values = heartRatePoints,
            //GeometrySize = 2,
            //LineSmoothness = 0.5,
            Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(SKColors.Red.WithAlpha(40)),

           // AnimationsSpeed = TimeSpan.FromMilliseconds(1500),

           // EasingFunction = LiveChartsCore.EasingFunctions.ExponentialOut
        });

        // Add horizontal lines for zones
        newSeries.Add(new LineSeries<DateTimePoint>
        {
            Name = "Moderate Zone",
            Values = new[] {
                new DateTimePoint(startTime, 70),
                new DateTimePoint(endTime, 70)
            },
            GeometrySize = 0,
            Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 1 },
            AnimationsSpeed = TimeSpan.Zero,
            Fill = null

        });

        newSeries.Add(new LineSeries<DateTimePoint>
        {
            Name = "Intensive Zone",
            Values = new[] {
                new DateTimePoint(startTime, 140),
                new DateTimePoint(endTime, 140)
            },
            GeometrySize = 0,
            Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 1 },
            AnimationsSpeed = TimeSpan.Zero,
            Fill = null
        });

        Series = newSeries;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            HrChart.InvalidateMeasure();
        });
    }
}
