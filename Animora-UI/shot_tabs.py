import asyncio
from playwright.async_api import async_playwright

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await b.new_page(viewport={'width': 1440, 'height': 950}, device_scale_factor=1.5)
        errors = []
        pg.on('pageerror', lambda e: errors.append(str(e)))
        pg.on('console', lambda m: errors.append(m.text) if m.type == 'error' else None)
        await pg.goto('http://localhost:8080/appointments.html', wait_until='networkidle')
        await pg.wait_for_timeout(900)

        # list tab
        await pg.click('#scTabList')
        await pg.wait_for_timeout(600)
        await pg.evaluate("window.scrollTo(0, 0)")
        await pg.screenshot(path='/tmp/shot_tab_list.png')

        # list tab — history scope
        await pg.click('[data-scope="history"]')
        await pg.wait_for_timeout(400)
        await pg.screenshot(path='/tmp/shot_tab_list_hist.png')

        # settings tab
        await pg.click('#scTabSet')
        await pg.wait_for_timeout(600)
        await pg.screenshot(path='/tmp/shot_tab_set.png', full_page=True)

        # back to calendar tab via keyboard shortcut
        await pg.click('#scTabCal')
        await pg.wait_for_timeout(600)
        await pg.screenshot(path='/tmp/shot_tab_cal_back.png')

        print('JS_ERRORS:', errors if errors else 'none')
        await b.close()

asyncio.run(main())
