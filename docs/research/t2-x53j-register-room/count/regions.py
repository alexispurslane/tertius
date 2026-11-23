import sys
rows=[l.rstrip("\n").split("\t") for l in open(sys.argv[1])]
stores=[i for i,r in enumerate(rows) if r[2].startswith("stack_store") and "i32" in r[2] and "r51" not in r[2] and i>100]
end=stores[0]+20
m=max(rows[:end], key=lambda r:int(r[1])); print("  visibility part (lines 1..%d): peak %s live at line %s: %s"%(end, m[1], m[0], m[2][:60]))
m=max(rows[end:], key=lambda r:int(r[1])); print("  response part: peak %s live at line %s: %s"%(m[1], m[0], m[2][:60]))
print("  array stores", len(stores), "array loads", sum(1 for r in rows if r[2].startswith("stack_load") and "i32" in r[2]), "all stack instructions", sum(1 for r in rows if r[2].startswith("stack_")))
