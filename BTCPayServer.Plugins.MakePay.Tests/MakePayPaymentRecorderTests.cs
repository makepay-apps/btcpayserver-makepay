#nullable enable
using BTCPayServer.Data;
using BTCPayServer.Plugins.MakePay.Services;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.Tests;

public class MakePayPaymentRecorderTests
{
    [Theory]
    [InlineData("deposit_received")]
    [InlineData("swapping")]
    [InlineData("sending")]
    [InlineData("complete")]
    [InlineData(" DEPOSIT_RECEIVED ")]
    public void DepositReceiptAndLaterStatesCreateAProcessingPayment(string status)
    {
        Assert.Equal(
            PaymentStatus.Processing,
            MakePayPaymentRecorder.MapSessionStatusToPaymentStatus(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("quoted")]
    [InlineData("awaiting_deposit")]
    [InlineData("pending")]
    [InlineData("underpaid")]
    [InlineData("expired")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("refunded")]
    public void PreDepositAndTerminalFailureStatesDoNotCreateAPayment(string? status)
    {
        Assert.Null(MakePayPaymentRecorder.MapSessionStatusToPaymentStatus(status));
    }
}
