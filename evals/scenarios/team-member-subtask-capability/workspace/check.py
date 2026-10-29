import importlib
import sys
from pathlib import Path
sys.path.insert(0, str(Path.cwd()))
from backend.discounts import discount
from backend.shipping import shipping
from backend import service
from client.receipt import render
assert discount(1001) == 100
assert shipping(1000) == 200
assert shipping(1001) == 300
assert shipping(2001) == 400
r = service.receipt(1000, 1001)
assert r == dict(subtotal=1000, discount=100, shipping=300, total=1200), r
assert render(r) == 'Total: 12.00'
delegated_policy = importlib.import_module('backend.shipping')
delegated_policy.shipping = lambda weight: 999
importlib.reload(service)
assert service.receipt(1000, 1001)['shipping'] == 999, 'Service must consume the delegated policy module'
print('TEAM_SUBTASK_OK')
