from playwright.sync_api import sync_playwright
import sys

BASE = "http://localhost:5190"
NAV = ["The Standard", "Lenses", "Dimensions", "Calculator", "Verify"]
fail = []

with sync_playwright() as p:
    b = None
    for exe in ["/snap/bin/chromium", "/usr/bin/chromium-browser"]:
        try:
            b = p.chromium.launch(executable_path=exe, args=["--no-sandbox"])
            print("launched via", exe)
            break
        except Exception as e:
            print("could not launch", exe, "->", str(e)[:80])
    if b is None:
        print("NO BROWSER"); sys.exit(2)
    pg = b.new_page()
    errors = []
    pg.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
    pg.on("pageerror", lambda e: errors.append(str(e)))

    # 1. home loads + nav links all navigate (no 404)
    pg.goto(BASE, wait_until="networkidle")
    print("home title:", pg.title())
    for label in NAV:
        pg.goto(BASE, wait_until="networkidle")
        link = pg.locator("header.site nav a", has_text=label).first
        link.click()
        pg.wait_for_load_state("networkidle")
        body = pg.inner_text("body")
        if "Not found" in body or pg.title() == "":
            fail.append(f"nav '{label}' -> {pg.url} shows Not found")
        else:
            print(f"nav '{label}' -> {pg.url}  OK ({pg.title()[:40]})")

    # 2. dimensions page actually renders the 124-dim catalog
    pg.goto(BASE + "/dimensions", wait_until="networkidle")
    txt = pg.inner_text("body")
    for needle in ["Cyclomatic Complexity", "Code Health", "124 dimensions"]:
        if needle not in txt:
            fail.append(f"dimensions missing '{needle}'")
    print("dimensions rows:", pg.locator("table.lenses tbody tr").count())

    # 3. calculator: Load sample -> Compute -> CAI 72 Healthy
    pg.goto(BASE + "/calculator", wait_until="networkidle")
    pg.locator("button", has_text="Load the sample bundle").click()
    pg.wait_for_load_state("networkidle")
    pg.locator("button", has_text="Compute the CAI").click()
    pg.wait_for_load_state("networkidle")
    calc = pg.inner_text("body")
    if "CAI 72" in calc and "Healthy" in calc:
        print("calculator: CAI 72 Healthy  OK")
    else:
        fail.append("calculator did not show 'CAI 72 ... Healthy'")

    # 4. verify: paste sample bundle -> Reproduced
    pg.goto(BASE + "/verify", wait_until="networkidle")
    sample = open("/home/jimmy/work/CAI/scorer/examples/evidence.sample.json").read()
    pg.fill("#ev", sample)
    pg.locator("button", has_text="Reproduce it").click()
    pg.wait_for_load_state("networkidle")
    ver = pg.inner_text("body")
    if "Reproduced" in ver:
        print("verify: Reproduced  OK")
    else:
        fail.append("verify did not reproduce the sample")

    if errors:
        fail.append("console/page errors: " + " | ".join(errors[:5]))
    b.close()

print("\n=== RESULT ===")
if fail:
    print("FAIL:")
    for f in fail: print("  -", f)
    sys.exit(1)
print("ALL CHECKS PASSED")
