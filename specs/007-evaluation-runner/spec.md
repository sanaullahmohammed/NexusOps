# Feature Specification: Evaluation Runner

**Feature Branch**: `007-evaluation-runner`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "Add NexusOps.Evaluation: a dependency-light .NET console project that evaluates the AgentHost's tool-routing accuracy against a curated JSON dataset of 20-30 realistic user prompts. Each dataset case is labeled with an expected tool selection (one of the 9 curated tools already defined in NexusOps.Contracts.ToolNames) and an expected path (Direct or Saga). The runner has two modes: (1) --validate-only, which is fully credential-free and offline — it validates the dataset's schema, that every case ID is unique, that every expectedTool is a recognized/supported tool name, and that every expectedPath is a supported value (and that it's consistent with the tool's actual path) — this is the mode CI runs on every push/PR, so it must never require network access, RabbitMQ, Postgres, Redis, or Azure AI credentials. (2) Live mode (the default when --validate-only is not passed), which sends each dataset prompt to a running AgentHost's POST /api/chat endpoint, captures which tool(s) the agent actually invoked for that turn, compares it to the expected tool, and prints a per-case pass/fail plus a summary table (counts and pass rate). Live mode must not be able to break CI: when AgentHost is not reachable, it must exit in a distinct 'skipped' state with clear setup guidance printed to the console — never as a failure. When AgentHost is reachable and credentials are configured, local runs are expected to actually execute the live evaluation and report real pass/fail results. No third-party evaluation/testing framework. Update README.md's existing Evaluation section to document both commands and their credential requirements."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - CI validates the evaluation dataset on every push (Priority: P1)

The project's automated build pipeline checks, on every push and pull request, that the evaluation dataset is well-formed — without needing any credentials, external services, or a running instance of the agent. If someone edits the dataset and introduces a mistake (a duplicate case identifier, a tool name that doesn't exist, an unsupported path label), the pipeline fails immediately and names the mistake, before it ever reaches a reviewer or a live run.

**Why this priority**: This is the gate every other capability depends on. It is also the only piece of this feature that must run unattended, credential-free, in an environment (CI) the project does not control. Without it, a broken dataset could sit undetected until someone happens to run a live evaluation locally.

**Independent Test**: Can be fully tested by running the dataset-validation command against both a known-good dataset (succeeds) and a deliberately corrupted copy (fails, naming the specific defect) — no running services of any kind required.

**Acceptance Scenarios**:

1. **Given** the checked-in evaluation dataset, **When** dataset validation is run with no network access and no credentials configured, **Then** it completes successfully and reports how many cases were validated.
2. **Given** a dataset copy with two cases sharing the same case identifier, **When** dataset validation is run, **Then** it fails and names both the duplicate identifier and the offending cases.
3. **Given** a dataset copy where one case's expected tool is not one of the project's recognized tools, **When** dataset validation is run, **Then** it fails and names the case and the unrecognized tool.
4. **Given** a dataset copy where one case's expected path does not match the actual path of its expected tool, **When** dataset validation is run, **Then** it fails and names the mismatch.

---

### User Story 2 - A developer measures live tool-routing accuracy (Priority: P2)

A developer who has the full application running locally with working AI credentials wants to know, after changing agent instructions, tool descriptions, or adding a new tool, whether the agent still routes realistic user prompts to the right tool. They run the evaluation in live mode and get a per-case pass/fail readout plus a summary (how many passed, how many failed, overall pass rate) they can act on immediately.

**Why this priority**: This is the feature's actual value proposition — a repeatable, objective check on the thing that is hardest to verify by hand (natural-language routing behavior) and easiest to silently regress when agent instructions or tool descriptions change. It depends on User Story 1's dataset already being trustworthy.

**Independent Test**: Can be fully tested by starting the application locally with valid credentials, running the live evaluation command, and confirming the console shows a result (pass or fail, with the tool the agent actually chose) for every case in the dataset plus an accurate summary count.

**Acceptance Scenarios**:

