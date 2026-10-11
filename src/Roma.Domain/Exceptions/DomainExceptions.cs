namespace Roma.Domain.Exceptions;

public class DomainValidationException : Exception
{
    public DomainValidationException(string message) : base(message) { }
}

public class DomainConflictException : Exception
{
    public DomainConflictException(string message) : base(message) { }
}

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}
