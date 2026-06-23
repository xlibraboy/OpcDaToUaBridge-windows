using Microsoft.Extensions.Logging;

namespace OpcBridge.App;

internal sealed class DashboardLogStore
{
    public const int MaxEntries = 500;

    private readonly object sync_ = new();
    private readonly Queue<DashboardLogEntry> entries_ = new();

    public void Add(LogLevel level, string category, string message, Exception? exception)
    {
        DashboardLogEntry entry = new(
            DateTime.UtcNow,
            level,
            category,
            message,
            exception?.ToString());

        lock (sync_)
        {
            if (entries_.Count >= MaxEntries)
            {
                entries_.Dequeue();
            }

            entries_.Enqueue(entry);
        }
    }

    public IReadOnlyList<DashboardLogEntry> GetEntries(int limit, LogLevel? minimumLevel = null)
    {
        int cappedLimit = Math.Clamp(limit, 1, MaxEntries);

        lock (sync_)
        {
            DashboardLogEntry[] snapshot = entries_.ToArray();
            List<DashboardLogEntry> entries = new(cappedLimit);

            for (int index = snapshot.Length - 1; index >= 0 && entries.Count < cappedLimit; index--)
            {
                DashboardLogEntry entry = snapshot[index];
                if (minimumLevel.HasValue && entry.Level < minimumLevel.Value)
                {
                    continue;
                }

                entries.Add(entry);
            }

            return entries;
        }
    }
}

internal sealed record DashboardLogEntry(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionText);

internal sealed class DashboardLogProvider : ILoggerProvider
{
    private readonly DashboardLogStore store_;

    public DashboardLogProvider(DashboardLogStore store)
    {
        store_ = store;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DashboardLogger(store_, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class DashboardLogger : ILogger
    {
        private readonly DashboardLogStore store_;
        private readonly string category_name_;

        public DashboardLogger(DashboardLogStore store, string categoryName)
        {
            store_ = store;
            category_name_ = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
            {
                return false;
            }

            if (category_name_.StartsWith("OpcBridge.", StringComparison.Ordinal))
            {
                return logLevel >= LogLevel.Information;
            }

            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            store_.Add(logLevel, category_name_, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
