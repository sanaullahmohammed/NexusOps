# Phase 1 Data Model: Constitution Reconciliation

No new or modified entities. This feature:

- Changes no persisted schema (`OrderActionSagaState`, `OrderActionDbContext`, and every other EF Core model are untouched — Decision 1 in research.md found the existing notification behavior already compliant, so no new approval/notification state is introduced).
- Adds no new message contract (`NotificationRequested`, `ApproveOrderAction`, `RejectOrderAction`, etc. are all untouched).
- Changes one line of infrastructure configuration (`webfrontend`'s health check registration in `AppHost.cs`) — introduces no data entity. `conventional-branch` is explicitly not touched (2026-09-04 revision — see spec.md's Clarifications).

This file exists to record that the "extract entities from feature spec" step of Phase 1 was performed and found nothing applicable, not to document a data model that doesn't exist for this feature.
