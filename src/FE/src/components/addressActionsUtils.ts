export { copyTextToClipboard } from '../lib/clipboard';

export function getAddressMapsUrl(address: string): string {
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
}