1. **Given** the application is running locally with valid AI credentials, **When** live evaluation is run, **Then** every dataset case is sent to the agent, each result records which tool (if any) the agent invoked, and the console reports pass/fail per case plus a summary table with total, passed, failed, and pass rate.
2. **Given** a case whose prompt causes the agent to invoke a different tool than the one the case expects, **When** live evaluation is run, **Then** that case is reported as failed and the report shows both the expected and the actually-invoked tool.
3. **Given** a case whose prompt causes the agent to invoke exactly the expected tool, **When** live evaluation is run, **Then** that case is reported as passed.

---

### User Story 3 - Live evaluation never breaks an unattended or credential-free run (Priority: P1)

Anyone who runs live evaluation in an environment without a reachable agent or without AI credentials configured — including, but not limited to, the CI pipeline if it were ever invoked without the validate-only flag — gets a clear, distinct "skipped, here's what to set up" outcome rather than a build-breaking failure or a confusing crash.

**Why this priority**: The single most important safety property this feature has: a credential-dependent capability must be structurally incapable of failing an unattended pipeline. This is as load-bearing as User Story 1 and is what makes User Story 2 safe to add at all.

**Independent Test**: Can be fully tested by running live evaluation with no application running (nothing listening on the expected address) and confirming the run reports "skipped" with actionable setup guidance, exits in a way that is distinguishable from both "all cases passed" and "some cases failed," and never raises an unhandled error.

**Acceptance Scenarios**:

1. **Given** no instance of the agent is reachable, **When** live evaluation is run, **Then** the run reports a skipped outcome, prints the steps needed to enable a live run, and this outcome is never reported as a failure.
2. **Given** the agent is reachable but a specific case's request fails or times out, **When** live evaluation is run, **Then** that one case is reported as failed with the reason, the run continues through the remaining cases, and the run as a whole still completes and reports a summary.

---

### Edge Cases

- A dataset case's prompt causes the agent to invoke more than one tool in the same turn (e.g., a compound question): the case passes if the expected tool is among those invoked, and the report shows every tool that was invoked so a reviewer can judge whether the extra call was reasonable.
- A dataset case's prompt causes the agent to invoke no tool at all (a conversational, non-actionable reply): the case is reported as failed, showing "no tool invoked" against the expected tool.
- The agent is reachable when the run starts but becomes unreachable partway through (service restarts mid-run): the affected case(s) are reported as failed with the connection error, not silently dropped, and the run still finishes and reports a summary.
- The dataset file is missing entirely, or is not valid JSON: dataset validation fails with a message identifying the problem, distinct from a schema violation inside an otherwise-valid file.
- Two cases legitimately test the same tool with different phrasings: this is expected and encouraged, not a validation error — only the case *identifier* must be unique, not the expected tool.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The evaluation dataset MUST be a checked-in, human-readable file containing between 20 and 30 cases covering realistic user prompts.
- **FR-002**: Each dataset case MUST carry, at minimum: a unique case identifier, the user prompt text, the tool the agent is expected to invoke, and the path (Direct or Saga) that tool belongs to.
- **FR-003**: Each of the project's currently curated tools MUST be represented by at least one dataset case.
- **FR-004**: The dataset MUST include cases for both the Direct path and the Saga path, including at least one case per mutating (approval-gated) tool.
- **FR-005**: The system MUST provide a dataset-validation capability that requires no network access, no message broker, no database, and no AI credentials of any kind.
- **FR-006**: Dataset validation MUST verify that every case identifier is unique within the dataset.
- **FR-007**: Dataset validation MUST verify that every case's expected tool is one of the project's currently recognized, curated tools.
- **FR-008**: Dataset validation MUST verify that every case's expected path is one of the two supported values (Direct or Saga), and that it matches the actual path of that case's expected tool.
- **FR-009**: Dataset validation MUST verify that every case has non-empty values for all required fields (FR-002).
- **FR-010**: Dataset validation MUST report every defect it finds in a single run (not stop at the first one), each naming the offending case identifier and the specific problem.
- **FR-011**: Dataset validation MUST signal overall success or failure in a way an automated pipeline can act on (i.e., distinguishable pass/fail outcomes), and on success MUST report how many cases were validated.
- **FR-012**: The system MUST provide a live-evaluation capability that sends each dataset case's prompt to a running instance of the agent and records which tool, if any, the agent invoked while handling it.
- **FR-013**: Live evaluation MUST treat a case as passed only when the expected tool is among the tool(s) the agent actually invoked for that prompt, and failed otherwise (including when no tool was invoked).
- **FR-014**: Live evaluation MUST report, for every case, at minimum: the case identifier, pass/fail, the expected tool, and the tool(s) actually invoked (or that none were).
- **FR-015**: Live evaluation MUST report a summary after all cases complete: total cases run, number passed, number failed, and the pass rate.
- **FR-016**: Live evaluation MUST detect, before sending any dataset prompts, whether a live agent is reachable, and MUST NOT require any dataset prompt to be sent in order to make that determination.
- **FR-017**: When no live agent is reachable, live evaluation MUST stop without sending dataset prompts, MUST report a skipped outcome distinct from both "all passed" and "some failed," MUST print actionable setup guidance, and MUST NOT signal this outcome as a failure to an automated pipeline.
- **FR-018**: When the live agent is reachable but an individual case's request fails or times out, live evaluation MUST record that case as failed with the reason and continue evaluating the remaining cases rather than aborting the run.
- **FR-019**: The two mutating (approval-gated) dataset cases MUST be satisfiable by the agent taking an action that requires no further human step to be safe to run repeatedly — i.e., running live evaluation repeatedly against the same environment MUST NOT itself execute any real-world side effect (a refund actually issued, an order actually cancelled) without a separate, explicit approval step outside the evaluation run.
- **FR-020**: Dataset-validation and live-evaluation MUST each be invocable as a distinct, explicitly named mode of the same tool, with dataset-validation selectable independently of whether a live agent happens to be reachable.
- **FR-021**: The project's automated build pipeline MUST run dataset validation on every push and pull request.
- **FR-022**: The project's published setup documentation MUST describe both the dataset-validation and the live-evaluation commands, and MUST state which credentials or running components each one requires.

