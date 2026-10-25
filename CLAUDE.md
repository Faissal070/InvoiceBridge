\# OrderSync



Demo project: syncs webshop orders to Moneybird as invoices, with a Blazor dashboard.



\## Stack

\- .NET 10, Blazor Web App (Interactive Server)

\- EF Core with SQLite

\- Project lives in src/OrderSync



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

Code in `src/OrderSync` is organized **by type**, not by feature.
The namespace always matches the folder (`OrderSync.<Folder>`), one type per file.

| Folder | Namespace | Contains |
|---|---|---|
| `Clients/` | `OrderSync.Clients` | HTTP clients for external APIs, plus their interfaces (`IMoneybirdClient`, `MoneybirdClient`, `MockShopClient`) |
| `Services/` | `OrderSync.Services` | Business logic (`OrderSyncService`, `OrderInvoicingService`) |
| `Workers/` | `OrderSync.Workers` | `BackgroundService` implementations (`OrderSyncWorker`) |
| `Models/` | `OrderSync.Models` | Entities, enums, DTOs and result records (`SyncedOrder`, `SyncStatus`, `MockOrder`, `MoneybirdInvoice`, ...) |
| `Options/` | `OrderSync.Options` | Configuration classes bound from appsettings/user-secrets (`MoneybirdOptions`) |
| `Exceptions/` | `OrderSync.Exceptions` | Custom exceptions (`MoneybirdException`) |
| `Data/` | `OrderSync.Data` | `OrderSyncDbContext` and `Migrations/` only (entities go in `Models/`) |
| `MockShop/` | `OrderSync.MockShop` | The fake webshop itself: minimal API endpoints and seed data |
| `Components/` | `OrderSync.Components` | Blazor layout and pages |

- New code goes in the folder for its type; do not create feature folders.
- Tests in `tests/OrderSync.Tests` mirror this structure
  (e.g. `Services/OrderInvoicingServiceTests.cs`, namespace `OrderSync.Tests.Services`).
- Inside `OrderSync.*` namespaces, `Options` refers to the `OrderSync.Options` namespace.
  Use `Microsoft.Extensions.Options.Options.Create(...)` fully qualified when needed.
- When an entity moves namespace, update the type name in the EF migration snapshot and
  check with `dotnet ef migrations has-pending-model-changes` that no migration is needed.

