namespace MpbkpuLib.Utility;

/// <summary>
/// Estimates the time left of a job phase.
/// The work is measured in bytes plus a fixed cost per item, so many small files (or deletions, where the size does
/// not matter) are estimated correctly. The speed is measured over the last <see cref="Window"/>, so the estimate
/// follows speed changes; no estimate is given during the first <see cref="Warmup"/>.
/// </summary>
public class EtaEstimator
{
    /// <summary>
    /// Fixed cost of an item on a local file system, expressed in bytes
    /// </summary>
    public const long LocalItemCost = 256 * 1024;
    /// <summary>
    /// Fixed cost of an item on a remote file system (one or more requests per file), expressed in bytes
    /// </summary>
    public const long RemoteItemCost = 2 * 1024 * 1024;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Warmup = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MinSampleInterval = TimeSpan.FromMilliseconds(250);

    private readonly long _totalWork;
    private readonly long _itemCost;
    private readonly DateTime _started;
    private readonly Func<DateTime> _clock;
    private readonly Queue<(DateTime Time, long Work)> _samples = new();

    /// <param name="totalBytes">Total bytes of the phase (0 when the size does not matter, e.g. deletions)</param>
    /// <param name="totalItems">Total items of the phase</param>
    /// <param name="itemCost">Fixed cost of an item, in bytes</param>
    /// <param name="clock">UTC clock (for tests)</param>
    public EtaEstimator(long totalBytes, long totalItems, long itemCost, Func<DateTime>? clock = null)
    {
        _itemCost = itemCost;
        _totalWork = Math.Max(0, totalBytes) + Math.Max(0, totalItems) * itemCost;
        _clock = clock ?? (() => DateTime.UtcNow);
        _started = _clock();
        _samples.Enqueue((_started, 0));
    }

    /// <summary>
    /// Update the progress and get the time left
    /// </summary>
    /// <param name="doneBytes">Bytes done (also of the item in progress)</param>
    /// <param name="doneItems">Items completed</param>
    /// <returns>The time left, null while it cannot be estimated yet</returns>
    public TimeSpan? Update(long doneBytes, long doneItems)
    {
        var now = _clock();
        var work = Math.Max(0, doneBytes) + Math.Max(0, doneItems) * _itemCost;
        if (now - _samples.Last().Time >= MinSampleInterval)
            _samples.Enqueue((now, work));
        // Keep one sample older than the window as the starting point of the speed measure
        while (_samples.Count > 2 && now - _samples.ElementAt(1).Time >= Window)
            _samples.Dequeue();

        if (now - _started < Warmup || work <= 0)
            return null;

        var (fromTime, fromWork) = _samples.Peek();
        var seconds = (now - fromTime).TotalSeconds;
        var speed = seconds > 0 ? (work - fromWork) / seconds : 0;
        if (speed <= 0)
            return null;

        return TimeSpan.FromSeconds(Math.Max(0, _totalWork - work) / speed);
    }
}
