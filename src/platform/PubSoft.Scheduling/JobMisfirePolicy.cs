namespace PubSoft.Scheduling;

/// <summary>How the scheduler treats a scheduled occurrence discovered after its calendar time.</summary>
public enum JobMisfirePolicy
{
    /// <summary>Only exact timestamp matches run; missed occurrences are ignored. This is the default.</summary>
    Ignore = 0,
    /// <summary>
    /// Runs the latest missed occurrence once when it is inside the configured misfire window.
    /// Older occurrences in the same backlog are compressed into that single run.
    /// </summary>
    FireOnce = 1
}