### Key Entities

- **Evaluation Case**: A single labeled test scenario — a realistic user prompt, the tool the project's agent is expected to select for it, and which of the two request paths (Direct or Saga) that tool belongs to. Identified uniquely within the dataset.
- **Evaluation Dataset**: The complete, checked-in collection of Evaluation Cases used both for offline validation and for live evaluation runs.
- **Evaluation Run Result**: The outcome of evaluating one Evaluation Case in a live run — which tool(s), if any, the agent actually invoked, and whether that satisfied the case's expectation.
- **Evaluation Run Summary**: The aggregate outcome of a full live-evaluation run — total cases, passed, failed, pass rate — or, if the agent could not be reached at all, a skipped outcome with guidance in place of results.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Dataset validation completes in under 5 seconds with no network access and no credentials configured, on the checked-in dataset.
- **SC-002**: Every one of the project's currently curated tools is exercised by at least one dataset case, and both the Direct and Saga paths are represented.
- **SC-003**: A developer with a working local environment can obtain a full live pass/fail report, including a summary pass rate, for the entire dataset in a single command.
- **SC-004**: Running live evaluation with no agent reachable never produces a pipeline failure and always produces console guidance sufficient for a developer unfamiliar with this feature to know their next step.
- **SC-005**: A deliberately introduced dataset defect (duplicate identifier, unrecognized tool, mismatched path) is caught by dataset validation 100% of the time, before any live run is attempted.
- **SC-006**: The automated build pipeline's evaluation-dataset check has a 0% false-failure rate attributable to missing credentials or unreachable services, across ordinary pushes and pull requests.

## Assumptions

- "The agent" refers to this project's existing AI agent host and its established tool-routing behavior; this feature evaluates that existing behavior and does not add new tools or change routing logic itself.
- The set of "currently curated tools" and their Direct/Saga path classification are defined elsewhere in the project and are read, not redefined, by this feature; dataset validation checks dataset cases against that existing, authoritative set.
- "Reachable" for the live agent means it responds to a basic connectivity check; a developer wanting a live run is expected to have already started the full local environment (including AI credentials) as documented elsewhere in the project's setup instructions.
- A single conversation turn per dataset case is sufficient to evaluate tool-routing accuracy; multi-turn conversational scenarios are out of scope for this feature.
- Judging response *quality* (correctness of the agent's final natural-language answer, not just which tool it picked) is out of scope for this feature; scope is limited to tool-selection and path accuracy.
- The dataset is maintained by project contributors directly editing the checked-in file; no authoring UI is in scope.
