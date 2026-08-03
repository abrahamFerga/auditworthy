# Auditworthy.IntegrationTests

Deliberately empty. The Testcontainers fixture (pgvector Postgres +
`WebApplicationFactory<Program>`), the committed `.http` request catalog and the golden-conversation
eval harness are installed by **`/deliver:install-runbook`**, which owns that contract.

Hand-rolling them here would create a second source of truth for how this product is run and
proved, and the two would drift. Run the runbook install before the first feature issue is worked.

Until then the honest statement is: **this product has no runtime proof yet.** `dotnet build`
proves compilation, not behaviour.
