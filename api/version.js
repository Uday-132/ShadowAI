module.exports = (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Cache-Control', 'no-cache');
  res.status(200).json({
    version: '8.3.0',
    downloadUrl: 'https://shadow-ai-iota.vercel.app/SystemCoreHost.exe',
    releaseNotes: 'v8.3.0 — Modernized UI/UX and stealth scan improvements.'
  });
};
