# Quickstart: Evaluation Runner

## Credential-free dataset validation (what CI runs)

No running services, no credentials, no network access required.

```bash
dotnet run --project NexusOps.Evaluation -- --validate-only
```

Expected output on the checked-in dataset: a success message naming how many cases were validated (24), and exit code `0`. Corrupt a case (duplicate `id`, an unrecognized `expectedTool`, a mismatched `expectedPath`) and re-run to see it fail with the specific defect named, exit code `1`.

## Live evaluation (requires a running AgentHost with Azure AI credentials)

1. Ensure Azure AI credentials are configured (see README's "Configure Azure AI Foundry credentials" section) and start the application:

   ```bash
   dotnet run --project NexusOps.AppHost
   ```

2. From the Aspire dashboard, find `agent-host`'s external HTTP endpoint (its port is assigned dynamically under Aspire). If instead you run AgentHost directly (`dotnet run --project NexusOps.AgentHost`), it is `http://localhost:5186` by default — no `--base-url` needed.

3. Run live evaluation:

   ```bash
   dotnet run --project NexusOps.Evaluation -- --base-url http://localhost:5186
   ```

   (or set `AGENTHOST_BASE_URL` instead of passing `--base-url`.)

Expected output: a pass/fail line per case (showing the expected tool and what the agent actually invoked), then a summary table (total / passed / failed / pass rate), exit code `0` if every case passed, `1` if any case failed.

## Without a reachable AgentHost (the safety path)

```bash
dotnet run --project NexusOps.Evaluation
```

With nothing listening on the default/target address, this prints a `SKIPPED` banner with the exact steps above and exits `0` — never a failure. This is what makes it safe for this to be the *default* mode: running it by accident in an environment with no AgentHost (including, hypothetically, a misconfigured CI step) cannot break a build.
