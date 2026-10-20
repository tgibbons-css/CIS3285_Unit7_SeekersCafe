// Import calculation and validation helpers that do not depend on the browser's page elements.
import { cartTotal, toCents, buildOrderRequest, classifyCheckout, isOrder } from './core.mjs';

// Small helpers locate page elements and format integer cents as dollar amounts.
const $ = id => document.getElementById(id);
const money = cents => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100);
// UI state lives in memory. Busy flags prevent overlapping operations; guarded pauses
// checkout when an earlier submission might already have created an order.
const state = { config: null, menu: [], cart: [], category: 'All', menuReady: false,
  checkoutBusy: false, trackingBusy: false, staffBusy: false, guarded: false, lastOrderId: null, order: null };
const storageKey = 'seekers-cafe-demo-v1';

// Build elements with textContent so service-provided names are displayed as text rather than HTML.
function node(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
}
// Rebuilding a list replaces its buttons; restore keyboard focus to the matching new button.
function restoreButtonFocus(label) {
  if (!label) return;
  [...document.querySelectorAll('button[aria-label]')]
    .find(element => element.getAttribute('aria-label') === label)?.focus({ preventScroll: true });
}
// Create a reusable button and attach its action without accidentally submitting a form.
function button(text, action, className = 'subtle', label) {
  const element = node('button', className, text);
  element.type = 'button';
  if (label) element.setAttribute('aria-label', label);
  element.addEventListener('click', action);
  return element;
}
// Update feedback text and its CSS appearance; an empty message hides the feedback element.
function message(id, text, kind = 'info') {
  const element = $(id);
  element.textContent = text;
  element.dataset.kind = kind;
  element.hidden = !text;
}
// Keep only the cart and recovery information in this tab's session storage across page reloads.
// Browser storage is a convenience; the API services remain the source of saved business data.
function save() {
  try { sessionStorage.setItem(storageKey, JSON.stringify({ cart: state.cart, guarded: state.guarded, lastOrderId: state.lastOrderId })); }
  catch { /* Ordering still works when session storage is unavailable. */ }
}
// Treat stored JSON as untrusted input and restore only valid, bounded cart entries and order IDs.
function restore() {
  try {
    const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? '{}');
    const seen = new Set();
    state.cart = (Array.isArray(saved.cart) ? saved.cart : []).filter(item => {
      const valid = item && Number.isInteger(item.menuItemId) && item.menuItemId > 0
        && Number.isInteger(item.quantity) && item.quantity > 0 && item.quantity <= 20
        && typeof item.name === 'string' && item.name.length <= 100
        && Number.isFinite(item.price) && item.price >= 0 && item.price <= 1000 && !seen.has(item.menuItemId);
      if (valid) seen.add(item.menuItemId);
      return valid;
    }).slice(0, 50);
    state.guarded = saved.guarded === true;
    if (Number.isInteger(saved.lastOrderId) && saved.lastOrderId > 0 && saved.lastOrderId <= 2147483647)
      state.lastOrderId = saved.lastOrderId;
  } catch { /* Ignore corrupt or obsolete browser-local state. */ }
}
// Wrap fetch with a 15-second deadline and JSON parsing. HTTP errors are returned to callers;
// network failures and aborted requests throw, allowing checkout to handle uncertainty.
async function request(url, options = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 15000);
  try {
    const response = await fetch(url, { ...options, signal: controller.signal, cache: 'no-store' });
    const text = await response.text();
    let data = {};
    try { data = text ? JSON.parse(text) : {}; } catch { /* An invalid checkout response is uncertain. */ }
    return { ok: response.ok, status: response.status, data };
  } finally { clearTimeout(timer); }
}
// Prefer API validation messages, then ProblemDetails text, then a local fallback message.
function problem(data, fallback) {
  const fields = data?.errors ? Object.values(data.errors).flat().join(' ') : '';
  return fields || data?.detail || data?.title || fallback;
}
function orderUrl(path = '') { return `${state.config.orderBaseUrl}/api/orders${path}`; }
function jsonBody(value) { return { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) }; }

