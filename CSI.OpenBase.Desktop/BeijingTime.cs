namespace CSI.OpenBase.Desktop;

internal static class BeijingTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Offset);

    public static DateTimeOffset Convert(DateTimeOffset value) => value.ToOffset(Offset);
}
