# Database design

SQLite through EF Core 8 (`LenjDbContext`, migration `InitialCreate`). Conventions applied to every entity:

* Primary keys are application-generated `Guid`s (except `AuditLog.Id`, an auto-increment `long`).
* `DateTime` values are stored as UTC; a value converter marks them `DateTimeKind.Utc` on read.
* Decimals are `decimal(28,8)`; SQLite keeps them as TEXT, so sums are done in memory after loading lines
  (exact decimal arithmetic, no floating point).
* `CurrencyCode` is stored as a string (`IRT`, `IRR`, `USD`, `CNY`, `USDT`).
* Exchange-rate snapshots are owned types stored as three columns on the owning document:
  `RateBase`, `RateQuote`, `RateValue`.
* Customers are soft-deleted (`IsDeleted`, excluded by the customer queries); ledger rows are never deleted.

## Tables

| Table | Purpose | Important columns / constraints |
|---|---|---|
| `Customers` | Payers | `Code` **unique**, `FullName`, `Phone`, `Address`, `IsActive`, `IsDeleted` |
| `Exchangers` | Turkish exchangers (صرافی عابدی، داریوش عباسی) | `Name` **unique** |
| `Agents` | Turkish agents holding USD (آیدین، چاتای) | `Name` **unique** |
| `FinalRecipients` | Recipients in China (محمد) | `AcceptedCurrenciesCsv` (e.g. `USD,CNY,USDT`) |
| `Banks` | Iranian banks receiving deposits | `Name` **unique** |
| `Accounts` | Chart of accounts | `Code` **unique**, `Category`, `LinkedEntityId` (customer/exchanger/agent/bank/recipient) |
| `JournalEntries` | Ledger headers | `EntryNumber` **unique**, `OperationType`, `Status`, `TransferCaseId`, `SourceDocumentType/SourceDocumentId`, `ReversalOfEntryId`, `ReversedByEntryId`, posted/reversed by/at |
| `JournalLines` | Ledger lines | `AccountId`, `Currency`, `Debit`, `Credit` (exactly one > 0), `BaseRate`, `BaseAmount` |
| `TransferCases` | One transfer from a customer to a final recipient | `CaseNumber` **unique**, `TargetCurrency`, derived `Status`, `DiscrepancyFlag`, `IsCancelled` |
| `CustomerReceipts` | Money received in Iran | `ReceiptNumber` **unique** (null while draft), amount + currency, target amount + currency, rate snapshot, `Status`, `MismatchAccepted`, `JournalEntryId` |
| `BankDeposits` | Deposits that make up a receipt | `BankName`, `BankId`, `Amount`, `Currency`, `BankTrackingNo` (indexed), `Depositor`, date |
| `ExchangerCommitments` | Toman paid to an exchanger and the USD it owes | paid amount/currency, rate snapshot, committed amount/currency, `ReadyForDelivery`, `Status`, `JournalEntryId` |
| `AgentDeliveries` | USD handed by an exchanger to an agent | `TrackingNo` **unique**, `ExternalTrackingNo`, `Approval`, `IsReversed`, `JournalEntryId` |
| `ChinaReceipts` | Money received by the final recipient | settled USD, paid amount/currency, rate snapshot, `Approval`, `DispatchJournalEntryId`, `ReceiptJournalEntryId` |
| `DailyRates` | Reference rates (form defaults only) | effective date, base, quote, rate |
| `NumberSequences` | Document counters | `Key` primary key (one row per document kind and period), `LastValue` |
| `Users` | Application users | `UserName` **unique** (lower-case), `PasswordHash`, `Role`, `ExtraPermissionsCsv`, `IsActive` |
| `AuditLog` | Who did what | timestamp, user, operation, entity type/id, details |
| `Attachments` | Archived files | `Sha256` (indexed), relative path, content type, size, owner type/id, customer/case |
| `ReceiptPrints` | Every generated receipt PDF | receipt id/number, paper size, file path, printed by/at, sent to printer |
| `Settings` | Key/value JSON settings | company header, numbering, paths, backup, accounting |

## Chart of accounts (system codes)

| Code | Account | Category |
|---|---|---|
| 1100 | Iran cash (صندوق ایران) | IranCash |
| 1200-x | One per bank | Bank |
| 1300-x | One per exchanger | Exchanger |
| 1400-x | One per agent | AgentFunds |
| 1500 | In transit to China | InTransitToChina |
| 1600-x | One per final recipient | Recipient |
| 2100-x | One per customer | Customer |
| 3100 | Clearing | Clearing |
| 3200 | FX conversion | FxConversion |
| 4100 | Fees | Fees |
| 4200 | FX gain/loss | FxGainLoss |
| 4300 | Discrepancy | Discrepancy |

Per-party accounts are created on first use (`AccountDirectory.GetOrCreate…`).

## Integrity guarantees

* Unique indexes on every human-visible number (receipt, case, journal entry, delivery tracking number, customer
  code, account code, user name) back up the sequence allocation.
* Numbers are allocated inside the same transaction that posts the document, so a failed posting does not
  consume a number.
* A journal entry can only be posted when it balances in every currency.
* Posted entries are immutable; corrections are reversal entries linked both ways.
