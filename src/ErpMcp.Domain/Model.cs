namespace ErpMcp.Domain;

public sealed record Customer(
    string Id,
    string Name,
    string Country,
    string Email,
    string Iban,
    decimal CreditLimit);

public enum InvoiceStatus { Open, Paid, WrittenOff }

public sealed record Invoice(
    string Id,
    string CustomerId,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal Amount,
    decimal AmountPaid,
    string Currency,
    InvoiceStatus Status,
    string Memo)
{
    public decimal Outstanding => Status == InvoiceStatus.Open ? Amount - AmountPaid : 0m;

    public int DaysOverdue(DateOnly today) =>
        Status == InvoiceStatus.Open && today > DueDate ? today.DayNumber - DueDate.DayNumber : 0;
}

public enum JournalStatus { PendingApproval, Posted, Rejected }

public sealed record JournalLine(string Account, decimal Debit, decimal Credit);

public sealed class JournalEntry
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<JournalLine> Lines { get; init; }
    public required string DraftedBy { get; init; }
    public required DateTimeOffset DraftedAt { get; init; }
    public JournalStatus Status { get; internal set; } = JournalStatus.PendingApproval;
    public string? DecidedBy { get; internal set; }
    public DateTimeOffset? DecidedAt { get; internal set; }

    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
}

public sealed record Account(string Code, string Name);
