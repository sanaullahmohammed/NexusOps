# Quickstart: Verifying the Documentation Reconciliation

This feature has no runtime behavior — "quickstart" here means how to re-verify each corrected
claim, so a reviewer (or a future doc-drift audit) can confirm the docs still match reality without
re-deriving every check from scratch.

## Re-verify project/solution facts

```bash
grep -c "Project(" NexusOps.sln                 # total .NET+frontend projects in the solution
cat NexusOps.deployable.slnf                     # projects included in the CI-scoped filter
```

CLAUDE.md's Solution Filter section should state the `.sln` total and the `.slnf` exclusion list
(`frontend.esproj`, `NexusOps.IntegrationTests`) consistent with these two commands' output.

## Re-verify CI workflow facts

```bash
cat .github/workflows/ci.yml
```

CLAUDE.md's CI table/Solution Filter section should mention every job (`dotnet`, `integration-tests`,
`frontend`, `notification-service`), the dotnet job's compile-only `Build integration tests` step,
and `integration-tests`' `timeout-minutes: 30` and `if: github.event_name == 'push' && github.ref ==
'refs/heads/master'` gate.

## Re-verify repository structure facts

```bash
ls -d */ | grep -v node_modules
```

README.md's and CLAUDE.md's Project/Repository Structure blocks should list every top-level project
directory, including `NexusOps.IntegrationTests/`, `NexusOps.Evaluation/`, and `specs/`.

## Re-verify example queries and seed ID formats

```bash
grep -n "ORD-\|SKU-\|CUST-" NexusOps.Contracts/SeedDataConstants.cs
grep -n "public const string" NexusOps.Contracts/ToolNames.cs
```

README.md's Example Queries table should only reference capabilities that map to an entry in
`ToolNames.cs`, using ID formats that actually appear in seed data.

## Re-verify the architecture diagram

```bash
grep -n "builder.Add" NexusOps.AppHost/AppHost.cs
```

Every resource registered here (`redis`, `rabbitmq`, `postgres`, the three domain services,
`workflow-orchestrator`, `notification-service`, `agent-host`, `server`, `webfrontend`) should have a
corresponding node in README.md's Mermaid diagram.

## Re-verify the local run command

```bash
aspire --version   # environment's CLI version
grep "Aspire" NexusOps.AppHost/NexusOps.AppHost.csproj  # project's Aspire package version
```

If these still don't match, `dotnet run --project NexusOps.AppHost` (not `aspire start`) should be
what ROADMAP.md, README.md, and CLAUDE.md all document as the way to run the app locally.

## Re-verify the flagged constitution tensions

```bash
grep -n "NotificationRequested" NexusOps.WorkflowOrchestrator/OrderAction/*.cs
git log --all --format="%D" | grep -o 'chore/[a-zA-Z0-9._-]*' | sort -u
grep -n "WithHttpHealthCheck\|webfrontend" NexusOps.AppHost/AppHost.cs
```

None of these should change as a result of this feature. The flagged-tensions note should describe
what these commands show, not resolve it.

## Confirm no build/test regression

```bash
dotnet test NexusOps.deployable.slnf --configuration Release
```

This should still be green — this feature changes no `.cs`, `.ts`, `.yml`, or config file, only
Markdown.
