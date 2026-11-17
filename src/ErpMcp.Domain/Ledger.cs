using System.Collections.Concurrent;

namespace ErpMcp.Domain;

/// <summary>
/// In-memory sample ERP. Reads are open to the gateway; the only write path is
/// drafting a journal entry, and only a human approval can post it.
/// </summary>
public sealed class Ledger
{
    private readonly ConcurrentDictionary<string, Customer> _customers = new();
    private readonly ConcurrentDictionary<string, Invoice> _invoices = new();
    private readonly ConcurrentDictionary<string, JournalEntry> _journal = new();
    private readonly Dictionary<string, Account> _accounts;
    private readonly TimeProvider _clock;
    private int _journalSeq;

    public Ledger(TimeProvider clock, IEnumerable<Account> chartOfAccounts)
    {
        _clock = clock;
        _accounts = chartOfAccounts.ToDictionary(a => a.Code);
    }

    public DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    public void Add(Customer c) => _customers[c.Id] = c;
    public void Add(Invoice i) => _invoices[i.Id] = i;

    public IReadOnlyCollection<Account> Accounts => _accounts.Values;
    public IEnumerable<Customer> Customers => _customers.Values.OrderBy(c => c.Id);
    public Customer? FindCustomer(string id) => _customers.GetValueOrDefault(id.Trim().ToUpperInvariant());

    public IEnumerable<Invoice> Invoices => _invoices.Values.OrderBy(i => i.Id);
    public Invoice? FindInvoice(string id) => _invoices.GetValueOrDefault(id.Trim().ToUpperInvariant());

    public IEnumerable<JournalEntry> Journal => _journal.Values.OrderBy(j => j.Id);
    public JournalEntry? FindJournal(string id) => _journal.GetValueOrDefault(id.Trim().ToUpperInvariant());

    public JournalEntry DraftJournalEntry(string description, IReadOnlyList<JournalLine> lines, string draftedBy)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description is required.");
        if (lines.Count < 2)
            throw new DomainException("A journal entry needs at least two lines.");
        foreach (var l in lines)
        {
            if (!_accounts.ContainsKey(l.Account))
                throw new DomainException($"Unknown account '{l.Account}'. Use one of: {string.Join(", ", _accounts.Keys.Order())}.");
            if (l.Debit < 0 || l.Credit < 0)
                throw new DomainException("Debit and credit must not be negative.");
            if ((l.Debit == 0) == (l.Credit == 0))
                throw new DomainException("Each line must have either a debit or a credit, not both.");
        }
        var debit = lines.Sum(l => l.Debit);
        var credit = lines.Sum(l => l.Credit);
        if (debit != credit)
            throw new DomainException($"Entry is not balanced: debits {debit:0.00} vs credits {credit:0.00}.");

        var id = $"JE-{Interlocked.Increment(ref _journalSeq):0000}";
        var entry = new JournalEntry
        {
            Id = id,
            Description = description.Trim(),
            Lines = lines,
            DraftedBy = draftedBy,
            DraftedAt = _clock.GetUtcNow(),
        };
        _journal[id] = entry;
        return entry;
    }

    /// <summary>Human decision on a drafted entry. Enforces segregation of duties.</summary>
    public JournalEntry Decide(string entryId, string approver, bool approve)
    {
        var entry = FindJournal(entryId) ?? throw new DomainException($"Journal entry '{entryId}' not found.");
        if (entry.Status != JournalStatus.PendingApproval)
            throw new DomainException($"Journal entry {entry.Id} is already {entry.Status}.");
        if (string.Equals(entry.DraftedBy, approver, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Segregation of duties: the person who drafted an entry cannot approve it.");

        entry.Status = approve ? JournalStatus.Posted : JournalStatus.Rejected;
        entry.DecidedBy = approver;
        entry.DecidedAt = _clock.GetUtcNow();
        return entry;
    }
}

public sealed class DomainException(string message) : Exception(message);
