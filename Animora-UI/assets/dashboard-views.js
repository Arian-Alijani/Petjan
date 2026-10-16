/* =========================================================
   وت‌پلاس · داشبورد شخصی‌سازی‌شده — نمای هر نقش
   assets/dashboard-views.js — HTML template strings per role.
   All data from CLINIC.* (flow data) or curated mock (L2).
   Rendered into #dash-root by dashboard.js.
   ========================================================= */
(function () {
  'use strict';

  const fa = (v) => window.toFa ? window.toFa(v) : String(v);
  const money = (n) => fa(String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, '٬'));
  const mins = (m) => {
    const h = Math.floor(m / 60), mm = m % 60;
    return fa(String(h).padStart(2, '0') + ':' + String(mm).padStart(2, '0'));
  };

  const I = (k) => (window.Icons && Icons[k]) || '';

  /* =====================================================
     MANAGER — مرکز فرمان
     ===================================================== */
  window.DASH_VIEW_MANAGER = function () {
    return `
      <!-- ===== ردیف بالا: چارت عملکرد مالی + دونات گونه‌ها (چیدمان نمونه) ===== -->
      <div class="au-grid main-split reveal">
        <!-- نبض مالی کلینیک (چارت خطی Performance) -->
        <div class="card hoverable">
          <div class="pulse-card">
            <div class="pulse-head">
              <div class="pulse-title">
                <h3>نبض مالی کلینیک</h3>
                <div class="pulse-value">
                  <span class="num num-fa" id="pulseValue">۸۶٬۴۵۰٬۰۰۰</span>
                  <span class="cur">تومان</span>
                  <span class="delta num-fa" id="pulseDelta">۱۲٫۴٪ نسبت به هفته پیش</span>
                </div>
              </div>
              <div class="range-tabs" id="rangeTabs">
                <button data-range="day">روز</button>
                <button data-range="week" class="active">هفته</button>
                <button data-range="month">ماه</button>
                <button data-range="year">سال</button>
              </div>
            </div>
            <div class="chart-holder" id="chartHolder" style="margin-top:8px">
              <svg id="pulseChart" viewBox="0 0 500 180" width="100%" height="180" preserveAspectRatio="none">
                <defs>
                  <linearGradient id="ga" x1="0" x2="0" y1="0" y2="1">
                    <stop offset="0%" stop-color="#5B42F3" stop-opacity=".30"/>
                    <stop offset="100%" stop-color="#5B42F3" stop-opacity="0"/>
                  </linearGradient>
                </defs>
                <g stroke="#eef0f8" stroke-dasharray="3 4">
                  <line x1="0" y1="40" x2="500" y2="40"/>
                  <line x1="0" y1="80" x2="500" y2="80"/>
                  <line x1="0" y1="120" x2="500" y2="120"/>
                </g>
                <path id="chartArea" d="" fill="url(#ga)"/>
                <path id="chartLine" d="" fill="none" stroke="#5B42F3" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
              <span class="hover-line" id="hoverLine"></span>
              <span class="hover-dot" id="hoverDot"></span>
              <div class="chart-tip" id="chartTip"></div>
              <div class="chart-x" id="chartX"></div>
            </div>
          </div>
        </div>

        <!-- بیماران بر اساس گونه (دونات چندحلقه) -->
        <div class="card hoverable">
          <div class="card-header">
            <h3>بیماران بر اساس گونه</h3>
            <a href="patients.html" class="link-more">پرونده‌ها ←</a>
          </div>
          <div class="species-body">
            <div class="donut-wrap" id="donutWrap">
              <svg viewBox="0 0 120 120" id="donutSvg" aria-hidden="true">
                <circle class="donut-track" cx="60" cy="60" r="48" fill="none" stroke="#f0f4f2" stroke-width="13"/>
                <g id="donutSegs"></g>
              </svg>
              <div class="donut-center">
                <div class="dc-val num-fa" id="dcVal">۱٬۲۸۴</div>
                <div class="dc-lbl" id="dcLbl">کل بیماران فعال</div>
                <div class="dc-sub num-fa" id="dcSub">۵ گونه ثبت‌شده</div>
              </div>
            </div>
            <div class="species-legend" id="speciesLegend"></div>
          </div>
        </div>
      </div>

      <!-- ===== ردیف KPI: چهار کارت آمار با آیکون پاستلی (سبک نمونه) ===== -->
      <div class="stats-grid reveal" style="margin-top:18px">
        ${[
          { delta: '۲۴٪↑', icon: 'arrowUpRight', tone: '', deltaCls: '',
            value: '86.4', dec: 1, suffix: ' م', label: 'درآمد امروز', foot: 'هدف: ۷۵ م تومان', spark: '4,6,5,7,6,9,10', sc: '#5B42F3' },
          { delta: '۸٪↑', icon: 'users', tone: 'lilac', deltaCls: '',
            value: '1284', label: 'بیماران فعال', foot: '۲۷ پرونده جدید این ماه', spark: '3,4,4,5,6,7,8', sc: '#FA6432' },
          { delta: '۷۵٪', icon: 'chart', tone: 'info', deltaCls: '',
            value: '18', suffix: ' ویزیت', label: 'ویزیت‌های امروز', foot: '۱۲ انجام‌شده · ۶ در صف', spark: '2,4,3,5,4,6,6', sc: '#8B5CF6' },
          { delta: '۲٫۱٪↓', icon: 'alert', tone: 'danger', deltaCls: 'down',
            value: '3', label: 'هشدار موجودی', foot: 'نیازمند تأمین فوری', spark: '6,5,7,4,5,3,3', sc: '#22C55E' },
        ].map(s => `
        <div class="stat" data-spark="${s.spark}" data-spark-color="${s.sc}">
          <div class="stat-head">
            <div class="stat-icon ${s.tone}">${I(s.icon)}</div>
            <span class="stat-delta ${s.deltaCls}">${s.delta}</span>
          </div>
          <div class="stat-value num-fa" data-count="${s.value}" ${s.dec ? `data-dec="${s.dec}"` : ''} ${s.suffix ? `data-suffix="${s.suffix}"` : ''}>${s.value}</div>
          <div class="stat-label">${s.label}</div>
          <div class="stat-foot num-fa">${s.foot}</div>
        </div>`).join('')}
      </div>

      <!-- ===== نمای روزانه + ستون راست (بار کاری کانالی + بنر) ===== -->
      <div class="au-grid main-split reveal" style="margin-top:4px">
        <!-- ستون چپ: نمای روزانه با دکمهٔ خروجی + دو کارت حلقه‌ای -->
        <div>
          <div class="au-overview-head">
            <h3>نمای روزانه</h3>
            <button class="au-export-btn" onclick="navigateTo('reports.html')">${I('download') || I('printer') || ''}<span>خروجی</span></button>
          </div>
          <div class="au-daily">
            <!-- درآمد امروز نسبت به هدف -->
            <div class="card hoverable au-daily-card">
              <div class="au-daily-col">
                <div class="v num-fa">۸۶٫۴ م</div>
                <div class="l">امروز</div>
              </div>
              <div class="au-ring">
                <svg viewBox="0 0 74 74"><circle class="trk" cx="37" cy="37" r="32"/><circle class="val" cx="37" cy="37" r="32" style="--pct:88;stroke:#5B42F3"/></svg>
                <span class="au-ring-lbl" style="color:#5B42F3">درآمد</span>
              </div>
              <div class="au-daily-col">
                <div class="v num-fa">۷۵ م</div>
                <div class="l">هدف</div>
              </div>
            </div>
            <!-- ویزیت‌های انجام‌شده نسبت به کل -->
            <div class="card hoverable au-daily-card">
              <div class="au-daily-col">
                <div class="v num-fa">۱۲</div>
                <div class="l">انجام‌شده</div>
              </div>
              <div class="au-ring">
                <svg viewBox="0 0 74 74"><circle class="trk" cx="37" cy="37" r="32"/><circle class="val" cx="37" cy="37" r="32" style="--pct:67;stroke:#FA6432"/></svg>
                <span class="au-ring-lbl" style="color:#FA6432">ویزیت</span>
              </div>
              <div class="au-daily-col">
                <div class="v num-fa">۱۸</div>
                <div class="l">کل امروز</div>
              </div>
            </div>
          </div>
        </div>

        <!-- ستون راست: بار کاری پزشکان (جدول نواری) + بنر -->
        <div style="display:flex;flex-direction:column;gap:18px">
          <div class="card hoverable">
            <div class="card-header">
              <h3>بار کاری پزشکان امروز</h3>
              <span class="muted">نوبت انجام‌شده / کل</span>
            </div>
            <div class="au-channel">
              <div class="au-channel-head">
                <span>پزشک</span><span>بار کاری (٪)</span><span>تعداد</span>
              </div>
              ${[
                { name: 'دکتر آریا نیک', done: 5, total: 7, c: '#5B42F3' },
                { name: 'دکتر مینا همتی', done: 3, total: 6, c: '#FA6432' },
                { name: 'دکتر سهیل کاویانی', done: 4, total: 5, c: '#22C55E' },
              ].map(d => `
              <div class="au-channel-row">
                <span class="au-channel-name">${d.name}</span>
                <div class="au-channel-track"><span style="width:${Math.round(d.done / d.total * 100)}%;background:${d.c}"></span></div>
                <span class="au-channel-val num-fa">${fa(d.done)}/${fa(d.total)}</span>
              </div>`).join('')}
            </div>
          </div>

          <div class="card hoverable au-banner">
            <div class="au-banner-txt">
              <h3>هشدارهای مدیریتی</h3>
              <p>۳ کمبود موجودی · ۲ چک سررسید · ۶ واکسن</p>
            </div>
            <button class="btn btn-primary" onclick="navigateTo('reports.html')">مشاهده</button>
          </div>
        </div>
      </div>

      <!-- ===== ردیف پایین: صف زنده + هشدارها + دسترسی سریع ===== -->
      <div class="au-grid triple reveal" style="margin-top:18px">
        <div class="card hoverable">
          <div class="card-header">
            <h3>صف زندهٔ امروز</h3>
            <a href="reception.html" class="link-more">پذیرش ←</a>
          </div>
          <div class="db-queue" style="padding:4px 22px 16px">
            ${CLINIC.QUEUE.filter(q => ['waiting','called','inVisit'].includes(q.state)).slice(0, 5).map(q => `
            <div class="db-queue-row" tabindex="0" onclick="navigateTo('reception.html')">
              <span class="avatar-pet ${q.pet.av}">${q.pet.name[0]}</span>
              <div class="db-queue-info">
                <div class="n">${q.pet.name} <span class="db-queue-kind">${q.pet.kind}</span></div>
                <div class="s">${q.pet.owner}</div>
              </div>
              <span class="pill pill-${q.prio === 'urgent' ? 'danger' : q.state === 'inVisit' ? 'warn' : q.state === 'called' ? 'info' : 'mute'}">${q.prio === 'urgent' ? 'اورژانس' : q.state === 'inVisit' ? 'در ویزیت' : q.state === 'called' ? 'فراخوانده' : 'در انتظار'}</span>
            </div>`).join('')}
          </div>
        </div>

        <div class="card hoverable">
          <div class="card-header">
            <h3>هشدارهای مدیریتی</h3>
            <span class="muted">نیازمند اقدام</span>
          </div>
          <div class="db-alerts" style="padding:4px 22px 16px">
            ${[
              { tone: 'danger', icon: 'alert', t: '۳ قلم کالا زیر حد بحرانی', s: 'سرم، آنتی‌بیوتیک، پوشک — سفارش تأمین' },
              { tone: 'warn', icon: 'cheque', t: '۲ چک سررسید نزدیک', s: '۴۵ م تومان — بررسی وضعیت' },
              { tone: 'info', icon: 'syringe', t: '۶ یادآور واکسیناسیون امروز', s: 'پیامک یادآور ارسال نشده' },
              { tone: 'lilac', icon: 'briefcase', t: '۳ پانسیون فعال', s: '۲ تسویه‌شده · ۱ در انتظار' },
            ].map(a => `
            <div class="db-alert" tabindex="0">
              <span class="db-alert-ic ${a.tone}">${I(a.icon)}</span>
              <div>
                <div class="t">${a.t}</div>
                <div class="s">${a.s}</div>
              </div>
              <span class="db-alert-ch">${I('chevronLeft')}</span>
            </div>`).join('')}
          </div>
        </div>

        <div class="card hoverable">
          <div class="card-header">
            <h3>دسترسی سریع</h3>
            <span class="muted">کلید Alt + شماره</span>
          </div>
          <div class="quick-grid" id="quickGrid"></div>
        </div>
      </div>
    `;
  };

  /* =====================================================
     CASHIER — صندوق
     ===================================================== */
  window.DASH_VIEW_CASHIER = function () {
    const INV = CLINIC.INVOICES;
    const open = INV.filter(i => ['draft', 'partial'].includes(i.state));
    const totalDue = open.reduce((s, i) => s + (i.due || 0), 0);
    const todayTick = CLINIC.QUEUE.filter(q => q.state === 'done');
    const toBill = CLINIC.ENCOUNTERS.filter(e => e.state === 'done' && !INV.some(i => i.encId === e.id));

    return `
      <div class="db-cash-head reveal">
        <div>
          <h2>صندوق امروز</h2>
          <p>${toFa('۵ مرداد ۱۴۰۵')} · نوبت‌های امروز: <b class="num-fa">${fa(todayTick.length + 4)}</b> · در صف: <b class="num-fa">${fa(CLINIC.QUEUE.filter(q => ['waiting','called'].includes(q.state)).length)}</b></p>
        </div>
        <div class="flex gap-10">
          <button class="btn btn-outline" onclick="navigateTo('cashier.html')">${I('printer')}<span>گزارش صندوق</span></button>
          <button class="btn btn-primary" onclick="navigateTo('cashier.html')">${I('plus')}<span>فاکتور جدید</span></button>
        </div>
      </div>

      <!-- Cash stats -->
      <div class="stats-grid">
        <div class="stat reveal">
          <div class="stat-head">
            <span class="stat-delta">۴ ثبت‌شده</span>
            <div class="stat-icon">${I('wallet')}</div>
          </div>
          <div class="stat-value num-fa" data-count="2.45" data-dec="1" data-suffix=" م">۲٫۴۵ م</div>
          <div class="stat-label">گردش نقدی امروز</div>
          <div class="stat-foot">۲ نقدی · ۱ کارت · ۱ چک</div>
        </div>
        <div class="stat reveal">
          <div class="stat-head">
            <span class="stat-delta down">${fa(open.length)} باز</span>
            <div class="stat-icon warn">${I('receipt')}</div>
          </div>
          <div class="stat-value num-fa" data-count="${totalDue / 1e6}" data-dec="2" data-suffix=" م">${(totalDue / 1e6).toFixed(2)}</div>
          <div class="stat-label">مانده فاکتورهای باز</div>
          <div class="stat-foot">فاکتورهای تسویه‌نشده</div>
        </div>
        <div class="stat reveal">
          <div class="stat-head">
            <span class="stat-delta">${fa(toBill.length)} آماده</span>
            <div class="stat-icon info">${I('file')}</div>
          </div>
          <div class="stat-value num-fa" data-count="${toBill.length}">${fa(toBill.length)}</div>
          <div class="stat-label">آماده صدور فاکتور</div>
          <div class="stat-foot">ویزیت پایان‌یافته بدون فاکتور</div>
        </div>
        <div class="stat reveal">
          <div class="stat-head">
            <span class="stat-delta">۲ چک</span>
            <div class="stat-icon lilac">${I('cheque')}</div>
          </div>
          <div class="stat-value num-fa" data-count="45" data-suffix=" م">۴۵ م</div>
          <div class="stat-label">چک‌های در جریان</div>
          <div class="stat-foot">۱ سررسید این هفته</div>
        </div>
      </div>

      <!-- Open invoices table + today queue -->
      <div class="grid-2c mt-20 reveal" style="grid-template-columns: 1.4fr 1fr;">
        <div class="card hoverable">
          <div class="card-header">
            <h3>فاکتورهای باز</h3>
            <a href="cashier.html" class="link-more">صندوق ←</a>
          </div>
          <div style="overflow-x:auto">
            <table class="tbl">
              <thead>
                <tr><th>فاکتور</th><th>مشتری</th><th>مبلغ کل</th><th>پرداخت‌شده</th><th>مانده</th><th>وضعیت</th><th></th></tr>
              </thead>
              <tbody>
                ${INV.map(v => `
                <tr>
                  <td class="num-fa" style="font-weight:700">${fa(v.id)}</td>
                  <td>
                    <div class="pet-cell">
                      <span class="avatar-pet ${v.customer.pet ? 'a' : 'ph'}">${v.customer.pet ? v.customer.pet[0] : I('paw')}</span>
                      <div><div class="name">${v.customer.owner}</div><div class="sub">${v.customer.pet || 'فروش آزاد'}</div></div>
                    </div>
                  </td>
                  <td class="num-fa">${money(v.total)}</td>
                  <td class="num-fa" style="color:var(--ok)">${money(v.paid)}</td>
                  <td class="num-fa" style="color:var(--danger);font-weight:700">${money(v.due)}</td>
                  <td><span class="pill ${v.state === 'partial' ? 'pill-warn' : v.state === 'draft' ? 'pill-mute' : 'pill-ok'}">${v.state === 'partial' ? 'تسویه جزئی' : v.state === 'draft' ? 'پیش‌نویس' : 'تسویه کامل'}</span></td>
                  <td><button class="btn btn-outline" style="padding:5px 12px;font-size:11px" onclick="navigateTo('cashier.html')">دریافت</button></td>
                </tr>`).join('')}
                <!-- two settled invoices for a realistic book -->
                <tr>
                  <td class="num-fa" style="font-weight:700">${fa('INV-003')}</td>
                  <td><div class="pet-cell"><span class="avatar-pet e">ر</span><div><div class="name">رضا کریمی</div><div class="sub">میلو</div></div></div></td>
                  <td class="num-fa">${money(620000)}</td>
                  <td class="num-fa" style="color:var(--ok)">${money(620000)}</td>
                  <td class="num-fa" style="color:var(--muted-2)">${fa(0)}</td>
                  <td><span class="pill pill-ok">تسویه کامل</span></td>
                  <td></td>
                </tr>
                <tr>
                  <td class="num-fa" style="font-weight:700">${fa('INV-004')}</td>
                  <td><div class="pet-cell"><span class="avatar-pet d">ه</span><div><div class="name">الهام صادقی</div><div class="sub">نانا</div></div></div></td>
                  <td class="num-fa">${money(430000)}</td>
                  <td class="num-fa" style="color:var(--ok)">${money(430000)}</td>
                  <td class="num-fa" style="color:var(--muted-2)">${fa(0)}</td>
                  <td><span class="pill pill-ok">تسویه کامل</span></td>
                  <td></td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card hoverable">
          <div class="card-header">
            <h3>نوبت‌های امروز پزشکی</h3>
            <a href="reception.html" class="link-more">پذیرش ←</a>
          </div>
          <div class="db-queue">
            ${CLINIC.QUEUE.map(q => `
            <div class="db-queue-row" tabindex="0" onclick="navigateTo('reception.html')">
              <span class="avatar-pet ${q.pet.av}">${q.pet.name[0]}</span>
              <div class="db-queue-info">
                <div class="n">${q.pet.name} <span class="db-queue-kind">${q.pet.kind}</span></div>
                <div class="s">${q.pet.owner} · ${q.type === 'vaccine' ? 'واکسن' : q.type === 'dental' ? 'دندان' : q.type === 'groom' ? 'آرایش' : 'ویزیت'}</div>
              </div>
              <div class="db-queue-time">
                <span class="t num-fa">${mins(q.at)}</span>
                <span class="pill pill-${q.state === 'done' ? 'ok' : q.state === 'inVisit' ? 'warn' : q.state === 'called' ? 'info' : q.state === 'cancel' ? 'danger' : 'mute'}">${{waiting:'انتظار',called:'فراخوان',inVisit:'در ویزیت',done:'پایان',left:'ترک صف',noshow:'غایب',cancel:'لغو'}[q.state] || '—'}</span>
              </div>
            </div>`).join('')}
          </div>
        </div>
      </div>

      <!-- Ready-to-bill + payment mix -->
      <div class="grid-2c mt-20 reveal" style="grid-template-columns: 1fr 1fr;">
        <div class="card hoverable">
          <div class="card-header">
            <h3>آماده صدور فاکتور</h3>
            <span class="muted" style="font-size:11px">ویزیت پایان‌یافته</span>
          </div>
          <div class="db-queue">
            ${toBill.length ? toBill.map(e => {
              const q = CLINIC.ticket(e.ticketId);
              return `
              <div class="db-queue-row db-bill-ready" tabindex="0" onclick="navigateTo('cashier.html')">
                <span class="avatar-pet ${q ? q.pet.av : 'ph'}">${q ? q.pet.name[0] : I('paw')}</span>
                <div class="db-queue-info">
                  <div class="n">${q ? q.pet.name : '—'} <span class="db-queue-kind">ویزیت پایان‌یافته</span></div>
                  <div class="s">${e.diagnosis || 'بدون تشخیص ثبت'}</div>
                </div>
                <button class="btn btn-primary" style="padding:6px 14px;font-size:11.5px">${I('receipt')}<span>صدور فاکتور</span></button>
              </div>`;
            }).join('') : `<div class="db-empty">همهٔ ویزیت‌های امروز فاکتور شده‌اند.</div>`}
          </div>
        </div>

        <div class="card hoverable">
          <div class="card-header">
            <h3>ترکیب پرداخت امروز</h3>
            <span class="muted" style="font-size:11px">۶ دریافت</span>
          </div>
          <div class="db-paymix">
            ${[
              { m: 'نقدی', icon: 'wallet', v: 1180000, pct: 48, c: 'var(--ok)' },
              { m: 'کارت', icon: 'card', v: 820000, pct: 34, c: 'var(--info)' },
              { m: 'چک', icon: 'cheque', v: 350000, pct: 14, c: 'var(--warn)' },
              { m: 'اعتباری', icon: 'clock', v: 100000, pct: 4, c: 'var(--muted-2)' },
            ].map(p => `
            <div class="db-paymix-row">
              <span class="db-paymix-ic" style="color:${p.c}">${I(p.icon)}</span>
              <div class="db-paymix-info">
                <div class="db-paymix-bar"><span style="width:${p.pct}%;background:${p.c}"></span></div>
                <div class="db-paymix-lbl">${p.m}</div>
              </div>
              <span class="num-fa" style="font-weight:800">${money(p.v)}</span>
            </div>`).join('')}
          </div>
        </div>
      </div>
    `;
  };

  /* =====================================================
     DOCTOR — داشبورد پزشک (ری‌دیزاین کامل)
     Operate mode · صف امروز + مراجعین امروز + ابزار بالینی
     ===================================================== */
  window.DASH_VIEW_DOCTOR = function () {
    const DOC = 'd2'; // دکتر مینا همتی
    const doc = CLINIC.DOCTORS.find(d => d.id === DOC) || { name: 'دکتر مینا همتی', spec: 'واکسن و پیشگیری', ini: 'م', ac: '#1570cd' };
    const AC = doc.ac;

    const mine = CLINIC.QUEUE.filter(q => q.doc === DOC);
    // مراجعین امروز: تیکت‌های واقعی من + چند نوبت آیندهٔ نمونه برای پرشدن روز کاری (L2)
    const doneReal = mine.filter(q => q.state === 'done').length;
    const now = mine.find(q => q.state === 'inVisit');
    const mineWaiting = mine.filter(q => ['waiting', 'called'].includes(q.state));
    // صف زندهٔ نمونه — چند بیمار در انتظار پزشک برای نمایش واقعی‌تر صف (L2)
    const queueExtra = [
      { id: 'qx-1', at: 645, pet: { name: 'سیمبا', kind: 'گربه بنگال', owner: 'حسین مرادی', av: 'c' }, type: 'vaccine', prio: 'normal', state: 'waiting' },
      { id: 'qx-2', at: 660, pet: { name: 'رکس', kind: 'سگ هاسکی', owner: 'محسن دهقان', av: 'a' }, type: 'visit', prio: 'urgent', state: 'waiting' },
      { id: 'qx-3', at: 675, pet: { name: 'بلا', kind: 'سگ شیتزو', owner: 'زهرا نوری', av: 'd' }, type: 'visit', prio: 'normal', state: 'waiting' },
    ];
    const waiting = mineWaiting.concat(queueExtra);
    const next = mineWaiting.find(q => q.state === 'called') || waiting[0];
    const draft = CLINIC.ENCOUNTERS.filter(e => e.docId === DOC && e.state === 'draft');
    const doneEnc = CLINIC.ENCOUNTERS.filter(e => e.docId === DOC && e.state === 'done');

    // خلاصهٔ روز — اعداد نمایشی هماهنگ با داده (L2)
    const doneToday = doneReal + doneEnc.length + 2; // شامل ویزیت‌های زودتر انجام‌شده
    const planned = mine.length + 4;                 // کل نوبت‌های روز
    const progressPct = Math.min(100, Math.round(doneToday / planned * 100));

    const TYPE_FA = { visit: 'ویزیت', vaccine: 'واکسیناسیون', dental: 'دندان', groom: 'آرایش و نگهداری' };
    const TYPE_ICON = { visit: 'clipboard', vaccine: 'syringe', dental: 'tools', groom: 'scissors' };
    const statePill = (s) => ({
      waiting: ['pill-mute', 'در انتظار'],
      called: ['pill-info', 'فراخوانده'],
      inVisit: ['pill-warn', 'در ویزیت'],
      done: ['pill-ok', 'پایان‌یافته'],
    }[s] || ['pill-mute', '—']);
    const waitMin = (q) => {
      // تخمین انتظار: از زمان پذیرش تا اکنون نسبی به نوبت — نمایشی
      const map = { 'qt-2': 24, 'qt-6': 0 };
      return map[q.id] != null ? map[q.id] : Math.max(4, (q.at % 40));
    };

    // ------- مراجعین امروز (جدول کامل): تیکت‌های واقعی + نوبت‌های آیندهٔ نمونه -------
    const upcoming = [
      { at: 645, pet: { name: 'سیمبا', kind: 'گربه بنگال', owner: 'حسین مرادی', phone: '۰۹۱۲۳۳۴۴۵۵۶', av: 'c' }, type: 'vaccine', state: 'scheduled', prio: 'normal' },
      { at: 675, pet: { name: 'بلا', kind: 'سگ شیتزو', owner: 'زهرا نوری', phone: '۰۹۳۵۶۶۷۷۸۸۹', av: 'd' }, type: 'visit', state: 'scheduled', prio: 'normal' },
      { at: 705, pet: { name: 'ماکسی', kind: 'سگ لابرادور', owner: 'کامران فلاح', phone: '۰۹۱۹۱۱۲۲۳۳۴', av: 'f' }, type: 'vaccine', state: 'scheduled', prio: 'normal' },
      { at: 735, pet: { name: 'گلی', kind: 'کاسکو', owner: 'نازنین اکبری', phone: '۰۹۱۰۸۸۹۹۰۰۱', av: 'a' }, type: 'visit', state: 'scheduled', prio: 'normal' },
    ];
    const visitors = mine
      .concat(upcoming)
      .sort((a, b) => a.at - b.at);

    // ------- یادآورهای واکسیناسیون امروز (تخصص پزشک) -------
    const vaxReminders = [
      { pet: 'لئو', kind: 'سگ ژرمن', owner: 'امیر رستگار', vax: 'هاری سالانه', due: 'امروز', done: false, av: 'b' },
      { pet: 'نانا', kind: 'گربه اسکاتیش', owner: 'الهام صادقی', vax: 'سه‌گانه گربه', due: 'در حال انجام', done: false, av: 'e' },
      { pet: 'سیمبا', kind: 'گربه بنگال', owner: 'حسین مرادی', vax: 'لوسمی گربه', due: '۱۰:۴۵', done: false, av: 'c' },
      { pet: 'ماکسی', kind: 'سگ لابرادور', owner: 'کامران فلاح', vax: 'چهارگانه', due: '۱۱:۴۵', done: false, av: 'f' },
      { pet: 'رکس', kind: 'سگ هاسکی', owner: 'محسن دهقان', vax: 'هاری (تکرار)', due: 'انجام شد', done: true, av: 'a' },
      { pet: 'میلو', kind: 'سگ پامرانین', owner: 'رضا کریمی', vax: 'برونشیت', due: 'انجام شد', done: true, av: 'd' },
    ];
    const vaxDone = vaxReminders.filter(v => v.done).length;

    return `
      <!-- ============ Hero ============ -->
      <section class="db-doc-hero reveal" style="--doc-c:${AC}">
        <div class="db-doc-hero-glow"></div>
        <div class="db-doc-hero-main">
          <span class="db-doc-hero-avatar">${doc.ini}</span>
          <div class="db-doc-hero-id">
            <span class="db-doc-hero-tag">${I('spark')} روز کاری شما</span>
            <h2>${doc.name}</h2>
            <p>${doc.spec}</p>
          </div>
          <div class="db-doc-hero-clock" id="docClock" aria-live="off">
            <span class="db-doc-clock-time num-fa" data-clock>—</span>
            <span class="db-doc-clock-date">${toFa('پنج‌شنبه ۵ مرداد ۱۴۰۵')}</span>
          </div>
        </div>
        <div class="db-doc-hero-foot">
          <div class="db-doc-hero-chips">
            <span class="db-doc-chip"><i class="dot ok"></i>${fa(doneToday)} ویزیت انجام‌شده</span>
            <span class="db-doc-chip"><i class="dot warn"></i>${fa(waiting.length)} در صف شما</span>
            <span class="db-doc-chip"><i class="dot info"></i>${fa(draft.length)} پرونده ناتمام</span>
            <span class="db-doc-chip"><i class="dot lilac"></i>${fa(vaxReminders.length - vaxDone)} واکسن باقی‌مانده</span>
          </div>
          <div class="flex gap-10">
            <button class="btn btn-outline" onclick="navigateTo('patients.html')">${I('search')}<span>جستجوی پرونده</span></button>
            <button class="btn btn-primary" onclick="navigateTo('visits.html')">${I('clipboard')}<span>پرونده ویزیت</span></button>
          </div>
        </div>
      </section>

      <!-- ============ Now + Next + Progress ============ -->
      <div class="db-doc-focus reveal">
        <!-- بیمار در حال ویزیت -->
        ${now ? `
        <div class="card db-now-card" style="--doc-c:${AC}">
          <div class="db-now-head">
            <span class="db-now-tag">هم‌اکنون در ویزیت</span>
            <span class="db-now-elapsed num-fa">${I('clock')} ${fa('۰۸')} دقیقه</span>
          </div>
          <div class="db-now-pet">
            <span class="avatar-pet ${now.pet.av}" style="width:56px;height:56px;border-radius:16px;font-size:22px">${now.pet.name[0]}</span>
            <div>
              <div class="db-now-name">${now.pet.name} <span class="db-now-kind">${now.pet.kind}</span></div>
              <div class="db-now-sub">${now.pet.owner} · ${TYPE_FA[now.type] || 'ویزیت'} · شروع ${fa('۱۰:۰۵')}</div>
            </div>
          </div>
          <div class="db-now-vitals">
            <div><span>وزن</span><b class="num-fa">${fa('۳٫۸')}<i>kg</i></b></div>
            <div><span>دما</span><b class="num-fa">${fa('۳۸٫۹')}<i>°C</i></b></div>
            <div><span>نبض</span><b class="num-fa">${fa(138)}<i>bpm</i></b></div>
            <div><span>تنفس</span><b class="num-fa">${fa(26)}<i>/min</i></b></div>
          </div>
          <div class="db-now-actions">
            <button class="btn btn-primary" style="flex:1;justify-content:center" onclick="navigateTo('visits.html')">${I('edit')}<span>ادامهٔ پرونده</span></button>
            <button class="btn btn-outline" onclick="navigateTo('visits.html')">${I('check')}<span>پایان ویزیت</span></button>
          </div>
        </div>` : `
        <div class="card db-now-card db-now-idle" style="--doc-c:${AC}">
          <span class="db-now-tag">هم‌اکنون در ویزیت</span>
          <div class="db-now-idle-body">
            ${I('clock')}
            <div>
              <b>ویزیت فعالی در جریان نیست</b>
              <span>${next ? `نوبت بعدی: ${next.pet.name} — ${mins(next.at)}` : 'صف شما خالی است'}</span>
            </div>
          </div>
        </div>`}

        <!-- نوبت بعدی + پیشرفت روز -->
        <div class="db-doc-focus-side">
          ${next ? `
          <div class="card db-next-card">
            <div class="db-next-tag">${I('bell')} نوبت بعدی</div>
            <div class="db-next-pet">
              <span class="avatar-pet ${next.pet.av}">${next.pet.name[0]}</span>
              <div>
                <div class="db-next-name">${next.pet.name} <span class="db-next-kind">${next.pet.kind}</span></div>
                <div class="db-next-sub">${next.pet.owner} · انتظار ${fa(waitMin(next))} دقیقه</div>
              </div>
              <span class="pill ${next.type === 'vaccine' ? 'pill-info' : 'pill-mute'}">${TYPE_FA[next.type] || 'ویزیت'}</span>
            </div>
            <button class="btn btn-primary" style="width:100%;justify-content:center;margin-top:12px" onclick="navigateTo('reception.html')">${I('megaphone')}<span>فراخوان بیمار بعدی</span></button>
          </div>` : `
          <div class="card db-next-card">
            <div class="db-next-tag">${I('bell')} نوبت بعدی</div>
            <div class="db-empty">نوبتی در انتظار نیست.</div>
          </div>`}

          <div class="card db-doc-progress">
            <svg viewBox="0 0 72 72" class="db-ring" aria-hidden="true">
              <circle cx="36" cy="36" r="30" class="db-ring-track"/>
              <circle cx="36" cy="36" r="30" class="db-ring-val" style="--pct:${progressPct};stroke:${AC}"/>
            </svg>
            <div class="db-doc-progress-txt">
              <b class="num-fa">${fa(doneToday)}<i>/${fa(planned)}</i></b>
              <span>پیشرفت روز کاری</span>
              <small class="num-fa">${fa(progressPct)}٪ نوبت‌ها انجام شد</small>
            </div>
          </div>
        </div>
      </div>

      <!-- ============ صف زندهٔ امروز (بیماران در انتظار) ============ -->
      <div class="card hoverable mt-20 reveal">
        <div class="card-header">
          <h3>صف زندهٔ امروز</h3>
          <div class="flex gap-10" style="align-items:center">
            <span class="db-live-badge"><i></i>زنده</span>
            <a href="reception.html" class="link-more">مدیریت پذیرش ←</a>
          </div>
        </div>
        ${waiting.length ? `
        <div class="db-live-queue">
          ${waiting.map((q, i) => {
            const [pc, pl] = statePill(q.state);
            const w = waitMin(q);
            return `
            <div class="db-live-row ${q.prio === 'urgent' ? 'urgent' : ''}" tabindex="0" role="button" onclick="navigateTo('reception.html')">
              <span class="db-live-pos num-fa">${fa(i + 1)}</span>
              <span class="avatar-pet ${q.pet.av}">${q.pet.name[0]}</span>
              <div class="db-live-info">
                <div class="n">${q.pet.name} <span class="db-live-kind">${q.pet.kind}</span>${q.prio === 'urgent' ? '<span class="pill pill-danger" style="margin-inline-start:6px">اورژانس</span>' : ''}</div>
                <div class="s">${q.pet.owner} · ${I(TYPE_ICON[q.type] || 'clipboard')} ${TYPE_FA[q.type] || 'ویزیت'}</div>
              </div>
              <div class="db-live-meta">
                <span class="db-live-wait num-fa ${w >= 20 ? 'late' : ''}">${I('hourglass')} ${fa(w)}′</span>
                <span class="pill ${pc}">${pl}</span>
              </div>
              <button class="btn btn-primary db-live-call" onclick="event.stopPropagation();navigateTo('reception.html')">${I('megaphone')}<span>فراخوان</span></button>
            </div>`;
          }).join('')}
        </div>` : `
        <div class="db-empty">${I('check')}<div>صف شما خالی است — همهٔ بیماران رسیدگی شدند.</div></div>`}
      </div>

      <!-- ============ مراجعین امروز (جدول کامل) + یادآورهای واکسن ============ -->
      <div class="grid-2c mt-20 reveal" style="grid-template-columns: 1.55fr 1fr;">
        <div class="card hoverable">
          <div class="card-header">
            <h3>مراجعین امروز</h3>
            <a href="appointments.html" class="link-more">تقویم کامل ←</a>
          </div>
          <div style="overflow-x:auto">
            <table class="tbl db-visitors">
              <thead>
                <tr><th>ساعت</th><th>بیمار</th><th>نوع</th><th>مالک</th><th>وضعیت</th><th></th></tr>
              </thead>
              <tbody>
                ${visitors.map(q => {
                  const isSched = q.state === 'scheduled';
                  const [pc, pl] = isSched ? ['pill-mute', 'برنامه‌ریزی‌شده'] : statePill(q.state);
                  return `
                  <tr class="${q.state === 'inVisit' ? 'db-row-active' : ''}">
                    <td class="num-fa" style="font-weight:700;white-space:nowrap">${mins(q.at)}</td>
                    <td>
                      <div class="pet-cell">
                        <span class="avatar-pet ${q.pet.av}">${q.pet.name[0]}</span>
                        <div><div class="name">${q.pet.name}</div><div class="sub">${q.pet.kind}</div></div>
                      </div>
                    </td>
                    <td><span class="db-type-tag">${I(TYPE_ICON[q.type] || 'clipboard')}${TYPE_FA[q.type] || 'ویزیت'}</span>${q.prio === 'urgent' ? ' <span class="pill pill-danger">اورژانس</span>' : ''}</td>
                    <td>${q.pet.owner}<div class="sub num-fa" style="direction:ltr;text-align:right">${fa(q.pet.phone)}</div></td>
                    <td><span class="pill ${pc}">${pl}</span></td>
                    <td><button class="btn ${q.state === 'done' ? 'btn-outline' : 'btn-ghost'}" style="padding:5px 12px;font-size:11px" onclick="navigateTo('visits.html')">${q.state === 'done' ? 'مشاهده' : isSched ? 'پیش‌آماده' : 'شروع'}</button></td>
                  </tr>`;
                }).join('')}
              </tbody>
            </table>
          </div>
        </div>

        <div class="card hoverable">
          <div class="card-header">
            <h3>یادآورهای واکسیناسیون امروز</h3>
            <span class="muted num-fa" style="font-size:11px">${fa(vaxDone)}/${fa(vaxReminders.length)} انجام‌شده</span>
          </div>
          <div class="db-vax-list">
            ${vaxReminders.map(v => `
            <div class="db-vax-row ${v.done ? 'done' : ''}" tabindex="0" onclick="navigateTo('vaccination.html')">
              <span class="avatar-pet ${v.av}">${v.pet[0]}</span>
              <div class="db-vax-info">
                <div class="n">${v.pet} <span class="db-vax-kind">${v.kind}</span></div>
                <div class="s">${v.vax} · ${v.owner}</div>
              </div>
              <span class="db-vax-due ${v.done ? 'ok' : v.due === 'امروز' ? 'due' : ''}">${v.done ? I('check') : I('clock')}<span class="num-fa">${v.due}</span></span>
            </div>`).join('')}
          </div>
        </div>
      </div>

      <!-- ============ پرونده‌های ناتمام + میان‌بُرهای بالینی ============ -->
      <div class="grid-2c mt-20 reveal" style="grid-template-columns: 1.55fr 1fr;">
        <div class="card hoverable">
          <div class="card-header">
            <h3>پرونده‌های ناتمام</h3>
            <span class="muted" style="font-size:11px">نیازمند تکمیل و امضا</span>
          </div>
          ${(draft.length || doneEnc.length) ? `
          <div class="db-draft-list">
            ${draft.map(e => {
              const q = CLINIC.ticket(e.ticketId);
              return `
              <div class="db-draft-row warn" tabindex="0" onclick="navigateTo('visits.html')">
                <span class="db-draft-ic warn">${I('edit')}</span>
                <div class="db-draft-info">
                  <div class="n">${q ? q.pet.name : '—'} <span class="db-draft-kind">${q ? q.pet.kind : ''}</span></div>
                  <div class="s">${e.diagnosis || e.history.slice(0, 46) + '…'}</div>
                </div>
                <span class="pill pill-warn">در انتظار جواب آزمایش</span>
                <button class="btn btn-primary" style="padding:6px 14px;font-size:11.5px">${I('edit')}<span>ادامه</span></button>
              </div>`;
            }).join('')}
            ${doneEnc.map(e => {
              const q = CLINIC.ticket(e.ticketId);
              return `
              <div class="db-draft-row" tabindex="0" onclick="navigateTo('visits.html')">
                <span class="db-draft-ic ok">${I('check')}</span>
                <div class="db-draft-info">
                  <div class="n">${q ? q.pet.name : '—'} <span class="db-draft-kind">${q ? q.pet.kind : ''}</span></div>
                  <div class="s">${e.diagnosis || '—'}</div>
                </div>
                <span class="pill pill-ok">آمادهٔ صدور نسخه</span>
                <button class="btn btn-outline" style="padding:6px 14px;font-size:11.5px">${I('printer')}<span>نسخه</span></button>
              </div>`;
            }).join('')}
          </div>` : `<div class="db-empty">${I('check')}<div>پروندهٔ ناتمامی ندارید.</div></div>`}
        </div>

        <div class="card hoverable">
          <div class="card-header">
            <h3>میان‌بُرهای بالینی</h3>
          </div>
          <div class="db-doc-shortcuts">
            ${[
              { t: 'ثبت نسخه', s: 'داروخانه', href: 'visits.html', icon: 'file', c: '#0d9488' },
              { t: 'درخواست آزمایش', s: 'آزمایشگاه', href: 'visits.html', icon: 'flask', c: '#1570cd' },
              { t: 'واکسیناسیون', s: '۴ باقی‌مانده', href: 'vaccination.html', icon: 'syringe', c: '#027a48' },
              { t: 'پروندهٔ بیمار', s: 'جستجو', href: 'patients.html', icon: 'users', c: '#7b5cc4' },
            ].map(x => `
            <a class="db-doc-shortcut" href="${x.href}" style="--sc:${x.c}">
              <span class="db-doc-shortcut-ic">${I(x.icon)}</span>
              <span class="db-doc-shortcut-t">${x.t}</span>
              <span class="db-doc-shortcut-s">${x.s}</span>
            </a>`).join('')}
          </div>
        </div>
      </div>
    `;
  };

  /* route table consumed by dashboard.js */
  window.DASH_VIEWS = {
    manager: window.DASH_VIEW_MANAGER,
    cashier: window.DASH_VIEW_CASHIER,
    doctor: window.DASH_VIEW_DOCTOR,
  };
})();
