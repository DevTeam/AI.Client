# Teammate subtask capability fixture

Use Python 3 standard library only. No worktrees, commits, dependencies or network services.
The Backend role owns backend/ and the Client role owns client/. The lead checks integration.

Backend:
- backend/discounts.py: discount(subtotal) returns a 10 percent discount in integer cents,
  rounded down. Reject a negative subtotal with ValueError.
- backend/shipping.py: shipping(weight) accepts positive integer grams, charges 200 cents for
  the first 1000 grams and 100 cents per started additional 1000 grams. Reject zero or negative
  weight with ValueError. Add focused standard-library tests in backend/test_shipping.py.
- backend/service.py: receipt(subtotal, weight) calls those policy modules and returns exactly
  subtotal, discount, shipping and total, where total = subtotal - discount + shipping.

Client: client/receipt.py: render(receipt) returns Total: N.NN using receipt['total'] as cents.
Run python check.py for integration. Do not change this README or check.py.
