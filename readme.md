# My Project

Initial project setup.

---

# سامانه مالی لنج | LENJ Exchange Manager

A Persian (RTL), offline Windows desktop app for **شرکت بازرگانی لنج**. It manages customer accounts and
Iran → Turkey → China money transfers using a double-entry ledger.

برنامهٔ دسکتاپ ویندوز (فارسی و راست‌به‌چپ، آفلاین) برای مدیریت حساب مشتریان و حواله‌های ایران ← ترکیه ← چین،
با دفتر حسابداری دوطرفه.

## Stack

- C# / .NET 8, WPF (MVVM with CommunityToolkit.Mvvm, Generic Host).
- EF Core 8 + SQLite, with migrations.
- QuestPDF (Community) with embedded Vazirmatn for RTL PDFs; ClosedXML for Excel export.
- Serilog for logging; xUnit for tests.
- Data is stored in `%LOCALAPPDATA%\LenjExchange`.

## Repository layout

| Path | Contents |
|---|---|
| `LenjExchange.sln` | Solution |
| `src/LenjExchange.Domain` | Money, currencies, rates, parties, transfer documents, ledger, numbering, security, Shamsi/digit utilities |
| `src/LenjExchange.Application` | Use-case services (receipts, transfer flow, reversal, rates, reports, users, settings, attachments) |
| `src/LenjExchange.Infrastructure` | SQLite DbContext and migrations, PDF renderers, Excel export, backup/restore |
| `src/LenjExchange.Desktop` | WPF app (login/first run and all screens) |
| `tests/LenjExchange.Tests` | Unit, integration and acceptance tests |
| `installer/` | Inno Setup script, `publish.ps1` / `publish.sh`, Windows prerequisites bootstrap |
| `docs/` | Guides and design docs |
| `References/` | Receipt template, sample statement, brand spec |

## Build, test, publish

```bash
dotnet build LenjExchange.sln
dotnet test
./installer/publish.sh                                          # Linux/macOS: EXE only
powershell -ExecutionPolicy Bypass -File installer\publish.ps1  # Windows: EXE + Setup (needs Inno Setup 6)
```

The output is a self-contained win-x64 single-file `artifacts/publish/LenjExchange.exe`. On Windows, Setup goes to
`installer/Output/`. The WPF project builds on Linux via `EnableWindowsTargeting`, but it only runs on Windows.

## Documentation

- [راهنمای نصب](docs/install-guide.fa.md) · [راهنمای کاربر](docs/user-guide.fa.md)
- [Architecture](docs/architecture.md) · [Database design](docs/db-design.md) · [Accounting rules](docs/accounting-rules.md)
- [Open questions](docs/open-questions.md): defaults chosen where business input is still needed.

## Status

**Done and covered by automated tests:**
- Domain model and ledger, including per-currency balancing, FX legs, reversal with dependency checks and derived
  transfer status.
- Receipt numbering.
- Receipt, transfer-flow and reversal services; reports (PDF and Excel); backup/restore; attachments; users,
  permissions and audit.
- The six acceptance scenarios.

**Built, but not run on Windows yet:**
- The WPF desktop UI. It compiles with 0 warnings, but its runtime behaviour, bindings and layout are unverified.
- Printing (Windows shell print verb).
- Inno Setup compile.

**Not implemented:**
- Fees.
- A customer payment/refund screen (the service exists).
- Automatic FX gain/loss recognition.
- Discharging the customer's USD balance on China receipt.
- Extra receipt template fields.

See [open questions](docs/open-questions.md).
