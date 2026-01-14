// XactJobs Dashboard JavaScript

// HTMX configuration
document.addEventListener('DOMContentLoaded', function() {
    // Add HTMX loading indicator
    document.body.addEventListener('htmx:beforeRequest', function(evt) {
        const btn = evt.detail.elt;
        if (btn.tagName === 'BUTTON') {
            btn.disabled = true;
            const originalHtml = btn.innerHTML;
            btn.dataset.originalHtml = originalHtml;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span>';
        }
    });

    document.body.addEventListener('htmx:afterRequest', function(evt) {
        const btn = evt.detail.elt;
        if (btn.tagName === 'BUTTON' && btn.dataset.originalHtml) {
            btn.disabled = false;
            btn.innerHTML = btn.dataset.originalHtml;
            delete btn.dataset.originalHtml;
        }
    });

    // Handle HTMX errors
    document.body.addEventListener('htmx:responseError', function(evt) {
        console.error('HTMX Error:', evt.detail);
        alert('An error occurred. Please try again.');
    });
});

// Utility functions
function formatNumber(num) {
    return new Intl.NumberFormat().format(num);
}

function formatDate(dateString) {
    return new Date(dateString).toLocaleString();
}

function formatDuration(ms) {
    if (ms < 1000) return ms + 'ms';
    if (ms < 60000) return (ms / 1000).toFixed(1) + 's';
    if (ms < 3600000) return (ms / 60000).toFixed(1) + 'm';
    return (ms / 3600000).toFixed(1) + 'h';
}

// Confirm dialog helper
function confirmAction(message) {
    return confirm(message);
}
