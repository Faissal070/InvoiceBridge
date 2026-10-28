\# InvoiceBridge



Demo project: syncs webshop orders to Moneybird as invoices, with a Blazor dashboard.



\## Stack

\- .NET 10, Blazor Web App (Interactive Server)

\- EF Core with SQLite

\- Project lives in src/InvoiceBridge



\## Git workflow (always follow)

\- Never commit directly to `main`.

\- Every feature gets its own branch, created from an up-to-date `main`:

&#x20; `feature/<short-name>` (e.g. `feature/mock-shop`, `feature/moneybird-client`).

&#x20; Use `fix/<short-name>` for bug fixes.

\- Make small, focused commits on the feature branch.

\- All commit messages in English, imperative mood

&#x20; (e.g. "Add mock shop orders endpoint", "Fix retry on timeout").

\- Before merging: the project must build (`dotnet build`) and run without errors.

\- Do NOT merge on your own. When a feature is done, summarize what changed

&#x20; and ask me to review. Only merge after I approve.

\- Merge into `main` with `git merge --no-ff <branch>`, then delete the branch.



\## Rules

\- Never put secrets (API tokens, passwords) in code or in Git.

&#x20; Use `dotnet user-secrets` locally.

\- Use only fake/demo data, never real customer data.

\- Explain what you did in Dutch, step by step, after each feature.



## Project structure (always follow)

Code in `src/InvoiceBridge` is organized **by type**, not by feature.
The namespace always matches the folder (`InvoiceBridge.<Folder>`), one type per file.

| Folder | Namespace | Contains |
|---|---|---|
| `Clients/` | `InvoiceBridge.Clients` | HTTP clients for external APIs, plus their interfaces (`IMoneybirdClient`, `MoneybirdClient`, `MockShopClient`) |
| `Services/` | `InvoiceBridge.Services` | Business logic (`OrderSyncService`, `OrderInvoicingService`) |
| `Workers/` | `InvoiceBridge.Workers` | `BackgroundService` implementations (`OrderSyncWorker`) |
| `Models/` | `InvoiceBridge.Models` | Entities, enums, DTOs and result records (`SyncedOrder`, `SyncStatus`, `MockOrder`, `MoneybirdInvoice`, ...) |
| `Options/` | `InvoiceBridge.Options` | Configuration classes bound from appsettings/user-secrets (`MoneybirdOptions`) |
| `Exceptions/` | `InvoiceBridge.Exceptions` | Custom exceptions (`MoneybirdException`) |
| `Data/` | `InvoiceBridge.Data` | `InvoiceBridgeDbContext` and `Migrations/` only (entities go in `Models/`) |
| `MockShop/` | `InvoiceBridge.MockShop` | The fake webshop itself: minimal API endpoints and seed data |
| `Components/` | `InvoiceBridge.Components` | Blazor layout and pages |

- New code goes in the folder for its type; do not create feature folders.
- Tests in `tests/InvoiceBridge.Tests` mirror this structure
  (e.g. `Services/OrderInvoicingServiceTests.cs`, namespace `InvoiceBridge.Tests.Services`).
- Inside `InvoiceBridge.*` namespaces, `Options` refers to the `InvoiceBridge.Options` namespace.
  Use `Microsoft.Extensions.Options.Options.Create(...)` fully qualified when needed.
- When an entity moves namespace, update the type name in the EF migration snapshot and
  check with `dotnet ef migrations has-pending-model-changes` that no migration is needed.

