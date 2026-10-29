import sys
from pathlib import Path
sys.path.insert(0, str(Path.cwd()))
from server.discounts import discount
from server.taxes import tax
from server.shipping import shipping
from server.service import receipt
from client.receipt import render
assert discount(10000, 'gold', 'SAVE10') == 1900
assert discount(100, 'silver', 'TAKE300') == 100
assert discount(0, 'standard', 'none') == 0
assert tax(101, 'low') == 8
assert tax(0, 'standard') == 0
assert shipping(1000, 'local', False) == 200
assert shipping(1001, 'national', True) == 1200
assert shipping(2001, 'international', False) == 1700
order = dict(subtotal=10000, customer='gold', coupon='SAVE10', region='low', weight=1001, zone='national', express=True)
original = dict(order)
r = receipt(order)
assert r == dict(subtotal=10000, discount=1900, tax=567, shipping=1200, total=9867), r
assert order == original
assert render(r) == 'Subtotal: 100.00\nDiscount: 19.00\nTax: 5.67\nShipping: 12.00\nTotal: 98.67'
for function, args in [(discount,(-1,'gold','none')), (discount,(100,'other','none')), (tax,(10,'other')), (shipping,(0,'local',False)), (shipping,(1,'local',1)), (render,({},)), (render,({**r,'tax':-1},))]:
    try:
        function(*args)
    except ValueError:
        pass
    else:
        raise AssertionError((function.__name__, args))
print('RECEIPT_OK')
