namespace WCM.Application.Exceptions;

/// <summary>The database can't be trusted, so work that depends on it must stop.</summary>
public sealed class DatabaseStateException : Exception
{
    public DatabaseStateException(string message)
        : base(message)
    {
    }

    public DatabaseStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
