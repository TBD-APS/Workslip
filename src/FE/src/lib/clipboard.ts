export type ClipboardWriteOptions = {
  allowLegacyFallback?: boolean;
};

async function copyWithLegacyFallback(value: string): Promise<void> {
  const textarea = document.createElement('textarea');
  textarea.value = value;
  textarea.setAttribute('readonly', '');
  textarea.style.position = 'fixed';
  textarea.style.top = '0';
  textarea.style.left = '-9999px';
  textarea.style.opacity = '0';
  document.body.appendChild(textarea);

  try {
    textarea.focus();
    textarea.select();
    textarea.setSelectionRange(0, value.length);
    const copied = typeof document.execCommand === 'function' && document.execCommand('copy');
    if (!copied) {
      throw new Error('clipboard_unavailable');
    }
  } finally {
    textarea.remove();
  }
}

export async function copyTextToClipboard(
  value: string,
  options: ClipboardWriteOptions = {},
): Promise<void> {
  const { allowLegacyFallback = true } = options;

  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(value);
      return;
    } catch {
      if (!allowLegacyFallback) {
        throw new Error('clipboard_unavailable');
      }
      // Safari and restricted browser contexts can expose the API while rejecting writes.
      // Fall through to the selection-based browser fallback before reporting an error.
    }
  } else if (!allowLegacyFallback) {
    throw new Error('clipboard_unavailable');
  }

  await copyWithLegacyFallback(value);
}
