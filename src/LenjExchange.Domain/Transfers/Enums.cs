namespace LenjExchange.Domain.Transfers;

public enum TransferStatus
{
    Draft = 0,
    ReceivedInIran = 1,
    AwaitingTurkishExchanger = 2,
    ReadyForUsdDelivery = 3,
    DeliveredToAgent = 4,
    InTransitToChina = 5,
    PartiallyReceivedInChina = 6,
    FullyReceivedInChina = 7,
    Settled = 8,
    Discrepancy = 9,
    Cancelled = 10
}

public enum DocumentStatus
{
    Draft = 0,
    Posted = 1,
    Reversed = 2
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public static class TransferStatusText
{
    public static string Persian(TransferStatus s) => s switch
    {
        TransferStatus.Draft => "پیش‌نویس",
        TransferStatus.ReceivedInIran => "دریافت وجه در ایران",
        TransferStatus.AwaitingTurkishExchanger => "در انتظار صرافی ترکیه",
        TransferStatus.ReadyForUsdDelivery => "آماده تحویل دلار",
        TransferStatus.DeliveredToAgent => "تحویل به نماینده",
        TransferStatus.InTransitToChina => "در مسیر چین",
        TransferStatus.PartiallyReceivedInChina => "دریافت جزئی در چین",
        TransferStatus.FullyReceivedInChina => "دریافت کامل در چین",
        TransferStatus.Settled => "تسویه‌شده",
        TransferStatus.Discrepancy => "دارای مغایرت",
        TransferStatus.Cancelled => "لغوشده/ابطال‌شده",
        _ => s.ToString()
    };

    public static string Persian(DocumentStatus s) => s switch
    {
        DocumentStatus.Draft => "پیش‌نویس",
        DocumentStatus.Posted => "قطعی",
        DocumentStatus.Reversed => "برگشت‌خورده",
        _ => s.ToString()
    };

    public static string Persian(ApprovalStatus s) => s switch
    {
        ApprovalStatus.Pending => "در انتظار تأیید",
        ApprovalStatus.Approved => "تأییدشده",
        ApprovalStatus.Rejected => "ردشده",
        _ => s.ToString()
    };
}
