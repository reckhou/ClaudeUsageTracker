# Claude.ai Internal Usage API

Reference document for the undocumented quota API used by the Claude Pro integration.

## How It Was Discovered

Navigated to `claude.ai/settings/usage` using the `claude-in-chrome` browser automation tool, then used `mcp__claude-in-chrome__read_network_requests` with `urlPattern: "claude.ai"` to capture all XHR/Fetch calls firing on page load. Two endpoints returned JSON with `utilization` fields:

1. `GET /api/organizations` — returns the list of orgs the authenticated user belongs to, including their UUIDs
2. `GET /api/organizations/{uuid}/usage` — returns quota utilisation percentages and reset times

After identifying the endpoints, JavaScript was injected into the browser context to call them directly using `fetch()` with `credentials: 'include'`, which forwards the browser's session cookies. The full JSON response was captured and the field names documented below.

---

## Endpoints

### `GET /api/organizations`

Returns an array of the authenticated user's organizations.

**Auth:** Session cookies (`credentials: 'include'`) — no API key required.

**Response shape (relevant fields):**
```json
[
  {
    "uuid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "name": "Personal",
    ...
  }
]
```

Use `orgs[0].uuid` as the org identifier for the usage endpoint.

---

### `GET /api/organizations/{uuid}/usage`

Returns quota utilisation for the authenticated user's account.

**Auth:** Session cookies (`credentials: 'include'`) — no API key required.

**Full response shape:**
```json
{
  "five_hour": {
    "utilization": 42,
    "resets_at": "2026-03-28T15:00:00Z"
  },
  "seven_day": {
    "utilization": 17,
    "resets_at": "2026-04-03T00:00:00Z"
  },
  "extra_usage": {
    "is_enabled": false,
    "utilization": 0
  },
  "seven_day_oauth_apps": null,
  "seven_day_opus": null,
  "seven_day_sonnet": null,
  "seven_day_cowork": null,
  "iguana_necktie": null
}
```

**Field descriptions:**

| Field | Type | Description |
|-------|------|-------------|
| `five_hour.utilization` | int (0–100) | Current session usage as a percentage of the 5-hour rolling window limit |
| `five_hour.resets_at` | ISO 8601 UTC | When the current 5-hour window expires and usage resets |
| `seven_day.utilization` | int (0–100) | Weekly usage as a percentage of the 7-day rolling window limit |
| `seven_day.resets_at` | ISO 8601 UTC | When the 7-day window expires and usage resets |
| `extra_usage.is_enabled` | bool | Whether the user has purchased additional quota beyond the plan limit |
| `extra_usage.utilization` | int (0–100) | Usage percentage of the extra quota (0 when not enabled) |
| `seven_day_oauth_apps` | null | Always null on personal Pro plan — purpose unknown |
| `seven_day_opus` | null | Always null on personal Pro plan — may indicate per-model quota in future |
| `seven_day_sonnet` | null | Always null on personal Pro plan |
| `seven_day_cowork` | null | Always null on personal Pro plan — possibly team/co-working feature |
| `iguana_necktie` | null | Always null — internal field name, purpose unknown |

---

## Auth Mechanism

Both endpoints use the browser's existing session cookies. No API key or token is needed beyond a valid logged-in claude.ai session. JavaScript must pass `credentials: 'include'` with `fetch()`:

```javascript
const resp = await fetch('/api/organizations', { credentials: 'include' });
```

In the MAUI app, this is handled by the `ClaudeProWebViewPage` which navigates to `https://claude.ai/settings/usage` in a WebView, then calls `EvaluateJavaScriptAsync` to run the fetch calls using the WebView's existing session cookies.

---

## MAUI Integration

The integration calls these endpoints from within a MAUI `WebView` control:

1. User taps "Connect Claude Pro" on the Setup page
2. A modal `ClaudeProWebViewPage` opens, navigating to `https://claude.ai/settings/usage`
3. If the user is already logged in, the `Navigated` event fires and the JS is injected automatically
4. If not logged in, the user sees the full claude.ai login flow; after login the page navigates to `/settings/usage` and the JS fires
5. The parsed `QuotaRecord` is returned to the caller and stored in SQLite
6. Silent refresh (`FetchQuotaAsync`) works the same way but with an invisible WebView

---

## How to Re-Discover If the Page Changes

If this integration breaks after a claude.ai update:

1. Open Chrome DevTools → Network tab → filter to `Fetch/XHR`
2. Navigate to `https://claude.ai/settings/usage` and reload
3. Look for requests returning JSON that includes `utilization` fields
4. Note the endpoint path, check if the org UUID is still required
5. Verify field names haven't changed in the response body
6. Update `ClaudeProWebViewPage.ParseUsageResponse` and `QuotaRecord` model accordingly

Alternatively, use `mcp__claude-in-chrome__read_network_requests` with `urlPattern: "claude.ai"` to capture all requests programmatically.

---

## Fragility Warning

This is an **undocumented internal API**. Anthropic may change or remove it at any time without notice. Specifically:

- Endpoint paths (`/api/organizations`, `/api/organizations/{uuid}/usage`) may change
- JSON field names may be renamed or restructured
- The auth mechanism may change from cookies to something else
- The quota model itself (5-hour session + 7-day weekly) may be replaced

The `iguana_necktie` field name in particular suggests these endpoints are not designed for external consumption and may be refactored arbitrarily.
