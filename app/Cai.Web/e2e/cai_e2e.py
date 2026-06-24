from playwright.sync_api import sync_playwright
import sys

BASE = "http://localhost:5190"
NAV = ["The Standard", "Lenses", "Dimensions", "Calculator", "Verify", "API"]
fail = []

with sync_playwright() as p:
    b = None
    for exe in ["/snap/bin/chromium", "/usr/bin/chromium-browser"]:
        try:
            b = p.chromium.launch(executable_path=exe, args=["--no-sandbox"]); print("launched via", exe); break
        except Exception as e:
            print("could not launch", exe, "->", str(e)[:80])
    if b is None:
        print("NO BROWSER"); sys.exit(2)

    pg = b.new_page()
    errors = []
    pg.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
    pg.on("pageerror", lambda e: errors.append(str(e)))

    # 1. nav links all navigate (no 404)
    for label in NAV:
        pg.goto(BASE, wait_until="networkidle")
        pg.locator("header.site nav a", has_text=label).first.click()
        pg.wait_for_load_state("networkidle")
        if "Not found" in pg.inner_text("body"):
            fail.append(f"nav '{label}' -> {pg.url} Not found")
        else:
            print(f"nav '{label}' -> {pg.url}  OK")

    # 2. dimensions: 124 rows
    pg.goto(BASE + "/dimensions", wait_until="networkidle")
    rows = pg.locator("table.lenses tbody tr").count()
    print("dimension rows:", rows)
    if rows < 120:
        fail.append(f"dimensions only {rows} rows")

    # 3. calculator folds the DIMENSION sample -> CAI 69.x Fair + a lens table
    pg.goto(BASE + "/calculator", wait_until="networkidle")
    pg.locator("button", has_text="Load the sample bundle").click(); pg.wait_for_load_state("networkidle")
    pg.locator("button", has_text="Compute the CAI").click(); pg.wait_for_load_state("networkidle")
    calc = pg.inner_text("body")
    if "CAI 69" in calc and "Fair" in calc and "maturity" in calc:
        print("calculator: dimension fold -> CAI 69.x Fair, lens table  OK")
    else:
        fail.append("calculator did not fold the dimension sample to ~69 Fair with a lens table")

    # 4. calculator also VERIFIES when the bundle carries headlineScore
    pg.goto(BASE + "/calculator", wait_until="networkidle")
    bundle = '{ "rubricVersion": "r", "headlineScore": 76.2, "dimensions": [ {"id":"D1","lens":"codeHealth","score":8,"confidence":1}, {"id":"D2","lens":"codeHealth","score":6,"confidence":1}, {"id":"D5","lens":"architecture","score":9,"confidence":1} ] }'
    pg.fill("#ev", bundle)
    pg.locator("button", has_text="Compute the CAI").click(); pg.wait_for_load_state("networkidle")
    if "Reproduced" in pg.inner_text("body"):
        print("calculator: inline verify -> Reproduced  OK")
    else:
        fail.append("calculator did not show inline 'Reproduced' for a bundle with headlineScore")

    # 5. API page documents the endpoints
    pg.goto(BASE + "/api-reference", wait_until="networkidle")
    api = pg.inner_text("body")
    for needle in ["/api/rubrics", "/api/score", "POST"]:
        if needle not in api:
            fail.append(f"API page missing '{needle}'")
    print("API page documents endpoints  OK")

    # 6. dark-mode secondary button is NOT white-on-white
    pg.emulate_media(color_scheme="dark")
    pg.goto(BASE + "/calculator", wait_until="networkidle")
    btn = pg.locator("button.btn:not(.primary)").first
    bg = btn.evaluate("e => getComputedStyle(e).backgroundColor")
    fg = btn.evaluate("e => getComputedStyle(e).color")
    print(f"dark btn  bg={bg}  fg={fg}")
    if bg == fg or (bg in ("rgb(255, 255, 255)", "rgba(0, 0, 0, 0)") and "255" in fg):
        fail.append(f"dark-mode button looks white-on-white (bg={bg} fg={fg})")

    if errors:
        fail.append("console/page errors: " + " | ".join(errors[:5]))
    b.close()

print("\n=== RESULT ===")
if fail:
    print("FAIL:")
    for f in fail: print("  -", f)
    sys.exit(1)
print("ALL CHECKS PASSED")
