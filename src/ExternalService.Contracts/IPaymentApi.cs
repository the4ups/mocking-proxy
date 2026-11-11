using RestEase;

namespace ExternalService.Contracts;

public record PaymentRequestDto
{
    public required string OrderId { get; init; }
    public decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public record PaymentResponseDto
{
    public required string TransactionId { get; init; }
    public required string Status { get; init; }
    public decimal Amount { get; init; }
}

public interface IPaymentApi
{
    [Post("payments/charge")]
    Task<PaymentResponseDto> ChargeAsync([Body] PaymentRequestDto request);

    [Get("payments/{transactionId}")]
    Task<PaymentResponseDto> GetPaymentAsync([Path("transactionId")] string transactionId);
}
