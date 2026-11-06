namespace ModelingToolkit.IO.Native;

/// <summary>
/// Counts the steps of a reading or a writing, such as the files of an archive, reports the part done and stops at the
/// next step once the token is cancelled.
/// </summary>
internal sealed class Steps(int count, IProgress<double>? progress, CancellationToken cancellationToken)
{
    private int _done;

    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public void Next()
    {
        cancellationToken.ThrowIfCancellationRequested();
        _done++;
        progress?.Report(count == 0 ? 1.0 : Math.Min(1.0, (double)_done / count));
    }
}
