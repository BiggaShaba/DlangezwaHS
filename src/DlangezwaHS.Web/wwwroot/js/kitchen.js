// Kitchen screens — shared behaviour: pressed-button gradient, busy buttons on submit,
// "see more" lists, animated meters, and a small toast helper.
(function () {
    // Moving gradient whenever a .k-btn is pressed
    document.addEventListener('click', e => {
        const btn = e.target.closest('.k-btn');
        if (!btn || btn.classList.contains('is-busy') || btn.disabled) return;
        btn.classList.remove('is-pressed');
        void btn.offsetWidth;
        btn.classList.add('is-pressed');
    });
    document.addEventListener('animationend', e => {
        if (e.animationName === 'k-flow' && e.target.classList) e.target.classList.remove('is-pressed');
    });

    // Forms marked data-busy keep their submit button's gradient moving until the page changes
    document.addEventListener('submit', e => {
        const form = e.target;
        if (!form.matches('[data-busy]') || e.defaultPrevented) return;
        const btn = e.submitter && e.submitter.classList.contains('k-btn') ? e.submitter : form.querySelector('button[type=submit].k-btn');
        if (!btn) return;
        btn.classList.add('is-busy');
        if (btn.dataset.busyText) btn.innerHTML = `<span class="spinner-border spinner-border-sm" aria-hidden="true"></span>${btn.dataset.busyText}`;
        setTimeout(() => { btn.disabled = true; }, 0);
    });
    window.addEventListener('pageshow', () => document.querySelectorAll('.k-btn.is-busy').forEach(b => { b.classList.remove('is-busy'); b.disabled = false; }));

    // "See more": <div data-more-list data-more-initial="5" data-more-step="5"> … [data-more-item] … <button data-more-btn>
    window.kInitMore = function (root) {
        (root || document).querySelectorAll('[data-more-list]').forEach(list => {
            const initial = Number(list.dataset.moreInitial || 5), step = Number(list.dataset.moreStep || 5);
            const btn = document.querySelector(`[data-more-btn="${list.id}"]`);
            const items = () => Array.from(list.querySelectorAll(':scope [data-more-item]')).filter(i => !i.hasAttribute('data-filtered-out'));
            const update = () => {
                const hidden = items().filter(i => i.classList.contains('k-more-hidden')).length;
                if (btn) {
                    btn.hidden = hidden === 0;
                    const label = btn.querySelector('[data-more-count]');
                    if (label) label.textContent = Math.min(step, hidden);
                }
            };
            items().forEach((it, i) => it.classList.toggle('k-more-hidden', i >= initial));
            update();
            if (btn && !btn.dataset.bound) {
                btn.dataset.bound = '1';
                btn.addEventListener('click', () => {
                    items().filter(i => i.classList.contains('k-more-hidden')).slice(0, step).forEach(i => {
                        i.classList.remove('k-more-hidden');
                        i.classList.remove('k-more-in'); void i.offsetWidth; i.classList.add('k-more-in');
                    });
                    update();
                });
            }
        });
    };

    // Meters: <div class="k-meter"><span data-pct="42"></span></div> slide in after paint
    window.kAnimateMeters = function (root) {
        requestAnimationFrame(() => requestAnimationFrame(() =>
            (root || document).querySelectorAll('.k-meter > span[data-pct]').forEach(s => s.style.width = Math.min(100, Number(s.dataset.pct)) + '%')));
    };

    window.kToast = function (text) {
        const t = document.createElement('div');
        t.className = 'k-toast'; t.setAttribute('role', 'status');
        t.innerHTML = '<i class="bi bi-check-circle-fill"></i><span></span>';
        t.querySelector('span').textContent = text;
        document.body.appendChild(t);
        setTimeout(() => t.remove(), 4000);
    };

    // Numbers marked data-count count up from zero
    window.kCountUp = function (root) {
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        (root || document).querySelectorAll('[data-count]').forEach(el => {
            const target = Number(el.dataset.count), decimals = Number(el.dataset.decimals || 0), prefix = el.dataset.prefix || '';
            const start = performance.now(), dur = 1200;
            const tick = now => {
                const p = Math.min(1, (now - start) / dur), eased = 1 - Math.pow(1 - p, 3);
                el.textContent = prefix + (target * eased).toLocaleString('en-ZA', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
                if (p < 1) requestAnimationFrame(tick);
            };
            requestAnimationFrame(tick);
        });
    };

    document.addEventListener('DOMContentLoaded', () => { window.kInitMore(); window.kAnimateMeters(); window.kCountUp(); });
})();
