window.kayeDm = window.kayeDm || {};

// One-shot count-up for KPI/summary numbers. `decimals` controls formatting
// (2 for peso amounts, 0 for plain counts); skips the animation entirely
// under prefers-reduced-motion (shows the final value immediately).
window.kayeDm.countUp = function (elementId, target, prefix, decimals, durationMs) {
    const el = document.getElementById(elementId);
    if (!el) {
        return;
    }

    const format = value => `${prefix}${value.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })}`;

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        el.textContent = format(target);
        return;
    }

    const start = performance.now();
    const duration = durationMs || 600;

    function tick(now) {
        const progress = Math.min((now - start) / duration, 1);
        const eased = 1 - Math.pow(1 - progress, 3);
        el.textContent = format(target * eased);
        if (progress < 1) {
            requestAnimationFrame(tick);
        }
    }

    requestAnimationFrame(tick);
};
