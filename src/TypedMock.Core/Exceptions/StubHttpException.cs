namespace TypedMock.Core.Exceptions;

public class StubHttpException : Exception
{
    public int StatusCode { get; }
    public object? ResponseData { get; }

    public StubHttpException(int statusCode, string message, object? responseData = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseData = responseData;
    }
}
