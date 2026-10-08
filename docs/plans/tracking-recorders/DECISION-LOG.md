# Decision log

- 08-10-2026: Recorders are internal, not Umbraco notifications. Notifications are a public
  contract, and the test-usage recorder needs to run in the caller's own flow to see the
  ambient collector. Public hook points can come later if a need shows up.
- 08-10-2026: Built after the Copilot release, since every AI call goes through this code.
  Copilot shipped stable (18.1.1 / 17.1.1) on 08-10-2026, so the hold is lifted.
- 08-10-2026: The audit parent bug (#529) and the dashboard gap (#530) were fixed on their own
  first, both lines (#532/#533, #534/#535). #531 (usage lost on cancel) is left for T1, where the
  analytics recorder's end write moves to `CancellationToken.None`.
- 08-10-2026: Test coverage of today's behaviour is already strong, so "pin behaviour first" is
  the first commit of T1 rather than its own PR. Gaps listed in `PLAN.md`.
- 08-10-2026: Five design calls need confirming before T1 starts. See "Decisions to confirm" in
  `ARCHITECTURE.md`.
- 08-10-2026: All five confirmed by the maintainer: (1) recorder failures are isolated, including
  the audit factory's missing-profile throw; (2) every end write uses `CancellationToken.None`;
  (3) fixed order audit, trace, analytics, test usage; (4) plain internal ordered DI list, not a
  collection builder; (5) `Blocked` becomes a real outcome, counted as a failure on the dashboard.