// Discover API addresses from WebApp, then ask MenuService directly for the available menu.
// Refresh cart display prices too; checkout will still verify current prices on the server.
async function loadMenu() {
  $('reload-menu').disabled = true;
  state.menuReady = false;
  $('menu-items').setAttribute('aria-busy', 'true');
  renderCart();
  message('menu-message', 'Loading the menu…');
  try {
    if (!state.config) {
      const config = await request('/api/config');
      if (!config.ok || !config.data.menuBaseUrl || !config.data.orderBaseUrl) throw new Error('Configuration unavailable');
      state.config = config.data;
    }
    const result = await request(`${state.config.menuBaseUrl}/api/menu`);
    if (!result.ok || !Array.isArray(result.data)) throw new Error('Menu unavailable');
    state.menu = result.data;
    state.menuReady = true;
    for (const item of state.cart) {
      const current = state.menu.find(product => product.menuItemId === item.menuItemId);
      if (current) Object.assign(item, { name: current.name, price: current.price });
    }
    if (state.category !== 'All' && !state.menu.some(item => item.category === state.category)) state.category = 'All';
    renderCategories();
    renderMenu();
    save();
  } catch {
    state.menuReady = false;
    $('menu-items').replaceChildren();
    message('menu-message', 'The menu is taking a break. Check that MenuService is running, then select Refresh menu.', 'error');
  } finally {
    $('reload-menu').disabled = false;
    $('menu-items').setAttribute('aria-busy', 'false');
    renderCart();
  }
}
// Derive category filters from menu data and mark the selected filter for assistive technology.
function renderCategories() {
  const categories = ['All', ...new Set(state.menu.map(item => item.category))];
  $('categories').replaceChildren(...categories.map(category => {
    const item = button(category, () => {
      state.category = category; renderCategories(); renderMenu();
      $('categories').querySelector('[aria-pressed=true]')?.focus({ preventScroll: true });
    }, 'category');
    item.setAttribute('aria-pressed', String(category === state.category));
    return item;
  }));
}
// Apply the search and category filters, then rebuild the visible menu cards.
function renderMenu() {
  if (!state.menuReady) return;
  const focusedLabel = document.activeElement?.getAttribute('aria-label');
  const query = $('search').value.trim().toLowerCase();
  const items = state.menu.filter(item => (state.category === 'All' || item.category === state.category)
    && `${item.name} ${item.description} ${item.category}`.toLowerCase().includes(query));
  message('menu-message', items.length ? '' : state.menu.length ? 'No matches. Try another search or category.' : 'The sample menu is empty. Check back after items are added.');
  $('menu-items').replaceChildren(...items.map(item => {
    const card = node('article', 'menu-card');
    const symbol = node('span', 'product-symbol', item.category === 'Bakery' ? '◒' : item.category === 'Breakfast' ? '◓' : '☕');
    symbol.setAttribute('aria-hidden', 'true');
    const bottom = node('div', 'card-bottom');
    const add = button('Add +', () => addItem(item), 'add-button', `Add ${item.name} to cart`);
    add.disabled = state.checkoutBusy || state.guarded;
    bottom.append(node('span', 'price', money(toCents(item.price))), add);
    card.append(symbol, node('div', 'item-category', item.category), node('h3', '', item.name), node('p', '', item.description), bottom);
    return card;
  }));
  restoreButtonFocus(focusedLabel);
}
// Combine repeated selections into one cart line while enforcing the UI's quantity limits.
function addItem(item) {
  if (state.checkoutBusy || state.guarded) return;
  const existing = state.cart.find(line => line.menuItemId === item.menuItemId);
  if (existing && existing.quantity >= 20) { message('checkout-message', 'Choose up to 20 of each item.', 'warning'); return; }
  if (!existing && state.cart.length >= 50) { message('checkout-message', 'Choose up to 50 different items.', 'warning'); return; }
  if (existing) existing.quantity++;
  else state.cart.push({ menuItemId: item.menuItemId, name: item.name, price: item.price, quantity: 1 });
  save(); renderCart();
}
// Adjust one line's quantity; reducing it to zero removes that line from the cart.
function changeQuantity(id, delta) {
  if (state.checkoutBusy || state.guarded) return;
  const item = state.cart.find(line => line.menuItemId === id);
  if (!item || item.quantity + delta > 20) return;
  item.quantity += delta;
  state.cart = state.cart.filter(line => line.quantity > 0);
  save(); renderCart();
}
// Rebuild cart rows and estimated totals, and lock controls during checkout or uncertainty.
function renderCart() {
  const focusedLabel = document.activeElement?.getAttribute('aria-label');
  const locked = state.checkoutBusy || state.guarded;
  $('cart-count').textContent = state.cart.reduce((sum, item) => sum + item.quantity, 0);
  $('cart-total').textContent = money(cartTotal(state.cart));
  const unavailable = state.cart.some(item => !state.menu.some(product => product.menuItemId === item.menuItemId));
  if (!state.cart.length) $('cart-items').replaceChildren(node('p', 'empty-cart muted', 'Your cart is waiting for something good. Add a drink or a bite to get started.'));
  else $('cart-items').replaceChildren(...state.cart.map(item => {
    const row = node('div', 'cart-line');
    const top = node('div', 'cart-line-top');
    top.append(node('strong', '', item.name), node('span', '', money(toCents(item.price) * item.quantity)));
    const controls = node('div', 'quantity-controls');
    const minus = button('−', () => changeQuantity(item.menuItemId, -1), '', `Decrease ${item.name} quantity`);
    const plus = button('+', () => changeQuantity(item.menuItemId, 1), '', `Increase ${item.name} quantity`);
    const remove = button('Remove', () => { if (!locked) { state.cart = state.cart.filter(line => line.menuItemId !== item.menuItemId); save(); renderCart(); } }, 'remove', `Remove ${item.name} from cart`);
    minus.disabled = locked; plus.disabled = locked || item.quantity === 20; remove.disabled = locked;
    const count = node('span', '', String(item.quantity));
    count.setAttribute('aria-label', `${item.name} quantity ${item.quantity}`);
    controls.append(minus, count, plus, remove);
    row.append(top, controls);
    if (state.menuReady && !state.menu.some(product => product.menuItemId === item.menuItemId))
      row.append(node('p', 'small', 'No longer available. Remove this item before ordering.'));
    return row;
  }));
  $('checkout-button').disabled = locked || !state.menuReady || !state.cart.length || unavailable;
  $('checkout-button').firstChild.textContent = state.checkoutBusy ? 'Submitting once… ' : 'Place simulated order ';
  $('customer-name').disabled = locked;
  document.querySelectorAll('[name=paymentMethod]').forEach(input => { input.disabled = locked; });
  $('clear-uncertainty').hidden = !state.guarded || state.checkoutBusy;
  renderMenu();
  restoreButtonFocus(focusedLabel);
}
// Submit one checkout request and interpret its result without automatically retrying a POST.
// A saved order can have a successful, declined, or still-unknown payment outcome.
async function checkout(event) {
  event.preventDefault();
  if (state.checkoutBusy || state.guarded || $('checkout-button').disabled) return;
  let body;
  try { body = buildOrderRequest($('customer-name').value, new FormData($('checkout-form')).get('paymentMethod'), state.cart); }
  catch (error) { message('checkout-message', error.message, 'error'); return; }
  state.checkoutBusy = true;
  state.lastOrderId = null; state.order = null;
  $('order-id').value = ''; $('order-detail').hidden = true;
  message('track-message', '');
  // Persist before sending: a refresh or broken connection must never trigger an automatic repeat POST.
  state.guarded = true; save(); renderCart();
  message('checkout-message', 'Placing your order and checking the simulated payment…');
  try {
    const result = await request(orderUrl(), jsonBody(body));
    const outcome = classifyCheckout(result.status, result.data);
    if (outcome.orderId) {
      state.lastOrderId = outcome.orderId;
      state.cart = []; state.guarded = false;
      $('order-id').value = outcome.orderId;
      if (isOrder(result.data.order)) showOrder(result.data.order);
      else await trackOrder(outcome.orderId);
      const text = outcome.kind === 'success'
        ? `You’re on the list! Order #${outcome.orderId} is paid. Confirmed total: ${money(toCents(result.data.order.totalAmount))}. Follow its progress below.`
        : outcome.kind === 'declined'
          ? `Order #${outcome.orderId} was saved, but the simulated payment was declined. It remains Pending. You can check or cancel it below. No real payment occurred.`
          : `Order #${outcome.orderId} was saved, but its payment outcome is unknown. Use Check payment outcome below. Don’t resubmit checkout to check this order.`;
      message('checkout-message', text, outcome.kind === 'success' ? 'info' : 'warning');
      message('track-message', 'Your latest order is shown below. Refresh its status or check its saved payment.');
    } else if (outcome.kind === 'rejected') {
      state.guarded = false;
      message('checkout-message', `${problem(result.data, 'The order could not be accepted.')} No order was created. Your cart is still here.`, 'error');
      if (result.status === 400) await loadMenu();
    } else uncertainCheckout();
  } catch { uncertainCheckout(); }
  finally { state.checkoutBusy = false; save(); renderCart(); $('checkout-message').focus(); }
}
// Keep checkout paused when the response cannot establish whether an order was saved.
function uncertainCheckout() {
  message('checkout-message', 'The connection ended before checkout could be confirmed. The server may have saved an order. Check recent orders in the staff demo before starting another cart. Checkout is paused to avoid an accidental duplicate.', 'warning');
}
// Render saved order snapshots and the server-confirmed total, rather than the cart estimate.
function showOrder(order) {
  state.order = order;
  const panel = $('order-detail');
  panel.hidden = false;
  const heading = node('div', 'order-top');
  const badge = node('span', 'status-badge', order.status); badge.dataset.status = order.status;
  heading.append(node('h3', '', `Order #${order.orderId}`), badge);
  const date = new Date(order.orderDate).toLocaleString();
  const lines = node('ul', 'order-lines');
  for (const item of order.orderItems) {
    const line = node('li');
    line.append(node('span', '', `${item.quantity} × ${item.itemName}`), node('span', '', money(toCents(item.unitPrice) * item.quantity)));
    lines.append(line);
  }
  const total = node('div', 'total-row'); total.append(node('span', '', 'Confirmed order total'), node('strong', '', money(toCents(order.totalAmount))));
  const actions = node('div', 'order-actions');
  actions.append(button('Refresh status', () => trackOrder(order.orderId)));
  if (order.status === 'Pending') {
    actions.append(button('Check payment outcome', () => reconcile(order.orderId), 'primary'));
    actions.append(button('Cancel declined order', () => cancelOrder(order.orderId)));
  }
  panel.replaceChildren(heading, node('p', 'small muted', `${order.customerName} · ${date}`), lines, total, actions);
  if (order.status === 'Pending') panel.append(node('p', 'small muted', 'Pending does not prove payment failed. Check the saved payment first; only a confirmed decline can be cancelled.'));
}
// Fetch the latest saved order state with a read-only GET request.
async function trackOrder(id) {
  if (state.trackingBusy) return;
  if (!Number.isInteger(id) || id < 1 || id > 2147483647) { message('track-message', 'Enter a valid order number.', 'error'); return; }
  if (!state.config) { message('track-message', 'Reload the menu to reconnect to the services.', 'error'); return; }
  state.trackingBusy = true;
  message('track-message', 'Checking your order…');
  try {
    const result = await request(orderUrl(`/${id}`));
    if (!result.ok || !isOrder(result.data)) { $('order-detail').hidden = true; message('track-message', result.status === 404 ? `Order #${id} was not found.` : problem(result.data, 'Unable to retrieve the order.'), 'error'); return; }
    showOrder(result.data); message('track-message', 'Status updated. Refresh again whenever you like.');
    state.lastOrderId = id; save();
  } catch { message('track-message', 'Unable to reach OrderService. Try refreshing the status when it is running.', 'error'); }
  finally { state.trackingBusy = false; }
}
// Ask OrderService to check an existing payment result; reconciliation does not submit a new payment.
async function reconcile(id) {
  if (state.trackingBusy) return;
  state.trackingBusy = true;
  message('track-message', 'Checking the saved payment. This does not issue another payment.');
  try {
    const result = await request(orderUrl(`/${id}/payment/reconcile`), { method: 'POST' });
    if (isOrder(result.data.order)) showOrder(result.data.order);
    if (result.ok) message('track-message', result.data.payment.status === 'Declined'
      ? 'The simulated payment was declined. A Pending order may be cancelled.'
      : 'Payment confirmed. The saved order status is shown below.');
    else message('track-message', problem(result.data, 'Payment outcome is still unknown. Keep this order number.'), 'warning');
  } catch { message('track-message', 'Payment could not be checked. Keep this order number and try checking again later.', 'warning'); }
  finally { state.trackingBusy = false; }
}
// Request cancellation; OrderService verifies that the payment was declined and cancellation is allowed.
async function cancelOrder(id) {
  if (state.trackingBusy) return;
  state.trackingBusy = true;
  try {
    const result = await request(orderUrl(`/${id}/status`), { ...jsonBody({ status: 'Cancelled' }), method: 'PUT' });
    if (result.ok) { showOrder(result.data); message('track-message', 'The declined order was cancelled.'); }
    else message('track-message', problem(result.data, 'This order could not be cancelled.'), 'warning');
  } catch { message('track-message', 'The cancellation response was lost. Refresh this order before trying again.', 'warning'); }
  finally { state.trackingBusy = false; }
}
// Load the classroom staff view and offer the next fulfillment step for each eligible order.
async function loadOrders() {
  if (state.staffBusy) return;
  if (!state.config) { message('staff-message', 'Reload the menu to reconnect to the services.', 'error'); return; }
  state.staffBusy = true; $('reload-orders').disabled = true;
  message('staff-message', 'Loading recent orders…');
  try {
    const result = await request(orderUrl());
    if (!result.ok || !Array.isArray(result.data)) throw new Error('Orders unavailable');
    $('staff-orders').replaceChildren(...result.data.map(order => {
      const card = node('article', 'staff-card');
      const heading = node('div', 'order-top');
      heading.append(node('h3', '', `#${order.orderId} · ${order.customerName}`), node('span', 'status-badge', order.status));
      const actions = node('div', 'order-actions');
      actions.append(button('View order', () => { $('order-id').value = order.orderId; trackOrder(order.orderId); $('track').scrollIntoView(); }));
      const next = { Paid: 'Preparing', Preparing: 'Ready', Ready: 'Completed' }[order.status];
      if (next) actions.append(button(`Mark ${next}`, () => advanceOrder(order.orderId, next), 'primary'));
      card.append(heading, node('p', 'small muted', order.orderItems.map(item => `${item.quantity} × ${item.itemName}`).join(' · ')), node('strong', '', money(toCents(order.totalAmount))), actions);
      return card;
    }));
    message('staff-message', result.data.length ? 'Orders loaded. Refresh after changes made elsewhere.' : 'No orders yet.');
  } catch { message('staff-message', 'Unable to load orders. Check that OrderService is running.', 'error'); }
  finally { state.staffBusy = false; $('reload-orders').disabled = false; }
}
// Request a staff status change. The server enforces transitions and detects conflicting updates.
async function advanceOrder(id, status) {
  if (state.staffBusy) return;
  state.staffBusy = true;
  try {
    const result = await request(orderUrl(`/${id}/status`), { ...jsonBody({ status }), method: 'PUT' });
    if (!result.ok) { message('staff-message', problem(result.data, 'Status could not be updated. Refresh the orders.'), 'warning'); return; }
    if (state.order?.orderId === id) showOrder(result.data);
    state.staffBusy = false; await loadOrders();
    message('staff-message', `Order #${id} is ${status}.`);
  } catch { message('staff-message', 'The update could not be confirmed. Reload orders before trying again.', 'warning'); }
  finally { state.staffBusy = false; }
}

// Connect HTML controls to their handlers after the module has loaded and the page has been parsed.
$('reload-menu').addEventListener('click', loadMenu);
$('search').addEventListener('input', renderMenu);
$('checkout-form').addEventListener('submit', checkout);
$('track-form').addEventListener('submit', event => { event.preventDefault(); trackOrder(Number($('order-id').value)); });
$('reload-orders').addEventListener('click', loadOrders);
$('clear-uncertainty').addEventListener('click', () => {
  if (state.checkoutBusy) return;
  state.guarded = false; state.cart = []; save(); renderCart();
  message('checkout-message', 'A new empty cart is ready. Your earlier order, if saved, remains in the system.');
});
// Restore the tab's cart first, load current menu data, then retrieve the last known order if present.
restore();
renderCart();
if (state.guarded) uncertainCheckout();
await loadMenu();
if (state.lastOrderId) { $('order-id').value = state.lastOrderId; await trackOrder(state.lastOrderId); }
