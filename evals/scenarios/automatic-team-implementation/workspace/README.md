# Order receipt exercise

Use Python 3 and its standard library only. Run tests with `python -m unittest discover -s tests`.
The server and terminal client are separate areas, with one fixed receipt contract. Do not create
Git worktrees, commits, network services or dependencies. Keep all changes inside this workspace.

## Server

Implement these independent modules, each with focused unit tests. All money values are integer
cents. Reject negative amounts, counts and unknown modes with ValueError. Do not mutate inputs.

- `server/discounts.py`, `discount(subtotal, customer, coupon)`: customer is standard, silver or
  gold (0%, 5%, 10%). Coupon is none, SAVE10 or TAKE300 (0%, 10%, fixed 300 cents). Apply the
  customer reduction first, round down, then the coupon reduction, round down. Return the total
  discount, capped at subtotal. A zero subtotal is valid. Reject unknown customers and coupons.
- `server/taxes.py`, `tax(net, region)`: exempt, low or standard at 0%, 7% or 20%. Round tax up
  to a whole cent. A zero net is valid. Reject unknown regions.
- `server/shipping.py`, `shipping(weight, zone, express)`: weight is a positive integer in grams;
  zone is local, national or international with base fees 200, 500 or 1500 cents. Charge another
  100 cents for every started kg above the first kg. Express doubles the complete fee. Reject
  zero weight, negative weight, unknown zones and non-boolean express.
- `server/service.py`, `receipt(order)`: order fields subtotal, customer, coupon, region, weight,
  zone and express use those modules. Return exactly subtotal, discount, tax, shipping and total,
  where total is subtotal minus discount plus tax plus shipping. This is the shared JSON contract.

## Client

`client/receipt.py`, `render(receipt)`: return exactly five newline-separated lines, no final newline:
Subtotal: N.NN / Discount: N.NN / Tax: N.NN / Shipping: N.NN / Total: N.NN, replacing each slash
with a newline and each amount with dollars from integer cents, always two fractional digits.
Reject missing fields, negative values and non-integer values with ValueError. Do not import
server internals: consume only the fixed receipt dictionary contract.

Add a client demo taking an order as JSON on stdin and printing the rendered receipt. Agree any
contract questions between the server and client before integration. Validate both independent
areas and the combined JSON-to-receipt flow, including boundary cases.
