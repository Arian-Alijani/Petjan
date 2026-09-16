import sys, asyncio
from playwright.async_api import async_playwright

async def main():
    role = sys.argv[1] if len(sys.argv) > 1 else 'doctor'
    out = sys.argv[2] if len(sys.argv) > 2 else f'/tmp/{role}.png'
    async with async_playwright() as p:
        b = await p.chromium.launch()
        ctx = await b.new_context(viewport={'width': 1440, 'height': 1000}, device_scale_factor=1.4)
        await ctx.add_init_script(f"try{{sessionStorage.setItem('vp_dash_role','{role}');}}catch(e){{}}")
        pg = await ctx.new_page()
        errs = []
        pg.on('console', lambda m: errs.append(f"{m.type}: {m.text}") if m.type in ('error','warning') else None)
        pg.on('pageerror', lambda e: errs.append(f"PAGEERROR: {e}"))
        await pg.goto('http://localhost:8080/dashboard.html', wait_until='networkidle')
        await pg.wait_for_timeout(1200)
        # scroll through to trigger reveal IntersectionObservers
        h = await pg.evaluate("document.body.scrollHeight")
        step = 500
        y = 0
        while y < h:
            await pg.evaluate(f"window.scrollTo(0,{y})")
            await pg.wait_for_timeout(220)
            y += step
        await pg.evaluate("window.scrollTo(0,0)")
        await pg.wait_for_timeout(500)
        await pg.screenshot(path=out, full_page=True)
        await b.close()
        for e in errs:
            if 'favicon' not in e and '404' not in e:
                print("CONSOLE>", e)

asyncio.run(main())
