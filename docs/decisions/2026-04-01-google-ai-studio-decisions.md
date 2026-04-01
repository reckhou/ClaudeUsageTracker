# Google AI Studio Usage Tracking — Decision Log

## Brainstorming Phase — 2026-04-01

### Decision: WebView DOM scraping over direct RPC calls
- **Chosen:** DOM scraping via injected JS on `aistudio.google.com/usage` and `/spend`
- **Alternatives considered:** Direct RPC calls to `alkalimakersuite-pa.clients6.google.com`
- **Rationale:** Google's internal RPC transport uses proprietary channel mechanisms (iframe-based, SAPISIDHASH auth) that bypass standard CORS. Injected `fetch()` calls return "Failed to fetch". DOM scraping via accessibility tables is the only viable path.
- **Trade-offs accepted:** Fragile — DOM structure may change. Two-page navigation needed (usage + spend). Slower than a direct API call.

### Decision: "Cost" field over "Total cost" from Spend page
- **Chosen:** Use pre-credit "Cost" value
- **Alternatives considered:** "Total cost" field
- **Rationale:** "Savings" always equals "Cost" (Google credits offset 100%), making "Total cost" perpetually £0.00. The "Cost" field shows actual resource consumption.
- **Trade-offs accepted:** May show inflated costs for accounts without full credit coverage.

### Decision: Spend cap delta tracking for daily cost
- **Chosen:** Track spend cap readings over time, compute daily cost as deltas
- **Alternatives considered:** Cost chart table extraction (T0 on spend page)
- **Rationale:** Cost chart showed all zeroes in testing (savings offset cost). Spend cap value (`£X.XX / £Y.YY`) reflects actual accumulated cost and is reliably extractable via text regex.
- **Trade-offs accepted:** Cost chart would have given per-day granularity; delta approach gives only the change between readings.

## Planning Phase — 2026-04-01

### Decision: Separate data model from ProviderUsageRecord
- **Chosen:** New `GoogleAiUsageRecord` with per-model fields (requests, tokens, cost)
- **Alternatives considered:** (a) Flatten into `ProviderUsageRecord` (one card per model), (b) extend `ProviderUsageRecord` with optional model fields
- **Rationale:** Google AI tracks API usage metrics (per-model requests, tokens, cost over time), not plan quotas (used X of Y). The data shape is fundamentally different. A separate model avoids polluting the quota model and enables a purpose-built UI card.
- **Trade-offs accepted:** More code — new model, new VM, new card template. Can't reuse existing provider card UI.

### Decision: Separate auto-refresh timer (30 min hardcoded)
- **Chosen:** Dedicated 30-min timer for Google AI, independent of plan-provider auto-refresh
- **Alternatives considered:** (a) Use the same configurable timer, (b) make Google AI interval configurable too
- **Rationale:** Google AI scraping is slow (WebView navigation to two pages per project), hitting Google too frequently risks auth challenges. 30 min is a sensible balance for API usage data that doesn't change rapidly. Plan-provider refresh (5-min default) is appropriate for quota tracking but too aggressive for scraping.
- **Trade-offs accepted:** Less user control. If 30 min proves wrong, need a code change to adjust.

### Decision: Singleton GoogleAiCardViewModel outside Providers collection
- **Chosen:** Google AI card is a static property on `ProvidersDashboardViewModel`, rendered in its own XAML section
- **Alternatives considered:** Adding it to the `Providers` ObservableCollection as a `ProviderCardViewModel`
- **Rationale:** The card's UI is completely different (no progress bars, has per-model table, has project dropdown). Forcing it into the same template would require heavy conditional visibility toggling. A separate card section is cleaner.
- **Trade-offs accepted:** Mini mode needs explicit handling for the Google AI card (can't just filter `MiniProviders`).

### Decision: Multi-project support via comma-separated SecureStorage
- **Chosen:** Store project IDs as comma-separated string in SecureStorage, manage via ObservableCollection in SetupViewModel
- **Alternatives considered:** SQLite table for project configuration
- **Rationale:** Project list is small (1-5 items typically), rarely changes, and follows the same storage pattern as other provider credentials. SQLite would be overkill.
- **Trade-offs accepted:** No metadata per project (just the ID string). Comma in project IDs would break parsing (unlikely for GCP project IDs).
