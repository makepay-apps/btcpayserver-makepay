#nullable enable
using BTCPayServer.Plugins.MakePay.PaymentHandler;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.Tests;

public class MakePayConnectedCompanyUrlTests
{
    [Fact]
    public void ConnectedCompanyUrlsUseTheResolvedRouteKey()
    {
        var config = new MakePayPaymentMethodConfig();

        config.SetConnectedCompanyUrls("demo-shop-2");

        Assert.Equal(
            "https://www.makecrypto.io/home/demo-shop-2/merchant/payment-settings",
            config.ConnectedPaymentSettingsUrl);
        Assert.Equal(
            "https://www.makecrypto.io/home/demo-shop-2/wallet",
            config.ConnectedWalletUrl);
    }

    [Fact]
    public void ConnectedCompanyUrlsEscapeTheResolvedRouteKey()
    {
        var config = new MakePayPaymentMethodConfig
        {
            ApiBaseUrl = "https://portal.example.test/"
        };

        config.SetConnectedCompanyUrls(" demo shop/2 ");

        Assert.Equal(
            "https://portal.example.test/home/demo%20shop%2F2/merchant/payment-settings",
            config.ConnectedPaymentSettingsUrl);
        Assert.Equal(
            "https://portal.example.test/home/demo%20shop%2F2/wallet",
            config.ConnectedWalletUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConnectedCompanyUrlsFallBackToHomeWithoutARouteKey(string? routeKey)
    {
        var config = new MakePayPaymentMethodConfig();

        config.SetConnectedCompanyUrls(routeKey);

        Assert.Equal("https://www.makecrypto.io/home", config.ConnectedPaymentSettingsUrl);
        Assert.Equal("https://www.makecrypto.io/home", config.ConnectedWalletUrl);
    }
}
