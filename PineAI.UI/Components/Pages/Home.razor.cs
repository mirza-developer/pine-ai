using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PineAI.Core.Features.MenuLink;
using PineAI.Core.Features.Order;
using PineAI.UI.Services;
using System.Globalization;

namespace PineAI.UI.Components.Pages;

public partial class Home
{
    public List<MenuLinkDto> AccessibleLinks = [];

    [Inject] private AuthStateService AuthState { get; set; } = default!;
    [Inject] private MenuAccessStateService MenuAccessState { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ApiClientService ApiClient { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private string selectedRange = "weekly";
    private bool isLoading = true;
    private string? errorMessage;
    private static readonly PersianCalendar persianCalendar = new();

    protected override async Task OnInitializedAsync()
    {
        if (!AuthState.IsAuthenticated)
            Navigation.NavigateTo("/login");

        await MenuAccessState.EnsureLoadedAsync();

        AccessibleLinks = MenuAccessState.GetLinks().Where(l => l.Url != "/").ToList();

        // Start loading chart data
        await LoadChartData();

        StateHasChanged();
    }

    private async Task OnRangeChanged()
    {
        await LoadChartData();
    }

    private async Task LoadChartData()
    {
        isLoading = true;
        errorMessage = null;
        StateHasChanged();

        try
        {
            var dateRange = GetDateRange();

            var statistics = await ApiClient.GetOrderStatisticsAsync(dateRange.StartDate, dateRange.EndDate, dateRange.GroupBy);

            if (statistics is null || statistics.DataPoints is null)
            {
                errorMessage = "خطا در دریافت اطلاعات";
              
                isLoading = false;
               
                StateHasChanged();
              
                return;
            }

            var labels = statistics.DataPoints.Select(d => d.Label).ToList();
           
            var data = statistics.DataPoints.Select(d => d.Count).ToList();
          
            isLoading = false;

            StateHasChanged();

            await Task.Delay(100);

            await RenderChart(labels, data);
        }
        catch (Exception ex)
        {
            errorMessage = $"خطا: {ex.Message}";
            isLoading = false;
            StateHasChanged();
        }
    }

    private DateRangeResult GetDateRange()
    {
        var now = DateTime.Now;
        var persianNow = DateTime.Now;
        var persianYear = persianCalendar.GetYear(persianNow);
        var persianMonth = persianCalendar.GetMonth(persianNow);

        if (selectedRange == "weekly")
        {
            // Weekly in current Persian month
            var firstDayOfMonth = persianCalendar.ToDateTime(persianYear, persianMonth, 1, 0, 0, 0, 0);
            var daysInMonth = persianCalendar.GetDaysInMonth(persianYear, persianMonth);
            var lastDayOfMonth = persianCalendar.ToDateTime(persianYear, persianMonth, daysInMonth, 23, 59, 59, 0);
            return new DateRangeResult { StartDate = firstDayOfMonth, EndDate = lastDayOfMonth, GroupBy = "week" };
        }
        else if (selectedRange == "monthly")
        {
            // Monthly in current Persian year
            var firstDayOfYear = persianCalendar.ToDateTime(persianYear, 1, 1, 0, 0, 0, 0);
            var lastDayOfYear = persianCalendar.ToDateTime(persianYear, 12, persianCalendar.GetDaysInMonth(persianYear, 12), 23, 59, 59, 0);
            return new DateRangeResult { StartDate = firstDayOfYear, EndDate = lastDayOfYear, GroupBy = "month" };
        }
        else // yearly
        {
            // Last 3 Persian years
            var startYear = persianYear - 2;
            var firstDay = persianCalendar.ToDateTime(startYear, 1, 1, 0, 0, 0, 0);
            var lastDay = persianCalendar.ToDateTime(persianYear, 12, persianCalendar.GetDaysInMonth(persianYear, 12), 23, 59, 59, 0);
            return new DateRangeResult { StartDate = firstDay, EndDate = lastDay, GroupBy = "year" };
        }
    }

    private string ConvertToPersianLabel(DateTime date, string groupBy)
    {
        var year = persianCalendar.GetYear(date);
        var month = persianCalendar.GetMonth(date);
        var day = persianCalendar.GetDayOfMonth(date);

        var monthNames = new[] { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
                                 "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

        if (groupBy == "week")
        {
            return $"{year}/{month:D2}/{day:D2}";
        }
        else if (groupBy == "month")
        {
            return $"{monthNames[month - 1]} {year}";
        }
        else if (groupBy == "year")
        {
            return year.ToString();
        }
        else // day
        {
            return $"{year}/{month:D2}/{day:D2}";
        }
    }

    private async Task RenderChart(List<string> labels, List<int> data)
    {
        try
        {
            var chartConfig = new
            {
                type = "line",
                data = new
                {
                    labels = labels,
                    datasets = new[]
                    {
                        new
                        {
                            label = "تعداد سفارشات",
                            data = data,
                            borderColor = "#41A870",
                            backgroundColor = "rgba(65, 168, 112, 0.15)",
                            tension = 0.1,
                            fill = true
                        }
                    }
                },
                options = new
                {
                    responsive = true,
                    maintainAspectRatio = false,
                    plugins = new
                    {
                        legend = new
                        {
                            display = true,
                            position = "top"
                        },
                        title = new
                        {
                            display = false
                        }
                    },
                    scales = new
                    {
                        y = new
                        {
                            beginAtZero = true,
                            ticks = new
                            {
                                stepSize = 1
                            }
                        }
                    }
                }
            };

            await JS.InvokeVoidAsync("renderOrderChart", chartConfig);
        }
        catch (Exception ex)
        {
            errorMessage = $"خطا در رسم نمودار: {ex.Message}";
        }
    }

    public static string GetCardColor(string iconName) => iconName switch
    {
        var n when n.Contains("person") => "success",
        var n when n.Contains("excel") => "info",
        var n when n.Contains("box") => "warning",
        var n when n.Contains("tags") => "primary",
        var n when n.Contains("key") => "secondary",
        var n when n.Contains("chat") => "dark",
        _ => "primary"
    };
}
