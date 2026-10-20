import test from 'node:test';
import assert from 'node:assert/strict';
import { cartTotal, toCents, buildOrderRequest, classifyCheckout } from '../src/WebApp/wwwroot/core.mjs';

test('cart uses integer cents for quantities and decimal prices', () => {
  assert.equal(toCents(4.50), 450);
  assert.equal(cartTotal([{ price: .10, quantity: 3 }, { price: .20, quantity: 2 }]), 70);
  assert.equal(cartTotal([]), 0);
});
test('checkout sends only editable input; server retains control of prices', () => {
  assert.deepEqual(buildOrderRequest(' Demo ', 'Dining Dollars', [
    { menuItemId: 1, quantity: 2, price: .01, name: 'Forged', status: 'Paid' }
  ]), { customerName: 'Demo', paymentMethod: 'Dining Dollars', items: [{ menuItemId: 1, quantity: 2 }] });
});
test('invalid names, methods, empty carts, duplicates and quantities are rejected', () => {
  const valid = [{ menuItemId: 1, quantity: 1 }];
  assert.throws(() => buildOrderRequest(' ', 'Credit Card', valid));
  assert.throws(() => buildOrderRequest('x'.repeat(101), 'Credit Card', valid));
  assert.throws(() => buildOrderRequest('Demo', 'Cash', valid));
  assert.throws(() => buildOrderRequest('Demo', 'Credit Card', []));
  assert.throws(() => buildOrderRequest('Demo', 'Credit Card', [...valid, ...valid]));
  for (const quantity of [0, -1, 21, 1.5]) assert.throws(() => buildOrderRequest('Demo', 'Credit Card', [{ menuItemId: 1, quantity }]));
});
test('responses that guarantee no saved order permit a corrected checkout', () => {
  assert.equal(classifyCheckout(400, { errors: {} }).kind, 'rejected');
  assert.equal(classifyCheckout(503, { title: 'MenuService unavailable' }).kind, 'rejected');
});
test('known declined/unknown orders are retained for tracking rather than resubmitted', () => {
  assert.deepEqual(classifyCheckout(402, { orderId: 7 }), { kind: 'declined', orderId: 7 });
  assert.deepEqual(classifyCheckout(503, { order: { orderId: 8 } }), { kind: 'unknown', orderId: 8 });
});
test('a valid paid response confirms checkout; incomplete success is uncertain', () => {
  const order = { orderId: 1, customerName: 'Demo', orderDate: '2026-10-19T12:00:00Z', status: 'Paid', totalAmount: 9, orderItems: [] };
  assert.equal(classifyCheckout(201, { order }).kind, 'success');
  assert.equal(classifyCheckout(201, { orderId: 1 }).kind, 'unknown');
  for (const status of [200, 201, 404, 500, 503]) assert.equal(classifyCheckout(status, {}).kind, 'uncertain');
});
