(function () {
    function pad(value) {
        return value.toString().padStart(2, '0');
    }

    function isValidDate(year, month, day) {
        if (year < 1900 || year > 2100 || month < 1 || month > 12 || day < 1 || day > 31) {
            return false;
        }

        const date = new Date(year, month - 1, day);
        return date.getFullYear() === year
            && date.getMonth() === month - 1
            && date.getDate() === day;
    }

    function formatDate(value) {
        const raw = (value ?? '').toString().trim();
        if (!raw || raw === '0' || raw === '—') {
            return '—';
        }

        // PMS numeric date: yyyyMMdd, for example 20260630.
        const compact = raw.match(/^(\d{4})(\d{2})(\d{2})$/);
        if (compact) {
            const year = parseInt(compact[1], 10);
            const month = parseInt(compact[2], 10);
            const day = parseInt(compact[3], 10);
            if (isValidDate(year, month, day)) {
                return `${pad(day)}/${pad(month)}/${year}`;
            }
        }

        // SQL/ISO date text: yyyy-MM-dd or yyyy-MM-dd HH:mm:ss.
        const iso = raw.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})(?:[ T].*)?$/);
        if (iso) {
            const year = parseInt(iso[1], 10);
            const month = parseInt(iso[2], 10);
            const day = parseInt(iso[3], 10);
            if (isValidDate(year, month, day)) {
                return `${pad(day)}/${pad(month)}/${year}`;
            }
        }

        // dd/MM/yyyy or dd-MM-yyyy: normalize separators only.
        const dmy = raw.match(/^(\d{1,2})[-/](\d{1,2})[-/](\d{4})(?:[ T].*)?$/);
        if (dmy) {
            const day = parseInt(dmy[1], 10);
            const month = parseInt(dmy[2], 10);
            const year = parseInt(dmy[3], 10);
            if (isValidDate(year, month, day)) {
                return `${pad(day)}/${pad(month)}/${year}`;
            }
        }

        return raw;
    }

    let isFormatting = false;

    function formatGuestDates(container) {
        if (isFormatting) {
            return;
        }

        isFormatting = true;
        try {
            const root = container || document;
            root.querySelectorAll('#gd-guests-list div').forEach(line => {
                const label = line.querySelector('strong')?.textContent?.replace(':', '').trim().toLowerCase();
                if (label !== 'arrival' && label !== 'departure') {
                    return;
                }

                const value = line.querySelector('span');
                if (!value) {
                    return;
                }

                const formatted = formatDate(value.textContent);
                if (value.textContent !== formatted) {
                    value.textContent = formatted;
                }
            });
        } finally {
            isFormatting = false;
        }
    }

    document.addEventListener('DOMContentLoaded', () => {
        const guestList = document.getElementById('gd-guests-list');
        if (!guestList) {
            return;
        }

        formatGuestDates(guestList);

        const observer = new MutationObserver(() => formatGuestDates(guestList));
        observer.observe(guestList, { childList: true, subtree: true });
    });
})();
