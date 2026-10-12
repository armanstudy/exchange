using LenjExchange.Application.Transfers;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Parties;
using LenjExchange.Domain.Transfers;

namespace LenjExchange.Tests.Support;

/// <summary>Builds the standard Iran → Turkey → China flow used by integration and acceptance tests.</summary>
public static class Flow
{
    public static readonly ExchangeRate UsdIrt100k = new(CurrencyCode.USD, CurrencyCode.IRT, 100_000m);

    public static async Task<(Customer Customer, CustomerReceipt Receipt)> ReceiveAsync(TestApp app, decimal irt = 100_000_000m,
        ExchangeRate? rate = null, string customerName = "علی احمدی", DateTime? dateUtc = null, Customer? customer = null)
    {
        customer ??= await app.NewCustomerAsync(customerName);
        var mohammad = await app.MohammadAsync();
        var date = dateUtc ?? app.Clock.UtcNow;
        var receipt = await app.Get<ReceiptService>().RegisterAsync(new ReceiptInput(
            customer.Id, date, irt, CurrencyCode.IRT, CurrencyCode.USD, rate ?? UsdIrt100k, mohammad.Id,
            new[] { new DepositInput("بانک ملت", date, irt, CurrencyCode.IRT, $"TRK-{Guid.NewGuid():N}"[..16], customer.FullName) }));
        return (customer, receipt);
    }

    public static async Task<ExchangerCommitment> CommitAsync(TestApp app, CustomerReceipt receipt, decimal irt = 100_000_000m, ExchangeRate? rate = null)
    {
        var ex = await app.ExchangerAsync();
        return await app.Get<TransferFlowService>().CreateCommitmentAsync(new CommitmentInput(
            receipt.TransferCaseId, ex.Id, app.Clock.UtcNow, irt, CurrencyCode.IRT, rate ?? UsdIrt100k));
    }

    public static async Task<AgentDelivery> DeliverAsync(TestApp app, ExchangerCommitment c, decimal usd, string agentName = "آیدین")
    {
        var agent = await app.AgentAsync(agentName);
        var flow = app.Get<TransferFlowService>();
        var d = await flow.RegisterDeliveryAsync(new DeliveryInput(c.Id, agent.Id, app.Clock.UtcNow, usd, "EXT-1"));
        return await flow.ApproveDeliveryAsync(d.Id);
    }

    public static async Task<ChinaReceipt> ChinaAsync(TestApp app, Guid caseId, decimal usd, CurrencyCode paid = CurrencyCode.USD,
        ExchangeRate? rate = null, bool approve = true, string agentName = "آیدین")
    {
        var agent = await app.AgentAsync(agentName);
        var mohammad = await app.MohammadAsync();
        var flow = app.Get<TransferFlowService>();
        var cr = await flow.RegisterChinaReceiptAsync(new ChinaReceiptInput(caseId, agent.Id, mohammad.Id, app.Clock.UtcNow, usd, paid, rate, "CN-1"));
        return approve ? await flow.ApproveChinaReceiptAsync(cr.Id) : cr;
    }
}
