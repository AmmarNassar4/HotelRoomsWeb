(function () {
    function pad(value) {
        return value.toString().padStart(2, '0');
    }

    function formatNow() {
        const parts = new Intl.DateTimeFormat('en-GB', {
            timeZone: 'Asia/Riyadh',
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
        }).formatToParts(new Date()).reduce((acc, part) => {
            acc[part.type] = part.value;
            return acc;
        }, {});

        return `${parts.day}/${parts.month}/${parts.year} ${parts.hour}:${parts.minute}:${parts.second} KSA`;
    }

    function formatStored(value) {
        const raw = (value ?? '').toString().trim();
        if (!raw || raw === '—') {
            return '—';
        }

        const match = raw.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?/);
        if (!match) {
            return `${raw} KSA`;
        }

        const year = match[1];
        const month = pad(match[2]);
        const day = pad(match[3]);
        const hour = pad(match[4] || '00');
        const minute = pad(match[5] || '00');
        const second = pad(match[6] || '00');
        return `${day}/${month}/${year} ${hour}:${minute}:${second} KSA`;
    }

    window.HotelKsaTime = {
        formatNow,
        formatStored
    };
})();
