using LenjExchange.Domain.Common;
using LenjExchange.Domain.Monetary;

namespace LenjExchange.Domain.Transfers;

/// <summary>
/// A transfer case (پرونده انتقال) groups all documents of one Iran → Turkey → China transfer.
/// Its status is never set by hand (except cancel / discrepancy flags); it is derived from posted documents.
/// </summary>
public class TransferCase : AuditableEntity
{
    public string CaseNumber { get; private set; } = "";
    public Guid CustomerId { get; private set; }
    public Guid? FinalRecipientId { get; private set; }
    /// <summary>Currency the commitment to the customer is denominated in (normally USD).</summary>
    public CurrencyCode TargetCurrency { get; private set; } = CurrencyCode.USD;
    public TransferStatus Status { get; private set; } = TransferStatus.Draft;
    public bool IsCancelled { get; private set; }
    public string? CancelReason { get; private set; }
    public bool DiscrepancyFlag { get; private set; }
    public string? DiscrepancyNote { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? LastStatusChangeUtc { get; private set; }

    protected TransferCase() { }

    public TransferCase(string caseNumber, Guid customerId, Guid? finalRecipientId, CurrencyCode targetCurrency, string? notes)
    {
        CaseNumber = Guard.NotBlank(caseNumber, "شماره پرونده");
        CustomerId = customerId;
        FinalRecipientId = finalRecipientId;
        TargetCurrency = targetCurrency;
        Notes = notes;
    }

    public void Cancel(string reason)
    {
        CancelReason = Guard.NotBlank(reason, "علت ابطال");
        IsCancelled = true;
    }

    public void FlagDiscrepancy(string note)
    {
        DiscrepancyFlag = true;
        DiscrepancyNote = note;
    }

    public void ClearDiscrepancy()
    {
        DiscrepancyFlag = false;
        DiscrepancyNote = null;
    }

    public TransferCaseSnapshot Recalculate(
        IEnumerable<CustomerReceipt> receipts,
        IEnumerable<ExchangerCommitment> commitments,
        IEnumerable<AgentDelivery> deliveries,
        IEnumerable<ChinaReceipt> chinaReceipts,
        DateTime utcNow)
    {
        var snap = TransferStatusCalculator.Calculate(this, receipts, commitments, deliveries, chinaReceipts);
        if (snap.Status != Status)
        {
            Status = snap.Status;
            LastStatusChangeUtc = Guard.Utc(utcNow);
        }
        return snap;
    }
}

public sealed record TransferCaseSnapshot(
    TransferStatus Status,
    Money Target,
    Money Committed,
    Money DeliveredToAgents,
    Money InTransit,
    Money ReceivedInChina,
    IReadOnlyList<string> Issues)
{
    /// <summary>Amount still owed to the final recipient in the case target currency.</summary>
    public Money Unsettled => Target - ReceivedInChina;
    public Money ExchangerOutstanding => Committed - DeliveredToAgents;
}

public static class TransferStatusCalculator
{
    public static TransferCaseSnapshot Calculate(
        TransferCase c,
        IEnumerable<CustomerReceipt> receipts,
        IEnumerable<ExchangerCommitment> commitments,
        IEnumerable<AgentDelivery> deliveries,
        IEnumerable<ChinaReceipt> chinaReceipts)
    {
        var cur = c.TargetCurrency;
        var postedReceipts = receipts.Where(r => r.Status == DocumentStatus.Posted).ToList();
        var postedCommitments = commitments.Where(x => x.Status == DocumentStatus.Posted).ToList();
        var approvedDeliveries = deliveries.Where(d => d.Counts).ToList();
        var china = chinaReceipts.ToList();
        var issues = new List<string>();

        Money Sum(IEnumerable<Money> items) => items.Aggregate(Money.Zero(cur), (a, b) => a + CurrencyMath.ConvertFixed(b, cur));

        var target = Sum(postedReceipts.Select(r => r.TargetTotal));
        var committed = Sum(postedCommitments.Where(x => x.CommittedCurrency == cur || CurrencyMath.AreFixedEquivalent(x.CommittedCurrency, cur)).Select(x => x.Committed));
        var delivered = cur == CurrencyCode.USD ? Sum(approvedDeliveries.Select(d => d.Money)) : Money.Zero(cur);
        var inTransit = cur == CurrencyCode.USD ? Sum(china.Where(x => x.IsPendingInTransit).Select(x => x.SettledUsd)) : Money.Zero(cur);
        var received = cur == CurrencyCode.USD ? Sum(china.Where(x => x.Counts).Select(x => x.SettledUsd)) : Money.Zero(cur);

        if (c.DiscrepancyFlag) issues.Add(c.DiscrepancyNote ?? "مغایرت ثبت‌شده توسط کاربر");
        if (postedReceipts.Any(r => r.MismatchAccepted)) issues.Add("مغایرت جمع واریزها با مبلغ رسید");
        if (received > target) issues.Add("مبلغ دریافتی در چین بیشتر از مبلغ تعهد به مشتری است");
        if (delivered > committed) issues.Add("تحویل به نماینده بیشتر از تعهد صرافی است");
        foreach (var x in postedCommitments)
        {
            var del = approvedDeliveries.Where(d => d.ExchangerCommitmentId == x.Id).Aggregate(Money.Zero(x.CommittedCurrency), (a, d) => a + d.Money);
            if (x.CommittedCurrency == CurrencyCode.USD && del > x.Committed) issues.Add("تحویل بیش از تعهد یکی از صرافی‌ها");
        }

        TransferStatus status;
        if (c.IsCancelled) status = TransferStatus.Cancelled;
        else if (postedReceipts.Count == 0) status = TransferStatus.Draft;
        else if (issues.Count > 0) status = TransferStatus.Discrepancy;
        else if (!received.IsZero && received >= target)
        {
            var allCommitmentsSettled = postedCommitments.Count > 0 && postedCommitments.All(x =>
                approvedDeliveries.Where(d => d.ExchangerCommitmentId == x.Id)
                    .Aggregate(Money.Zero(x.CommittedCurrency), (a, d) => a + d.Money) >= x.Committed);
            status = allCommitmentsSettled ? TransferStatus.Settled : TransferStatus.FullyReceivedInChina;
        }
        else if (!received.IsZero) status = TransferStatus.PartiallyReceivedInChina;
        else if (!inTransit.IsZero) status = TransferStatus.InTransitToChina;
        else if (!delivered.IsZero) status = TransferStatus.DeliveredToAgent;
        else if (postedCommitments.Any(x => x.ReadyForDelivery)) status = TransferStatus.ReadyForUsdDelivery;
        else if (postedCommitments.Count > 0) status = TransferStatus.AwaitingTurkishExchanger;
        else status = TransferStatus.ReceivedInIran;

        return new TransferCaseSnapshot(status, target, committed, delivered, inTransit, received, issues);
    }
}
