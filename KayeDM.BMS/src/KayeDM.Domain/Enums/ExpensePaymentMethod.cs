namespace KayeDM.Domain.Enums;

// Separate from KayeDM.Domain.Enums.PaymentMethod (Cash/GCash only, used by
// Order) because expenses can also be paid by bank transfer.
public enum ExpensePaymentMethod
{
    Cash,
    GCash,
    BankTransfer
}
