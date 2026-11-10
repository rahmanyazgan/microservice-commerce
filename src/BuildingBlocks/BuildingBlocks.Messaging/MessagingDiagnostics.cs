using System.Diagnostics;

namespace BuildingBlocks.Messaging;

public static class MessagingDiagnostics
{
    public const string ActivitySourceName = "ETicaret.Messaging";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public const string RetryCountHeader = "x-retry-count";
    public const string TraceParentHeader = "traceparent";
    public const string ErrorHeader = "x-error";
    public const string OriginalQueueHeader = "x-original-queue";
}

