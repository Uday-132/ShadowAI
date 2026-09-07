/**
 * GET /api/version
 * Returns the latest app version info.
 * Update `version` and `downloadUrl` here whenever you publish a new release.
 */
module.exports = (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Cache-Control', 'no-cache');

  res.status(200).json({
    version: '8.0.0',
    downloadUrl: 'https://shadow-ai-iota.vercel.app/SystemCoreHost.exe',
    releaseNotes: 'v8.0.0 — Download button fix, UI refresh, uninstall improvements.'
  });
};
