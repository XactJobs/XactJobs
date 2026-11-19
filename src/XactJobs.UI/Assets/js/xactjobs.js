// XactJobs Dashboard JavaScript

document.addEventListener('DOMContentLoaded', function () {
    // Send the antiforgery token with every htmx request (the API checks it on the requests that change data)
    const antiforgery = document.querySelector('meta[name="xactjobs-antiforgery"]');

    document.body.addEventListener('htmx:configRequest', function (evt) {
        if (antiforgery && antiforgery.dataset.header) {
            evt.detail.headers[antiforgery.dataset.header] = antiforgery.content;
        }
    });

    // Spinner on the button while its request runs
    document.body.addEventListener('htmx:beforeRequest', function (evt) {
        const btn = evt.detail.elt;
        if (btn.tagName === 'BUTTON') {
            btn.disabled = true;
            btn.dataset.originalHtml = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span>';
        }
    });

    document.body.addEventListener('htmx:afterRequest', function (evt) {
        const btn = evt.detail.elt;
        if (btn.tagName === 'BUTTON' && btn.dataset.originalHtml) {
            btn.disabled = false;
            btn.innerHTML = btn.dataset.originalHtml;
            delete btn.dataset.originalHtml;
        }
    });

    // The API sends the outcome of an action as an htmx event, for errors too
    document.body.addEventListener('xactjobs-message', function (evt) {
        showToast(evt.detail.text, evt.detail.level);
    });

    document.body.addEventListener('htmx:responseError', function (evt) {
        const xhr = evt.detail.xhr;
        if (xhr.getResponseHeader('HX-Trigger')) return; // already shown

        let message = 'The request failed (' + xhr.status + ').';
        try {
            const body = JSON.parse(xhr.responseText);
            if (body && body.message) message = body.message;
        } catch (e) { /* not JSON */ }

        showToast(message, 'danger');
    });

    document.body.addEventListener('htmx:sendError', function () {
        showToast('The server could not be reached.', 'danger');
    });

    // Theme switch (the theme itself is set by xactjobs-theme.js, before the page is drawn)
    document.querySelectorAll('[data-xj-theme]').forEach(function (item) {
        item.addEventListener('click', function () {
            window.xactJobsTheme.set(item.dataset.xjTheme);
        });
    });
    xactJobsShowThemeChoice();

    // Bootstrap's tooltips start only when created (the search's help, in the filters, which htmx never swaps)
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (el) {
        new bootstrap.Tooltip(el);
    });

    // Error details modal
    const errorModal = document.getElementById('xj-error-modal');
    if (errorModal) {
        errorModal.addEventListener('show.bs.modal', function (evt) {
            const source = evt.relatedTarget;
            errorModal.querySelector('[data-xj-field="id"]').textContent = source.dataset.jobId || '';
            errorModal.querySelector('[data-xj-field="message"]').textContent = source.dataset.errorMessage || '';
            errorModal.querySelector('[data-xj-field="stack"]').textContent = source.dataset.errorStack || '';
            errorModal.querySelector('[data-xj-section="stack"]').hidden = !source.dataset.errorStack;
        });
    }
});

// The server renders UTC times; show them in the browser's time zone, with the UTC time as the tooltip
function xactJobsLocalizeTimes(root) {
    root.querySelectorAll('time[datetime]').forEach(function (el) {
        const date = new Date(el.getAttribute('datetime'));
        if (!isNaN(date)) el.textContent = date.toLocaleString();
    });
}

// right after htmx swaps content in, before the browser paints it: htmx.onLoad alone runs after the settle delay, so a refreshed
// table showed its UTC times first, and flickered
document.addEventListener('htmx:afterSwap', function (evt) {
    xactJobsLocalizeTimes(evt.detail.target);
});

// the page itself, and content swapped in another way (outerHTML replaces the target)
htmx.onLoad(xactJobsLocalizeTimes);

