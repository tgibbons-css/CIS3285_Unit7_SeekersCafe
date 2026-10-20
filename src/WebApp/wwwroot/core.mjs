// Pure functions keep cart arithmetic and the checkout contract easy to inspect/test.
export const toCents = price => Math.round(price * 100);
// Sum integer cents for the displayed estimate; OrderService calculates the authoritative total.
export const cartTotal = cart => cart.reduce((sum, item) => sum + toCents(item.price) * item.quantity, 0);

// Check the response shape before rendering it as an order in the UI.
export function isOrder(value) {
  return value && Number.isInteger(value.orderId) && value.orderId > 0
    && typeof value.customerName === 'string' && typeof value.orderDate === 'string'
    && ['Pending', 'Paid', 'Preparing', 'Ready', 'Completed', 'Cancelled'].includes(value.status)
    && Number.isFinite(value.totalAmount) && value.totalAmount >= 0
    && Array.isArray(value.orderItems) && value.orderItems.every(item => item
      && typeof item.itemName === 'string' && Number.isInteger(item.quantity) && item.quantity > 0
      && Number.isFinite(item.unitPrice) && item.unitPrice >= 0);
}

// Validate user selections for immediate feedback; the server independently validates every request.
export function buildOrderRequest(name, method, cart) {
  if (!name.trim() || name.trim().length > 100) throw new Error('Enter a name of up to 100 characters.');
  if (!['Credit Card', 'Dining Dollars'].includes(method)) throw new Error('Choose a simulated payment method.');
  if (!cart.length || cart.length > 50) throw new Error('Choose between 1 and 50 different items.');
  const seen = new Set();
  for (const item of cart) {
    if (!Number.isInteger(item.menuItemId) || item.menuItemId < 1 || seen.has(item.menuItemId)
        || !Number.isInteger(item.quantity) || item.quantity < 1 || item.quantity > 20)
      throw new Error('Choose a quantity from 1 to 20 for each distinct item.');
    seen.add(item.menuItemId);
  }
  // Prices, total, item names, dates and statuses are never sent by the browser.
  return { customerName: name.trim(), paymentMethod: method,
    items: cart.map(item => ({ menuItemId: item.menuItemId, quantity: item.quantity })) };
}

// Distinguish a saved order from a definite rejection or an uncertain outcome.
// A timeout or unexpected response cannot prove that the server did not save an order.
export function classifyCheckout(status, data) {
  const orderId = data?.order?.orderId ?? data?.orderId;
  if (Number.isInteger(orderId) && orderId > 0) {
    return { kind: status === 201 && isOrder(data.order) && data.order.status === 'Paid' ? 'success' : status === 402 ? 'declined' : 'unknown', orderId };
  }
  // Only these documented responses guarantee that no order was created.
  if (status === 400 || (status === 503 && data?.title === 'MenuService unavailable'))
    return { kind: 'rejected' };
  return { kind: 'uncertain' };
}
