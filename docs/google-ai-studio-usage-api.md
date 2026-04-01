# Google AI Studio Internal Usage & Spend APIs

Reference document for the undocumented RPC endpoints and DOM scraping approach used by the Google AI Studio integration.

## How It Was Discovered

Navigated to `aistudio.google.com/usage` and `aistudio.google.com/spend` using the `claude-in-chrome` browser automation tool, then used `read_network_requests` to capture all XHR/Fetch calls on page load. The key RPC endpoints were identified by filtering for `alkalimakersuite` and `rpc` URL patterns.

Response bodies were captured by installing XHR interceptors and switching the Time Range filter to trigger re-fetches.

---

## RPC Endpoints (Reference Only — Not Directly Callable)

### `FetchMetricTimeSeries` (Usage Page)

**URL:** `https://alkalimakersuite-pa.clients6.google.com/$rpc/google.internal.alkali.applications.makersuite.v1.MakerSuiteService/FetchMetricTimeSeries`

**Method:** POST

**Auth:** Google session cookies via proprietary transport (not standard fetch/XHR — CORS-restricted from injected JS).

**Request body (JSON array):**
```json
[null, null, null, null, 2, null, 1, "projects/{project_number}", null, [time_range_a, time_range_b], [metric_ids]]
```

**Metric IDs (discovered via response analysis):**

| Metric ID | Data Returned | Description |
|-----------|---------------|-------------|
| 1 | Integer counts by model, per day | **Request count** per model |
| 2 | Large integer counts by model, per day | **Input token count** per model |
| 3 | Integer counts aggregated, per day | **Total requests** (no model breakdown) |
| 4 | Integer counts by HTTP status, per day | **Error count** by status code (404, 429, 503) |
| 5 | null (empty for test account) | Unknown — possibly output tokens |
| 6 | null (empty for test account) | Unknown — possibly cached input tokens |
| 8 | null (empty for test account) | Unknown |
| 9 | null (empty for test account) | Unknown — possibly cache read tokens |
| 11 | Float values (0–1), per day | **Success rate** |

**Response body (JSON array):**
```json
[
  [
    [
      [
        [["epoch_seconds"], ["value"]],
        [["epoch_seconds"], ["value"]]
      ],
      "model-name"
    ],
    [
      [
        [["epoch_seconds"], ["value"]]
      ],
      "model-name-2"
    ]
  ],
  ["start_epoch"],
  ["end_epoch"],
  ["interval_seconds"]
]
```

**Example response (metric 1 — request count, 7-day range):**
```json
[
  [
    [[["1774944000"],["1"]],"gemini-2.0-flash"],
    [[["1774512000"],["72"]],["1774598400"],["772"]],["1774684800"],["449"]],["1774771200"],["386"]],["1774857600"],["657"]],["1774944000"],["591"]],["1775030400"],["228"]]],"gemini-2.5-flash-lite"],
    [[["1774512000"],["881"]]],"gemini-3.1-flash-lite"]
  ],
  ["1774512000"],
  ["1775116800"],
  ["86400"]
]
```

### `GetSdui` (Spend Page)

**URL:** `https://cloudconsole-pa.clients6.google.com/$rpc/google.internal.cloud.clientapi.sdui.BillingSduiService/GetSdui`

**Method:** POST

**Auth:** Google session cookies via proprietary transport.

SDUI = "Server-Driven UI" — returns complete page data including cost calculations. Response format is opaque (not decoded).

### `GetProjectUsageLimit` (Spend Page)

**URL:** `https://alkalimakersuite-pa.clients6.google.com/$rpc/google.internal.alkali.applications.makersuite.v1.MakerSuiteService/GetProjectUsageLimit`

**Method:** POST

Returns the monthly spend cap configuration.

### Other Endpoints (Page Load)

