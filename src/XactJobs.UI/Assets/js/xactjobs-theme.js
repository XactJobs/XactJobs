// The color theme: light, dark, or auto (the operating system's setting, followed when it changes).
// Loaded in <head> before the styles, so the page is drawn in its theme from the start. The choice is kept in localStorage.
(function () {
    var storageKey = 'xactjobs-theme';
    var media = window.matchMedia('(prefers-color-scheme: dark)');

    function storedChoice() {
        try {
            var value = localStorage.getItem(storageKey);
            return value === 'light' || value === 'dark' ? value : 'auto';
        } catch (e) {
            return 'auto';
        }
    }

    function resolve(choice) {
        return choice === 'auto' ? (media.matches ? 'dark' : 'light') : choice;
    }

    function apply(choice) {
        var theme = resolve(choice);
        document.documentElement.setAttribute('data-bs-theme', theme);
        document.dispatchEvent(new CustomEvent('xactjobs-theme', { detail: { choice: choice, theme: theme } }));
    }

    window.xactJobsTheme = {
        choice: storedChoice,
        set: function (choice) {
            try {
                if (choice === 'light' || choice === 'dark') localStorage.setItem(storageKey, choice);
                else localStorage.removeItem(storageKey);
            } catch (e) { /* storage blocked: the choice lasts for this page */ }
            apply(choice === 'light' || choice === 'dark' ? choice : 'auto');
        }
    };

    media.addEventListener('change', function () {
        if (storedChoice() === 'auto') apply('auto');
    });

    apply(storedChoice());
})();
