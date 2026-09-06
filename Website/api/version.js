/**
 * GET /api/version
 * Returns the latest app version info.
 * Update `version` and `downloadUrl` here whenever you publish a new release.
 */
module.exports = (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Cache-Control', 'no-cache');

  res.status(200).json({
    version: '2.0.0',
    downloadUrl: 'https://shadow-ai-iota.vercel.app/setup.exe',
    releaseNotes: 'v2.0.0 — Dual-model MCQ verification with timeout & tiebreaker, live response timer, full markdown rendering, auto-update system, follow-up fix.'
  });
};