| Endpoint | Purpose |
|----------|---------|
| `GenerateAccessToken` | Auth token refresh (called twice on load) |
| `ListModels` | Available models for the account |
| `ListCloudProjects` | User's cloud projects |
| `GetUserPreferences` | User settings |
| `GetLoggingContext` | Logging configuration |
| `ListPromos` | Active promotions |
| `ListImportedProjects` | Imported project list |
| `ListCodeAssistantConfigurations` | Code assistant settings |

---

## Why Direct RPC Calls Don't Work

Google's internal RPC transport is **not standard `fetch()` or `XMLHttpRequest`**. Attempts to call both `alkalimakersuite-pa.clients6.google.com` and `cloudconsole-pa.clients6.google.com` from injected JavaScript on `aistudio.google.com` result in "Failed to fetch" (CORS). Google uses a proprietary channel mechanism (likely iframe-based or batched script transport) that:

1. Handles auth via SAPISIDHASH headers
2. Bypasses standard CORS restrictions through origin-trusted channels
3. Cannot be replicated from injected `fetch()` or `XMLHttpRequest` calls

---

## DOM Scraping Approach (Recommended for MAUI WebView)

Since direct RPC calls are not possible, the recommended approach is DOM scraping via the page's accessibility tables.

### Usage Page (`aistudio.google.com/usage`)

**URL Pattern:** `https://aistudio.google.com/usage?timeRange={range}&project={project_id}`

**Time Range values:** `last-hour`, `last-1-day`, `last-7-days`, `last-28-days`, `last-90-days`, `this-month`

**Page Structure (5 sections, 9 chart tables):**

| Table Index | Section | Chart Name | Data |
|-------------|---------|------------|------|
| T0 | Overview | Total API Requests (bar) | Aggregated request counts per day |
| T1 | Overview | Success Rate (line) | Success rate % per day |
| T2 | Overview | Total API Errors (bar) | Error counts by HTTP status per day |
| T3 | Generate content & Live API | Input Tokens per model | Token counts per model per day |
| T4 | Generate content & Live API | Requests per model | Request counts per model per day |
| T5 | Generate media | Imagen Requests per model | (empty unless using Imagen) |
| T6 | Generate media | Veo Requests per model | (empty unless using Veo) |
| T7 | Embed content | Embedding Tokens per model | (empty unless using embeddings) |
| T8 | Embed content | Embedding Requests per model | (empty unless using embeddings) |

**Filters:**
- Project (combobox) — selects the GCP project
- Time Range (combobox) — sets the date range
- Model (combobox, multi-select checkboxes) — filters T3/T4 by model. **Note:** The Model filter only affects the "Generate content & Live API" section (T3, T4). The Overview section (T0–T2) always shows aggregated data.

**Extraction Steps (JavaScript to inject in WebView):**

```javascript
// 1. Click all "Populate data" buttons to fill accessibility tables
const buttons = [...document.querySelectorAll('button')]
  .filter(b => b.textContent.includes('Populate data'));
buttons.forEach(b => b.click());

// 2. Wait 500ms for tables to populate

// 3. Read table data
const tables = document.querySelectorAll('table');
tables.forEach((table, i) => {
  const rows = [...table.querySelectorAll('tr')];
  if (rows.length < 2) return; // Skip empty tables
  
  // Column headers = dates (skip first 2 cells: label + audio)
  const headers = [...rows[0].querySelectorAll('td, th')];
  const dates = headers.slice(2).map(c => c.textContent.trim());
  
  // Data rows: first cell = series label, remaining cells = values
  rows.slice(1).forEach(row => {
    const cells = [...row.querySelectorAll('td, th')];
    const label = cells[0]?.textContent.trim().split(' Play')[0];
    const values = cells.slice(2).map(c => c.textContent.trim());
    // label = model name or metric name
    // values = array of data points matching dates
  });
});
```

