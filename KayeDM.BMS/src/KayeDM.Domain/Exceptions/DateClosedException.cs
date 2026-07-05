namespace KayeDM.Domain.Exceptions;

public class DateClosedException : DomainException
{
    public DateClosedException(string message) : base(message)
    {
    }
}
