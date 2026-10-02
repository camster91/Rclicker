namespace RClicker.Server;

/// <summary>The server could not start. <see cref="Exception.Message"/> is written for the user.</summary>
public sealed class ServerStartException : Exception
{
    public ServerStartException()
    {
    }

    public ServerStartException(string message)
        : base(message)
    {
    }

    public ServerStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
