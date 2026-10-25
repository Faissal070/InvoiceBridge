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

