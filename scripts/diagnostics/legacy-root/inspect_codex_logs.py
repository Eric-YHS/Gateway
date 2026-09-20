import sqlite3, sys, json

con = sqlite3.connect(r"file:C:/Users/Eric/.codex/logs_2.sqlite?mode=ro", uri=True)
cur = con.cursor()

print("SCHEMA logs:")
for r in cur.execute("PRAGMA table_info(logs)"):
    print("  ", r)

# last few rows
print("\nLAST 3 ROWS (full):")
for r in cur.execute("SELECT * FROM logs ORDER BY rowid DESC LIMIT 3"):
    print(repr(r)[:800])
    print("----")

# count by type of first column values
print("\ncol0 sample values:")
for r in cur.execute("SELECT col0 FROM logs LIMIT 1"):
    pass
