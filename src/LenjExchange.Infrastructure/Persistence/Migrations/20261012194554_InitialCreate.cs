using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LenjExchange.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Nature = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    LinkedEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    StoredRelativePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DocType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TransferCaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OwnerType = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectiveDateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exchangers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exchangers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinalRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AcceptedCurrenciesCsv = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalRecipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryNumber = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TransferCaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceDocumentType = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaseCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReversalOfEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedByEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReversedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NumberSequences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastValue = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSequences", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptPrints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PrintedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrintedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PaperSize = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SentToPrinter = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptPrints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExtraPermissionsCsv = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransferCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CaseNumber = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinalRecipientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DiscrepancyFlag = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscrepancyNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastStatusChangeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferCases_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangerCommitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferCaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    PaidCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    FundingBankId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RateBase = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RateQuote = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RateValue = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: true),
                    CommittedAmount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    CommittedCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ReadyForDelivery = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangerCommitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangerCommitments_Exchangers_ExchangerId",
                        column: x => x.ExchangerId,
                        principalTable: "Exchangers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChinaReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferCaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinalRecipientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledUsdAmount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    PaidCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    RateBase = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RateQuote = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RateValue = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: true),
                    TrackingNo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Approval = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DispatchJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReceiptJournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChinaReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChinaReceipts_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChinaReceipts_FinalRecipients_FinalRecipientId",
                        column: x => x.FinalRecipientId,
                        principalTable: "FinalRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Debit = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    BaseRate = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: true),
                    BaseAmount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TransferCaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinalRecipientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    RateBase = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RateQuote = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RateValue = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: true),
                    TargetCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MismatchAccepted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinalizedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_TransferCases_TransferCaseId",
                        column: x => x.TransferCaseId,
                        principalTable: "TransferCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferCaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangerCommitmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    TrackingNo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ExternalTrackingNo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Approval = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentDeliveries_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentDeliveries_ExchangerCommitments_ExchangerCommitmentId",
                        column: x => x.ExchangerCommitmentId,
                        principalTable: "ExchangerCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankDeposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 28, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    BankTrackingNo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Depositor = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankDeposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankDeposits_CustomerReceipts_CustomerReceiptId",
                        column: x => x.CustomerReceiptId,
                        principalTable: "CustomerReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Category_LinkedEntityId",
                table: "Accounts",
                columns: new[] { "Category", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Code",
                table: "Accounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDeliveries_AgentId",
                table: "AgentDeliveries",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDeliveries_ExchangerCommitmentId",
                table: "AgentDeliveries",
                column: "ExchangerCommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDeliveries_TrackingNo",
                table: "AgentDeliveries",
                column: "TrackingNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDeliveries_TransferCaseId",
                table: "AgentDeliveries",
                column: "TransferCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Name",
                table: "Agents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OwnerType_OwnerId",
                table: "Attachments",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TimestampUtc",
                table: "AuditLog",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_BankName_DateUtc",
                table: "BankDeposits",
                columns: new[] { "BankName", "DateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_BankTrackingNo",
                table: "BankDeposits",
                column: "BankTrackingNo");

            migrationBuilder.CreateIndex(
                name: "IX_BankDeposits_CustomerReceiptId",
                table: "BankDeposits",
                column: "CustomerReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_Banks_Name",
                table: "Banks",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChinaReceipts_AgentId",
                table: "ChinaReceipts",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChinaReceipts_FinalRecipientId",
                table: "ChinaReceipts",
                column: "FinalRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_ChinaReceipts_TransferCaseId",
                table: "ChinaReceipts",
                column: "TransferCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_CustomerId",
                table: "CustomerReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_DateUtc",
                table: "CustomerReceipts",
                column: "DateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_ReceiptNumber",
                table: "CustomerReceipts",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_TransferCaseId",
                table: "CustomerReceipts",
                column: "TransferCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Code",
                table: "Customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_FullName",
                table: "Customers",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRates_BaseCurrency_QuoteCurrency_EffectiveDateUtc",
                table: "DailyRates",
                columns: new[] { "BaseCurrency", "QuoteCurrency", "EffectiveDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangerCommitments_ExchangerId",
                table: "ExchangerCommitments",
                column: "ExchangerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangerCommitments_TransferCaseId",
                table: "ExchangerCommitments",
                column: "TransferCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Exchangers_Name",
                table: "Exchangers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalRecipients_Name",
                table: "FinalRecipients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_DateUtc",
                table: "JournalEntries",
                column: "DateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryNumber",
                table: "JournalEntries",
                column: "EntryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SourceDocumentType_SourceDocumentId",
                table: "JournalEntries",
                columns: new[] { "SourceDocumentType", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TransferCaseId",
                table: "JournalEntries",
                column: "TransferCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountId",
                table: "JournalLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_JournalEntryId",
                table: "JournalLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrints_CustomerReceiptId",
                table: "ReceiptPrints",
                column: "CustomerReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferCases_CaseNumber",
                table: "TransferCases",
                column: "CaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferCases_CustomerId",
                table: "TransferCases",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferCases_Status",
                table: "TransferCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentDeliveries");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "BankDeposits");

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropTable(
                name: "ChinaReceipts");

            migrationBuilder.DropTable(
                name: "DailyRates");

            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "NumberSequences");

            migrationBuilder.DropTable(
                name: "ReceiptPrints");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ExchangerCommitments");

            migrationBuilder.DropTable(
                name: "CustomerReceipts");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "FinalRecipients");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "Exchangers");

            migrationBuilder.DropTable(
                name: "TransferCases");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
