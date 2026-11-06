namespace ModelingToolkit.IO.Native.Tests;

/// <summary>Records what an operation reports, on the thread that reports it, and may act on each report.</summary>
internal sealed class RecordingProgress(Action<double>? onReport = null) : IProgress<double>
{
    private readonly List<double> _reports = [];

    public IReadOnlyList<double> Reports
    {
        get
        {
            lock (_reports)
            {
                return [.. _reports];
            }
        }
    }

    public void Report(double value)
    {
        lock (_reports)
        {
            _reports.Add(value);
        }
        onReport?.Invoke(value);
    }
}