**Table cell format notes:**
- Request counts: plain integers ("772", "449")
- Token counts: may use suffixes ("1.836M", "597K") — needs parsing
- Success rate: percentages ("97.807%")
- Dates: "Mar 27, 12:00 AM" format (12:00 AM = midnight UTC-8)

### Spend Page (`aistudio.google.com/spend`)

**URL Pattern:** `https://aistudio.google.com/spend?project={project_id}`

**Time Range values (Spend page):** `last-7-days`, `last-28-days`, `last-90-days`, `this-month`

**Page Structure:**

1. **Monthly spend cap section:**
   - Current spend / cap limit (e.g., "£0.03 / £4.00")
   - Parseable from page text

2. **Cost summary section:**
   - Date range (e.g., "March 5 - April 1, 2026")
   - Cost, Savings, Total cost values
   - Parseable from page text: `Cost £3.61 - Savings £3.61 = Total cost £0.00`

3. **Cost chart (1 table):**
   - T0: Total cost per day (bar chart)
   - Same "Populate data" button + table extraction pattern

**Extraction (summary values from DOM text):**
```javascript
const text = document.body.innerText;
const cost = text.match(/Cost\s+([\£\$\d.]+)/)?.[1];
const savings = text.match(/Savings\s+([\£\$\d.]+)/)?.[1];
const totalCost = text.match(/Total cost\s+([\£\$\d.]+)/)?.[1];
const dateRange = text.match(/Your total cost \(([^)]+)\)/)?.[1];
```

**Filters:**
- Time Range (combobox)
- Model (combobox) — filters cost by model

---

## Auth Mechanism

Both pages use Google account session cookies. No API key or OAuth token is needed beyond a valid logged-in `aistudio.google.com` session.

In the MAUI app, this is handled by the WebView control which navigates to `aistudio.google.com`. If the user is logged in, cookies persist across navigations. If not, they see the Google login flow.

**Important:** Google's auth includes 2FA and advanced bot detection. The WebView must:
1. Use a standard user agent
2. Allow the full Google login flow (including 2FA prompts)
3. Not trigger automated detection (avoid rapid page loads)

---

## MAUI Integration Plan

Following the same pattern as `ClaudeProWebViewPage`:

1. User taps "Connect Google AI Studio" on the Setup page
2. A modal WebView opens, navigating to `https://aistudio.google.com/usage`
3. If already logged in, the page loads directly; if not, user sees Google login flow
4. After the page loads (detect via `Navigated` event + wait for SPA render):
   a. Set time range to desired value via URL parameter
   b. Inject JS to click "Populate data" buttons
   c. Extract table data
   d. Navigate to `/spend` page
   e. Extract cost data
5. Parse and return structured data to the app

**Silent refresh** follows the same invisible WebView pattern used by `ClaudeProUsageProvider`.

---

## Available Models (Observed)

- `gemini-3.1-flash-lite`
- `gemini-2.5-flash-lite`
- `gemini-2.0-flash`

The model list is dynamic — `ListModels` RPC returns the available models. The Model filter dropdown shows only models with actual usage.

---

## Fragility Warning

This is based on **undocumented internal UI and RPC endpoints**. Google may change or remove any of this at any time. Specifically:

- The DOM table structure (table indices, cell layout, "Populate data" buttons) may change
- The URL parameter format (`timeRange=last-28-days`) may change
- The RPC endpoints and request/response formats may change
- The page layout and sections may be reorganised
- Google's auth flow may add additional challenges

The DOM scraping approach is more resilient than RPC replication since it relies on accessibility features (which tend to be more stable), but should still be tested regularly.

---

## How to Re-Discover If the Page Changes

1. Navigate to `aistudio.google.com/usage` in Chrome
2. Use `mcp__claude-in-chrome__read_network_requests` to capture RPC calls
3. Use `mcp__claude-in-chrome__read_page` (interactive filter) to find buttons and comboboxes
4. Click "Populate data" buttons and read table contents
5. Update table indices and extraction selectors as needed
