namespace KayeDM.Domain.Exceptions;

public class OversoldException : DomainException
{
    public OversoldException(string message) : base(message)
    {
    }
}