// Submits a page's filters (_Filters.cshtml). The period's times are worked out here, in the browser's time zone, and sent as UTC
// times (from, to); the last 24 hours, the default, are worked out by the server at each request, so they move with the time
function xactJobsSubmitFilters(form) {
    const period = form.elements['period'];

    if (period) {
        const day = form.elements['day'];
        day.hidden = period.value !== 'day';

        const range = xactJobsPeriodRange(period.value, day.value);

        // a day without a date yet: wait for one
        if (period.value === 'day' && !range) {
            day.focus();
            return;
        }

        form.elements['from'].value = range ? range.from.toISOString() : '';
        form.elements['to'].value = range ? range.to.toISOString() : '';
    }

    // without the empty fields in the URL
    Array.from(form.elements).forEach(function (field) {
        if (field.name && !field.value) field.disabled = true;
    });

    form.submit();
}

// The local start (included) and end (excluded) of a period, or null for the last 24 hours. Weeks start on Monday
function xactJobsPeriodRange(period, day) {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    // by the calendar, so a day with a daylight saving change still ends at midnight
    function addDays(date, days) {
        const result = new Date(date);
        result.setDate(result.getDate() + days);
        return result;
    }

    switch (period) {
        case 'today':
            return { from: today, to: addDays(today, 1) };
        case 'yesterday':
            return { from: addDays(today, -1), to: today };
        case 'week': {
            const monday = addDays(today, -((today.getDay() + 6) % 7));
            return { from: monday, to: addDays(monday, 7) };
        }
        case 'day': {
            const parts = (day || '').split('-').map(Number);
            if (parts.length !== 3 || parts.some(isNaN)) return null;
            const start = new Date(parts[0], parts[1] - 1, parts[2]);
            return { from: start, to: addDays(start, 1) };
        }
        default:
            return null;
    }
}

// Marks the chosen theme in the switch, and shows its icon on the switch's button
function xactJobsShowThemeChoice() {
    if (!window.xactJobsTheme) return;

    const choice = window.xactJobsTheme.choice();
    const icons = { light: 'bi-sun-fill', dark: 'bi-moon-stars-fill', auto: 'bi-circle-half' };

    document.querySelectorAll('[data-xj-theme]').forEach(function (item) {
        const chosen = item.dataset.xjTheme === choice;
        item.classList.toggle('active', chosen);
        item.setAttribute('aria-pressed', String(chosen));
    });

    document.querySelectorAll('[data-xj-theme-icon]').forEach(function (icon) {
        icon.className = 'bi ' + icons[choice];
    });
}

// The charts' text and grid lines take the theme's colors: as defaults for the charts the page creates, and set on the ones it
// already has when the theme changes
function xactJobsApplyChartTheme() {
    if (typeof Chart === 'undefined') return;

    const style = getComputedStyle(document.documentElement);
    const text = style.getPropertyValue('--bs-secondary-color').trim();
    const grid = style.getPropertyValue('--bs-border-color-translucent').trim();

    Chart.defaults.color = text;
    Chart.defaults.borderColor = grid;

    Object.values(Chart.instances).forEach(function (chart) {
        chart.options.color = text;
        chart.options.borderColor = grid;
        Object.values(chart.options.scales || {}).forEach(function (scale) {
            if (scale.ticks) scale.ticks.color = text;
            if (scale.grid) scale.grid.color = grid;
            if (scale.border) scale.border.color = grid;
        });
        if (chart.options.plugins && chart.options.plugins.legend && chart.options.plugins.legend.labels) {
            chart.options.plugins.legend.labels.color = text;
        }
        chart.update('none');
    });
}

xactJobsApplyChartTheme();

document.addEventListener('xactjobs-theme', function () {
    xactJobsShowThemeChoice();
    xactJobsApplyChartTheme();
});

// True while the user is looking at something an automatic refresh would disturb
function xactJobsIsBusy() {
    return !!document.querySelector('.modal.show');
}

function showToast(text, level) {
    const container = document.getElementById('xj-toasts');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = 'toast align-items-center border-0 text-bg-' + (level || 'secondary');
    toast.setAttribute('role', 'alert');

    const wrapper = document.createElement('div');
    wrapper.className = 'd-flex';

    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = text;

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-close btn-close-white me-2 m-auto';
    close.setAttribute('data-bs-dismiss', 'toast');

    wrapper.appendChild(body);
    wrapper.appendChild(close);
    toast.appendChild(wrapper);
    container.appendChild(toast);

    toast.addEventListener('hidden.bs.toast', function () { toast.remove(); });
    new bootstrap.Toast(toast, { delay: level === 'danger' ? 8000 : 4000 }).show();
}
