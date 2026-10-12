# Architecture — سامانه مالی لنج | LENJ Exchange Manager

Offline Windows desktop application (Persian / RTL) for customer accounts and Iran → Turkey → China currency transfers.

## Solution layout

```
LenjExchange.sln
├─ src/LenjExchange.Domain          net8.0   – entities, value objects, rules; no dependencies
├─ src/LenjExchange.Application     net8.0   – use cases/services; depends on Domain + provider-neutral EF Core
├─ src/LenjExchange.Infrastructure  net8.0   – SQLite DbContext + migrations, QuestPDF, ClosedXML, backup
├─ src/LenjExchange.Desktop         net8.0-windows (WPF) – MVVM UI, composition root
├─ tests/LenjExchange.Tests         net8.0   – xUnit unit + integration tests (real SQLite file)
├─ installer/                       – Inno Setup script, publish scripts, prerequisite bootstrapper
├─ References/                      – customer-supplied templates and the extracted brand spec
└─ docs/                            – this documentation
```

Dependency direction: `Desktop → Application/Infrastructure → Domain`. `Application` talks to the database only
through `IAppDbContext` (DbSets + `DatabaseFacade`), so the SQLite provider lives entirely in Infrastructure and
can be replaced (`DatabaseProvider` enum in `Infrastructure/DependencyInjection.cs`; only `Sqlite` is implemented).

## Key building blocks

| Concern | Where | Notes |
|---|---|---|
| Money | `Domain/Monetary` | `Money` is a value type; arithmetic across currencies throws. IRT/IRR fixed factor 10. |
| Rates | `ExchangeRate` | Explicit direction: `1 Base = Rate Quote`. Every document stores its own snapshot. |
| Ledger | `Domain/Ledger`, `Application/Ledger` | Double entry, balanced **per currency**; Draft → Posted → Reversed. |
| Transfer flow | `Application/Transfers` | Receipt → exchanger commitment → agent delivery → China receipt. See `accounting-rules.md`. |
| Status | `TransferStatusCalculator` | Derived from documents every time; never typed by the user. |
| Numbering | `ReceiptNumberPattern`, `NumberingService` | Pattern tokens, DB-backed sequences allocated inside the posting transaction + unique indexes. |
| Calendar | `Domain/Shamsi` | `PersianCalendar`; week starts Saturday; `DateRanges` builds whole-day local ranges → UTC half-open interval. |
| Transactions | `UnitOfWork` | One DB transaction per use case; nested calls join the ambient transaction; tracker cleared on rollback. |
| Security | `Application/Security` | PBKDF2-SHA256 (600k iterations), roles Admin/Operator + extra permissions, audit log. |
| Attachments | `AttachmentService` | JPG/PNG/PDF by extension + magic bytes, ≤ 25 MB, SHA-256 duplicate detection, read-only files. |
| Documents | `Infrastructure/Pdf` | QuestPDF, embedded Vazirmatn font, RTL; Latin runs isolated with LRE/PDF marks. |
| Excel | `Infrastructure/Excel` | ClosedXML, RTL worksheets. |
| Backup | `Infrastructure/Backup` | Zip = manifest (SHA-256) + SQLite online-backup copy + attachments; verify; admin restore. |
| Logging | Serilog | Rolling file under `%LOCALAPPDATA%\LenjExchange\Logs`. |

## Desktop (WPF)

* MVVM with CommunityToolkit.Mvvm; Generic Host for DI, Serilog and the `AutoBackupWorker` background service.
* `NavigationService` creates **one DI scope per page** (one `DbContext` per page) and disposes the previous one.
* `ShellViewModel` shows only the menu items the signed-in user has permission for; services re-check every
  permission (`ICurrentUser.Demand`), so hiding a button is never the only protection.
* Views: Login / first-run, Dashboard, Customers, New receipt, Transfer cases (timeline + journal), Turkish
  exchangers, Turkish agents, China receipts, Rates, Reports, Attachments archive, Users + audit log, Settings,
  Backup/Restore.
* Theme colours and fonts follow `References/BRAND-AND-LAYOUT-SPEC.md` (navy `#0B2F5B`, surfaces `#EEF2F7` /
  `#F5F8FB`, text `#526174` / `#1A1A1A`, Vazirmatn).

## Runtime data

Everything is local; no network access is needed or used.

| Path | Content |
|---|---|
| `%LOCALAPPDATA%\LenjExchange\Data\lenj.db` | SQLite database (migrated automatically on start) |
| `%LOCALAPPDATA%\LenjExchange\Attachments\yyyy\MM\` | Archived attachments (read-only files, relative paths in DB) |
| `%LOCALAPPDATA%\LenjExchange\Backups\` | Backup zips (configurable; keep N versions) |
| `%LOCALAPPDATA%\LenjExchange\Logs\` | Serilog files |
| `Documents\LenjExchange\Receipts\<yyyy>\<MM>\` | Receipt PDFs, never overwritten (`name (2).pdf`) |
| `Documents\LenjExchange\Reports\` | Exported reports |

## Build, test, publish

```bash
dotnet test LenjExchange.sln                       # 80 tests (Linux or Windows)
dotnet build src/LenjExchange.Desktop              # builds on Linux thanks to EnableWindowsTargeting
installer/publish.sh                               # cross-publish the win-x64 single-file EXE
```

On Windows, `installer\publish.ps1` runs the tests, publishes `artifacts\publish\LenjExchange.exe` and compiles
`installer\LenjExchange.iss` with Inno Setup 6. `installer\windows-setup\Install-Prerequisites.cmd` installs
Git, the .NET 8 SDK and Inno Setup on a clean machine and then runs the same steps.

EF Core migrations: `dotnet tool restore && dotnet ef migrations add <Name> -p src/LenjExchange.Infrastructure`
(a design-time factory is included).
