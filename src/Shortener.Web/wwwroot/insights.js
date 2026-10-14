export function viewerPreferences() {
  return {
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
    reducedMotion: window.matchMedia("(prefers-reduced-motion: reduce)").matches
  };
}
