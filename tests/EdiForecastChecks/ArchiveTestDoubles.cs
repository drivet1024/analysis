using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

sealed class ConveyorDataService
{
    public int Reads { get; private set; }
    public Task<IReadOnlyList<EdiHistoryDay>> GetEdiHistoryAsync(DateOnly start, DateOnly end)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<EdiHistoryDay>>(Enumerable.Range(0, end.DayNumber - start.DayNumber)
            .Select(i => new EdiHistoryDay(start.AddDays(i), 100)).ToArray());
    }
}
sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "EdiChecks";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class TestWeekClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class TestLogger<T> : ILogger<T>
{
    public Exception? LastException { get; private set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (exception != null) LastException = exception;
    }
}
sealed class EdiSectorForecastService(TestWeekClock clock)
{
    public int ForecastReads { get; private set; }
    public int ActualReads { get; private set; }
    public long ActualVolume { get; set; } = 125;
    public Task<EdiSectorForecastResponse> WithCurrentSectorDetailsAsync(EdiSectorForecastResponse result, CancellationToken token) => Task.FromResult(result);
    private EdiSectorForecastResponse Make(DateOnly sunday, bool available)
    {
        var dates = Enumerable.Range(1,455).Select(i=>sunday.AddDays(-i)).ToArray();
        var history = available ? dates.Select(d=>new EdiSectorHistory(d,530,100,100,0,0)).ToArray() : [];
        return new(sunday, clock.Now, !available, "sorted-sector-delivery-v2", 1, "Saint-Hubert", sunday.AddDays(-455),
            dates.Length, 100, 0, 0, 0, [new(530,"Test",12,100,100,0,0,EdiSectorModel.Build(sunday,530,history,dates))]);
    }
    public Task<EdiSectorForecastResponse> GetAsync(DateOnly date, bool persist, CancellationToken cancellationToken)
    { ForecastReads++; return Task.FromResult(Make(date,true)); }
    public Task<EdiSectorForecastResponse> EmptyWeekAsync(DateOnly date, CancellationToken token) => Task.FromResult(Make(date,false));
    public Task<EdiSectorHistorySet> ReadHistoryAsync(DateOnly start, DateOnly end, DateTime cutoff, CancellationToken token)
    {
        ActualReads++;
        var dates=Enumerable.Range(0,end.DayNumber-start.DayNumber).Select(i=>start.AddDays(i)).Where(d=>!EdiSectorCalendar.IsWeekend(d)).ToArray();
        return Task.FromResult(new EdiSectorHistorySet(dates.Select(d=>new EdiSectorHistory(d,530,ActualVolume,ActualVolume,0,0)).ToArray(), dates,
            dates.Length*ActualVolume,0,0,0));
    }
}
