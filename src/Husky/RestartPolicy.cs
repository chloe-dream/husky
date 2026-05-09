namespace Husky;

/// <summary>
/// Crash-restart bookkeeping per LEASH §8.4. Tracks attempts inside a
/// rolling one-hour window. After the cap is hit the launcher remains in
/// the "broken" state until a successful update calls <see cref="Reset"/>.
/// </summary>
internal sealed class RestartPolicy(
    int maxAttemptsPerHour,
    TimeSpan pauseBetweenAttempts,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly Lock gate = new();
    private readonly Queue<DateTimeOffset> attempts = new();
    private bool broken;

    public int MaxAttemptsPerHour { get; } = maxAttemptsPerHour;
    public TimeSpan PauseBetweenAttempts { get; } = pauseBetweenAttempts;

    public bool IsBroken
    {
        get { lock (gate) return broken; }
    }

    public int AttemptsInWindow
    {
        get { lock (gate) { Trim(time.GetUtcNow()); return attempts.Count; } }
    }

    /// <summary>
    /// Returns true if a restart is allowed *now* (cap not yet reached).
    /// Does not record the attempt — call <see cref="RecordAttempt"/> after
    /// the wait/restart is actually done.
    /// </summary>
    public bool CanRestart()
    {
        lock (gate)
        {
            if (broken) return false;
            Trim(time.GetUtcNow());
            return attempts.Count < MaxAttemptsPerHour;
        }
    }

    public void RecordAttempt()
    {
        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            Trim(now);
            attempts.Enqueue(now);
            if (attempts.Count >= MaxAttemptsPerHour)
                broken = true;
        }
    }

    /// <summary>
    /// Clears the attempts and the broken flag — called after a successful
    /// update brings in a (presumed) fixed binary.
    /// </summary>
    public void Reset()
    {
        lock (gate)
        {
            attempts.Clear();
            broken = false;
        }
    }

    private void Trim(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - Window;
        while (attempts.Count > 0 && attempts.Peek() < cutoff)
            attempts.Dequeue();
    }
}
