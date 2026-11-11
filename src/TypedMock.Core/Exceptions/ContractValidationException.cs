namespace TypedMock.Core.Exceptions;

public class ContractValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
