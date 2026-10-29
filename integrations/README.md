# No-code integrations

The API's transactional outbox POSTs every `order.paid` event as JSON to `Outbox__WebhookUrl`.
Any tool that accepts an incoming webhook works: Make.com, Zapier, n8n, Pipedream, your own service.

## Payload

```json
{
  "id": "7b0c6f7e-3c55-4a5b-9a6e-0d3b8f1f2a10",
  "type": "order.paid",
  "createdAt": "2026-10-30T09:12:44.123+00:00",
  "data": {
    "orderId": "3f1d…",
    "productSku": "api-audit",
    "productName": "API Security Audit",
    "amount": 4410,
    "currency": "usd",
    "customerEmail": "buyer@example.com",
    "promotionCode": "WELCOME10",
    "paidAt": "2026-10-30T09:12:44.101+00:00",
    "connectedAccountId": null,
    "applicationFeeAmount": null
  }
}
```

Amounts are in minor units (cents). Headers sent with every request:

| Header | Meaning |
|---|---|
| `X-Event-Id` | Same as `id`. Delivery is at-least-once, so de-duplicate on this. |
| `X-Event-Type` | `order.paid` |
| `X-Timestamp` | Unix seconds when the request was signed |
| `X-Signature` | `sha256=` + hex HMAC-SHA256 of `"{X-Timestamp}.{raw body}"` with `Outbox__SigningSecret` |

Failed deliveries (non-2xx, timeout) are retried with exponential backoff (5s, 10s, 20s … capped at 1h)
and dead-lettered after `Outbox__MaxAttempts`. Check `GET /api/outbox` for status.

## Make.com

[`make-blueprint.json`](make-blueprint.json) is an example scenario: **Custom webhook → filter `type = order.paid` → Google Sheets "Add a row"**.

1. Make → *Create a new scenario* → `…` menu → *Import Blueprint* → choose the file.
2. Open the webhook module, click *Add* to create a webhook and copy its URL.
3. Pick your Google connection, spreadsheet and sheet in the second module.
4. Set `Outbox__WebhookUrl` to the copied URL and restart the API.
5. Run a test purchase; Make detects the data structure from the first request.

The blueprint was written by hand against Make's documented blueprint format, not exported from a live
account, so Make may ask you to re-select modules after import. Make's webhook module does not verify
HMAC signatures by itself; treat the webhook URL as a secret, or verify `X-Signature` in a code step.

## Zapier

Zapier has no blueprint import for this, but the setup takes two minutes:

1. Create a Zap with trigger **Webhooks by Zapier → Catch Hook** (paid Zapier plan required).
2. Copy the hook URL into `Outbox__WebhookUrl` and run a test purchase so Zapier sees a sample.
3. Add a filter step `Type` *exactly matches* `order.paid`, then any action (Sheets, Slack, HubSpot…).

Like Make, Catch Hook does not check the signature; add a *Code by Zapier* step if you need to verify it.

## n8n

Use a **Webhook** node (POST) followed by a **Crypto** node (HMAC, SHA256, secret = signing secret,
value = `{{$json.headers["x-timestamp"]}}.{{JSON.stringify($json.body)}}`) and an **IF** node comparing
the result with `X-Signature`. Note that re-serialising the body may change whitespace; use the raw body
option of the Webhook node for exact verification.
