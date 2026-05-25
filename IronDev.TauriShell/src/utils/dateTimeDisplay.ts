const localDateTimeFormat = new Intl.DateTimeFormat(undefined, {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit'
});

const utcDateTimeFormat = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'UTC',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false
});

export const DateTimeDisplay = {
  toLocalDisplay(value?: string | null) {
    const date = parseUtcDate(value);

    return date ? localDateTimeFormat.format(date) : 'Unavailable';
  },

  toUtcMetadata(value?: string | null) {
    const date = parseUtcDate(value);

    if (!date) {
      return 'UTC unavailable';
    }

    return `${utcDateTimeFormat.format(date).replace(',', '')} UTC`;
  },

  toUtcTooltip(value?: string | null) {
    const date = parseUtcDate(value);

    return date ? `${date.toISOString()} UTC` : 'UTC timestamp unavailable';
  }
};

function parseUtcDate(value?: string | null) {
  if (!value) {
    return null;
  }

  const normalized = /(?:z|[+-]\d\d:?\d\d)$/i.test(value) ? value : `${value}Z`;
  const date = new Date(normalized);

  return Number.isNaN(date.getTime()) ? null : date;
}
