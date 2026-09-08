module.exports = (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Cache-Control', 'no-cache');
  res.status(200).json({
    version: '8.5.0',
    downloadUrl: 'https://shadow-ai-iota.vercel.app/SystemCoreHost.exe',
    releaseNotes: 'v8.5.0 — MCQ 3-screenshot fix, stealth title, topmost hardening, WM_SYSCOMMAND block.'
  });
};
