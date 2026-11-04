/**
 * Client-side text matching for short lists (catalogue): lower case, Arabic diacritics and tatweel removed, alef / ya / ta marbuta
 * forms unified — the same idea as the server's search normalisation (CLAUDE.md §6).
 */
export function normaliseSearch(value: string): string {
  return value
    .toLowerCase()
    .replace(/[ً-ْٰـ]/gu, '')
    .replace(/[آأإ]/gu, 'ا')
    .replace(/ى/gu, 'ي')
    .replace(/ة/gu, 'ه')
    .trim();
}
