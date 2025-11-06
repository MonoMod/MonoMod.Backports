namespace CompatUnbreaker.Attributes;

public sealed class NotSupportedAnymoreException : NotSupportedException
{
    public NotSupportedAnymoreException()
    {
    }

    public NotSupportedAnymoreException(string message) : base(message)
    {
    }

    public NotSupportedAnymoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
