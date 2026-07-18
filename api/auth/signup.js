const db = require('../db');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');

const JWT_SECRET = process.env.JWT_SECRET || 'shadow_ai_default_secret_123';

module.exports = async (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

  if (req.method === 'OPTIONS') {
    return res.status(200).end();
  }

  if (req.method !== 'POST') {
    return res.status(405).json({ error: 'Method not allowed' });
  }

  let body = req.body || {};
  if (typeof body === 'string') {
    try { body = JSON.parse(body); } catch (e) {}
  }
  const { email, password } = body;
  if (!email || !password) {
    return res.status(400).json({ error: 'Email and password are required.' });
  }

  try {
    const checkUser = await db.query('SELECT id FROM users WHERE email = $1', [email.toLowerCase().trim()]);
    if (checkUser.rows.length > 0) {
      return res.status(400).json({ error: 'User already exists with this email.' });
    }

    const salt = await bcrypt.genSalt(10);
    const passwordHash = await bcrypt.hash(password, salt);

    const now = new Date();
    const trialEndsAt = new Date(now.getTime() + 5 * 60 * 1000);

    const insertResult = await db.query(
      `INSERT INTO users (email, password_hash, trial_ends_at) 
       VALUES ($1, $2, $3) RETURNING id, email, created_at, trial_ends_at`,
      [email.toLowerCase().trim(), passwordHash, trialEndsAt]
    );

    const user = insertResult.rows[0];
    const token = jwt.sign({ id: user.id, email: user.email }, JWT_SECRET, { expiresIn: '30d' });

    return res.status(201).json({
      token,
      email: user.email,
      trial_ends_at: user.trial_ends_at
    });
  } catch (error) {
    console.error('Signup error:', error);
    return res.status(500).json({ error: 'Internal server error: ' + error.message });
  }
};
