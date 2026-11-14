import sys, json
for l in sys.stdin:
    try: j = json.loads(l)
    except Exception: continue
    if j['variant'].startswith(sys.argv[1] + '-'):
        print(j['variant'], 'max register r%d' % j['max_register_index'], 'instructions', j['decoded_instructions'])
