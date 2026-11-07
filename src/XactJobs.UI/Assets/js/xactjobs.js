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

// runs for the page and for every piece of content htmx swaps in
htmx.onLoad(xactJobsLocalizeTimes);

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
