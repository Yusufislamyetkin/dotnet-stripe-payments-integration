// Minimal vanilla JS front end for the demo. All state lives in the API.
const $ = (sel) => document.querySelector(sel);
const money = (amount, currency) =>
  new Intl.NumberFormat('en-US', { style: 'currency', currency: currency.toUpperCase() }).format(amount / 100);
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const badge = (s) => `<span class="badge ${esc(s)}">${esc(s)}</span>`;
const date = (d) => (d ? new Date(d).toLocaleString() : '');

function toast(message) {
  const el = $('#toast');
  el.textContent = message;
  el.style.display = 'block';
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (el.style.display = 'none'), 6000);
}

async function api(path, options = {}) {
  const res = await fetch(`/api${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
    body: options.body ? JSON.stringify(options.body) : undefined,
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const errors = data?.errors ? Object.values(data.errors).flat().join(' ') : '';
    throw new Error(`${data?.title ?? res.status}: ${data?.detail ?? errors}`);
  }
  return data;
}

const run = (fn) => async (...args) => {
  try { await fn(...args); } catch (e) { toast(e.message); }
};

// ---- Pricing page ---------------------------------------------------------
async function pricingPage() {
  const [catalog, sellers] = await Promise.all([api('/catalog'), api('/connect/accounts')]);
  $('#seller').innerHTML = '<option value="">Platform (no Connect)</option>' +
    sellers.filter((s) => s.chargesEnabled).map((s) => `<option value="${s.id}">${esc(s.email)}</option>`).join('');

  $('#products').innerHTML = catalog.products.map((p) => `
    <div class="card">
      <strong>${esc(p.name)}</strong>
      <span class="muted">${esc(p.description)}</span>
      <span class="price">${money(p.amount, p.currency)}</span>
      <button data-sku="${esc(p.sku)}" ${p.purchasable ? '' : 'disabled title="Set a Stripe test key and restart"'}>Buy now</button>
      <button class="secondary" data-check-sku="${esc(p.sku)}">Check coupon</button>
    </div>`).join('');

  $('#plans').innerHTML = catalog.plans.map((p) => `
    <div class="card">
      <strong>${esc(p.name)}</strong>
      <span class="price">${money(p.amount, p.currency)}<span class="muted"> / ${esc(p.interval)}</span></span>
      <span class="muted">${p.trialDays ? `${p.trialDays}-day free trial` : 'No trial'}</span>
      <button data-plan="${esc(p.code)}" ${p.purchasable ? '' : 'disabled title="Set a Stripe test key and restart"'}>Subscribe</button>
      <button class="secondary" data-check-plan="${esc(p.code)}">Check coupon</button>
    </div>`).join('');

  const form = () => ({ email: $('#email').value || null, promotionCode: $('#coupon').value.trim() || null });

  document.body.addEventListener('click', run(async (e) => {
    const t = e.target.closest('button');
    if (!t) return;
    const { email, promotionCode } = form();
    if (t.dataset.sku) {
      const r = await api('/checkout/one-time', { method: 'POST', body: { sku: t.dataset.sku, email, promotionCode, connectedAccountId: $('#seller').value || null } });
      location.href = r.url;
    } else if (t.dataset.plan) {
      const r = await api('/subscriptions/checkout', { method: 'POST', body: { planCode: t.dataset.plan, email, promotionCode } });
      location.href = r.url;
    } else if (t.dataset.checkSku || t.dataset.checkPlan) {
      if (!promotionCode) return toast('Enter a promotion code first (try WELCOME10).');
      const r = await api('/coupons/validate', { method: 'POST', body: { code: promotionCode, sku: t.dataset.checkSku ?? null, planCode: t.dataset.checkPlan ?? null } });
      toast(r.valid ? `${r.code}: ${r.description} → ${money(r.finalAmount, r.currency)}` : `${r.code}: ${r.reason}`);
    }
  }));
}

// ---- Orders page ----------------------------------------------------------
async function ordersPage() {
  const [orders, subs, outbox] = await Promise.all([api('/orders'), api('/subscriptions'), api('/outbox')]);

  $('#orders').innerHTML = orders.map((o) => `
    <tr><td>${date(o.createdAt)}</td><td>${esc(o.productName)}</td><td>${esc(o.customerEmail)}</td>
    <td>${money(o.amount, o.currency)}</td><td>${o.amountRefunded ? money(o.amountRefunded, o.currency) : ''}</td>
    <td>${esc(o.promotionCode)}</td><td>${badge(o.status)}</td>
    <td>${['Paid', 'PartiallyRefunded'].includes(o.status) ? `<button class="secondary" data-refund="${o.id}">Refund…</button>` : ''}</td></tr>`).join('')
    || '<tr><td colspan="8" class="muted">No orders yet.</td></tr>';

  $('#subscriptions').innerHTML = subs.map((s) => `
    <tr><td>${esc(s.customerEmail)}</td><td>${esc(s.planCode)}</td><td>${badge(s.status)}</td>
    <td>${date(s.trialEnd)}</td><td>${date(s.currentPeriodEnd)}${s.cancelAtPeriodEnd ? ' (cancels)' : ''}</td>
    <td>${['Canceled', 'IncompleteExpired'].includes(s.status) ? '' : `
      <button class="secondary" data-portal="${s.id}">Portal</button>
      <button class="secondary" data-change="${s.id}">Change plan…</button>
      <button class="secondary" data-cancel="${s.id}">Cancel</button>`}</td></tr>`).join('')
    || '<tr><td colspan="6" class="muted">No subscriptions yet.</td></tr>';

  $('#outbox').innerHTML = outbox.map((m) => `
    <tr><td>${date(m.createdAt)}</td><td>${esc(m.type)}</td><td>${badge(m.status)}</td><td>${m.attempts}</td>
    <td>${m.status === 'Pending' ? date(m.nextAttemptAt) : ''}</td><td>${esc(m.lastError)}</td></tr>`).join('')
    || '<tr><td colspan="6" class="muted">No integration events yet.</td></tr>';

  document.body.addEventListener('click', run(async (e) => {
    const t = e.target.closest('button');
    if (!t) return;
    if (t.dataset.refund) {
      const input = prompt('Amount in cents (leave empty for a full refund):');
      if (input === null) return;
      await api(`/orders/${t.dataset.refund}/refunds`, { method: 'POST', body: { amount: input ? Number(input) : null, reason: 'requested_by_customer' } });
      toast('Refund requested. The order updates when the charge.refunded webhook arrives — refresh in a few seconds.');
    } else if (t.dataset.portal) {
      location.href = (await api(`/subscriptions/${t.dataset.portal}/portal`, { method: 'POST' })).url;
    } else if (t.dataset.change) {
      const plan = prompt('New plan code (starter-monthly, pro-monthly, pro-yearly):');
      if (!plan) return;
      await api(`/subscriptions/${t.dataset.change}/change-plan`, { method: 'POST', body: { planCode: plan } });
      toast('Plan change sent to Stripe. Refresh after the webhook arrives.');
    } else if (t.dataset.cancel) {
      await api(`/subscriptions/${t.dataset.cancel}/cancel`, { method: 'POST', body: { atPeriodEnd: true } });
      toast('Cancellation scheduled at period end. Refresh after the webhook arrives.');
    }
  }));
}

// ---- Connect page ---------------------------------------------------------
async function connectPage() {
  const accounts = await api('/connect/accounts');
  $('#accounts').innerHTML = accounts.map((a) => `
    <tr><td>${esc(a.email)}</td><td>${esc(a.stripeAccountId)}</td><td>${a.detailsSubmitted ? '✓' : '–'}</td>
    <td>${a.chargesEnabled ? '✓' : '–'}</td><td>${a.payoutsEnabled ? '✓' : '–'}</td>
    <td><button class="secondary" data-onboard="${a.id}">Onboarding link</button>
        <button class="secondary" data-payouts="${a.id}">Payout status</button></td></tr>`).join('')
    || '<tr><td colspan="6" class="muted">No connected accounts yet.</td></tr>';

  $('#create').addEventListener('click', run(async () => {
    const r = await api('/connect/accounts', { method: 'POST', body: { email: $('#seller-email').value } });
    location.href = r.onboardingUrl;
  }));

  document.body.addEventListener('click', run(async (e) => {
    const t = e.target.closest('button');
    if (t?.dataset.onboard) location.href = (await api(`/connect/accounts/${t.dataset.onboard}/onboarding-link`, { method: 'POST' })).onboardingUrl;
    if (t?.dataset.payouts) $('#payouts').textContent = JSON.stringify(await api(`/connect/accounts/${t.dataset.payouts}/payouts`), null, 2);
  }));
}

const pages = { pricing: pricingPage, orders: ordersPage, connect: connectPage };
run(pages[document.body.dataset.page] ?? (async () => {}))();
