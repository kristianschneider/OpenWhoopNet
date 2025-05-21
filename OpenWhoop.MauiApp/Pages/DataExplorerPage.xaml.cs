using Microsoft.Maui.Controls;
using OpenWhoop.App.Services;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using OpenWhoop.Core.Entities;

namespace OpenWhoop.MauiApp.Pages
{
    public partial class DataExplorerPage : ContentPage
    {
        private readonly DbService _dbService;
        public ObservableCollection<HeartRateSample> HeartRateSamples { get; set; } = new();


        public DataExplorerPage(DbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                var context = _dbService.Context;
                int count = await context.HeartRateSamples.CountAsync();
                var samples = await context.HeartRateSamples
                    .OrderByDescending(h => h.TimestampUtc)
                    .Take(300) // Limit for performance, adjust as needed
                    .ToListAsync();
                HeartRateSamples.Clear();
                foreach (var s in samples)
                    HeartRateSamples.Add(s);
                HeartRateSamplesView.ItemsSource = HeartRateSamples;

                SampleCountLabel.Text = count.ToString();
                var oldest = await context.HeartRateSamples
                    .OrderBy(h => h.TimestampUtc)
                    .Select(h => h.TimestampUtc)
                    .FirstOrDefaultAsync();
                var newest = await context.HeartRateSamples
                    .OrderByDescending(h => h.TimestampUtc)
                    .Select(h => h.TimestampUtc)
                    .FirstOrDefaultAsync();
                OldestLabel.Text = oldest == default ? "N/A" : oldest.ToString("u");
                NewestLabel.Text = newest == default ? "N/A" : newest.ToString("u");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
           
        }
    }
}
