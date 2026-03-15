using System;

namespace Plugin.Bazarr.Emby.Trigger.Integration;

public enum BazarrRequestFailureKind
{
    Connection,
    Api,
}

public sealed class BazarrRequestException : Exception
{
    public BazarrRequestException(BazarrRequestFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public BazarrRequestFailureKind Kind { get; }
}
