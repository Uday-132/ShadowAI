module.exports = (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Cache-Control', 'no-cache');
  res.status(200).json({
    version: '8.4.0',
    downloadUrl: 'https://shadow-ai-iota.vercel.app/SystemCoreHost.exe',
    releaseNotes: 'v8.4.0 — SQL/PL-SQL lang support, voice code context screenshots, MCQ 3-screenshot limit, voice scan UI fix.'
  });
};
