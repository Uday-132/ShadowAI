// App.js - Interactivity logic for the Shadow AI Landing Page
const { Renderer, Program, Mesh, Geometry } = ogl;

const hexToRgb = hex => {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  return m ? [parseInt(m[1], 16) / 255, parseInt(m[2], 16) / 255, parseInt(m[3], 16) / 255] : [1, 1, 1];
};

const getAnchorAndDir = (origin, w, h) => {
  const outside = 0.2;
  switch (origin) {
    case 'top-left':
      return { anchor: [0, -outside * h], dir: [0, 1] };
    case 'top-right':
      return { anchor: [w, -outside * h], dir: [0, 1] };
    case 'left':
      return { anchor: [-outside * w, 0.5 * h], dir: [1, 0] };
    case 'right':
      return { anchor: [(1 + outside) * w, 0.5 * h], dir: [-1, 0] };
    case 'bottom-left':
      return { anchor: [0, (1 + outside) * h], dir: [0, -1] };
    case 'bottom-center':
      return { anchor: [0.5 * w, (1 + outside) * h], dir: [0, -1] };
    case 'bottom-right':
      return { anchor: [w, (1 + outside) * h], dir: [0, -1] };
    default: // "top-center"
      return { anchor: [0.5 * w, -outside * h], dir: [0, 1] };
  }
};

const vert = `
attribute vec2 position;
varying vec2 vUv;
void main() {
  vUv = position * 0.5 + 0.5;
  gl_Position = vec4(position, 0.0, 1.0);
}`;

const frag = `precision highp float;

uniform float iTime;
uniform vec2  iResolution;

uniform vec2  rayPos;
uniform vec2  rayDir;
uniform vec3  raysColor;
uniform float raysSpeed;
uniform float lightSpread;
uniform float rayLength;
uniform float pulsating;
uniform float fadeDistance;
uniform float saturation;
uniform vec2  mousePos;
uniform float mouseInfluence;
uniform float noiseAmount;
uniform float distortion;

varying vec2 vUv;

float noise(vec2 st) {
  return fract(sin(dot(st.xy, vec2(12.9898,78.233))) * 43758.5453123);
}

float rayStrength(vec2 raySource, vec2 rayRefDirection, vec2 coord,
                  float seedA, float seedB, float speed) {
  vec2 sourceToCoord = coord - raySource;
  vec2 dirNorm = normalize(sourceToCoord);
  float cosAngle = dot(dirNorm, rayRefDirection);

  float distortedAngle = cosAngle + distortion * sin(iTime * 2.0 + length(sourceToCoord) * 0.01) * 0.2;
  
  float spreadFactor = pow(max(distortedAngle, 0.0), 1.0 / max(lightSpread, 0.001));

  float distance = length(sourceToCoord);
  float maxDistance = iResolution.x * rayLength;
  float lengthFalloff = clamp((maxDistance - distance) / maxDistance, 0.0, 1.0);
  
  float fadeFalloff = clamp((iResolution.x * fadeDistance - distance) / (iResolution.x * fadeDistance), 0.5, 1.0);
  float pulse = pulsating > 0.5 ? (0.8 + 0.2 * sin(iTime * speed * 3.0)) : 1.0;

  float baseStrength = clamp(
    (0.45 + 0.15 * sin(distortedAngle * seedA + iTime * speed)) +
    (0.3 + 0.2 * cos(-distortedAngle * seedB + iTime * speed)),
    0.0, 1.0
  );

  return baseStrength * lengthFalloff * fadeFalloff * spreadFactor * pulse;
}

void mainImage(out vec4 fragColor, in vec2 fragCoord) {
  vec2 coord = vec2(fragCoord.x, iResolution.y - fragCoord.y);
  
  vec2 finalRayDir = rayDir;
  if (mouseInfluence > 0.0) {
    vec2 mouseScreenPos = mousePos * iResolution.xy;
    vec2 mouseDirection = normalize(mouseScreenPos - rayPos);
    finalRayDir = normalize(mix(rayDir, mouseDirection, mouseInfluence));
  }

  vec4 rays1 = vec4(1.0) *
               rayStrength(rayPos, finalRayDir, coord, 36.2214, 21.11349,
                           1.5 * raysSpeed);
  vec4 rays2 = vec4(1.0) *
               rayStrength(rayPos, finalRayDir, coord, 22.3991, 18.0234,
                           1.1 * raysSpeed);

  fragColor = rays1 * 0.5 + rays2 * 0.4;

  if (noiseAmount > 0.0) {
    float n = noise(coord * 0.01 + iTime * 0.1);
    fragColor.rgb *= (1.0 - noiseAmount + noiseAmount * n);
  }

  float brightness = 1.0 - (coord.y / iResolution.y);
  fragColor.x *= 0.1 + brightness * 0.8;
  fragColor.y *= 0.3 + brightness * 0.6;
  fragColor.z *= 0.5 + brightness * 0.5;

  if (saturation != 1.0) {
    float gray = dot(fragColor.rgb, vec3(0.299, 0.587, 0.114));
    fragColor.rgb = mix(vec3(gray), fragColor.rgb, saturation);
  }

  fragColor.rgb *= raysColor;
}

void main() {
  vec4 color;
  mainImage(color, gl_FragCoord.xy);
  gl_FragColor  = color;
}
`;

function initLightRays(container, options = {}) {
  const raysOrigin = options.raysOrigin ?? 'top-center';
  const raysColor = options.raysColor ?? '#ffffff';
  const raysSpeed = options.raysSpeed ?? 1;
  const lightSpread = options.lightSpread ?? 1;
  const rayLength = options.rayLength ?? 2;
  const pulsating = options.pulsating ?? false;
  const fadeDistance = options.fadeDistance ?? 1.0;
  const saturation = options.saturation ?? 1.0;
  const followMouse = options.followMouse ?? true;
  const mouseInfluence = options.mouseInfluence ?? 0.1;
  const noiseAmount = options.noiseAmount ?? 0.0;
  const distortion = options.distortion ?? 0.0;

  const renderer = new Renderer({
    dpr: Math.min(window.devicePixelRatio || 1, 1.0),
    alpha: true
  });
  const gl = renderer.gl;
  const canvas = gl.canvas;

  canvas.style.width = '100%';
  canvas.style.height = '100%';
  canvas.style.display = 'block';
  canvas.style.position = 'absolute';
  canvas.style.top = '0';
  canvas.style.left = '0';
  canvas.style.zIndex = '0';
  canvas.style.pointerEvents = 'none';

  container.appendChild(canvas);

  const uniforms = {
    iTime: { value: 0 },
    iResolution: { value: [1, 1] },
    rayPos: { value: [0, 0] },
    rayDir: { value: [0, 1] },
    raysColor: { value: hexToRgb(raysColor) },
    raysSpeed: { value: raysSpeed },
    lightSpread: { value: lightSpread },
    rayLength: { value: rayLength },
    pulsating: { value: pulsating ? 1.0 : 0.0 },
    fadeDistance: { value: fadeDistance },
    saturation: { value: saturation },
    mousePos: { value: [0.5, 0.5] },
    mouseInfluence: { value: mouseInfluence },
    noiseAmount: { value: noiseAmount },
    distortion: { value: distortion }
  };

  const program = new Program(gl, {
    vertex: vert,
    fragment: frag,
    uniforms
  });

  const geometry = new Geometry(gl, {
    position: { size: 2, data: new Float32Array([-1, -1, 3, -1, -1, 3]) },
    uv: { size: 2, data: new Float32Array([0, 0, 2, 0, 0, 2]) }
  });

  const mesh = new Mesh(gl, { geometry, program });

  const updatePlacement = () => {
    const wCSS = container.clientWidth;
    const hCSS = container.clientHeight;
    renderer.setSize(wCSS, hCSS);

    const dpr = renderer.dpr;
    const w = wCSS * dpr;
    const h = hCSS * dpr;

    uniforms.iResolution.value = [w, h];

    const { anchor, dir } = getAnchorAndDir(raysOrigin, w, h);
    uniforms.rayPos.value = anchor;
    uniforms.rayDir.value = dir;
  };

  updatePlacement();
  window.addEventListener('resize', updatePlacement);

  const mouse = { x: 0.5, y: 0.5 };
  const smoothMouse = { x: 0.5, y: 0.5 };

  const handleMouseMove = e => {
    const rect = container.getBoundingClientRect();
    mouse.x = (e.clientX - rect.left) / rect.width;
    mouse.y = (e.clientY - rect.top) / rect.height;
  };

  if (followMouse) {
    window.addEventListener('mousemove', handleMouseMove);
  }

  let rafId = null;
  const loop = t => {
    rafId = requestAnimationFrame(loop);
    uniforms.iTime.value = t * 0.001;

    if (followMouse && mouseInfluence > 0.0) {
      const smoothing = 0.92;
      smoothMouse.x = smoothMouse.x * smoothing + mouse.x * (1 - smoothing);
      smoothMouse.y = smoothMouse.y * smoothing + mouse.y * (1 - smoothing);
      uniforms.mousePos.value = [smoothMouse.x, smoothMouse.y];
    }

    try {
      renderer.render({ scene: mesh });
    } catch (error) {
      console.warn('WebGL rendering error:', error);
    }
  };
  rafId = requestAnimationFrame(loop);

  return () => {
    if (rafId) cancelAnimationFrame(rafId);
    window.removeEventListener('resize', updatePlacement);
    if (followMouse) window.removeEventListener('mousemove', handleMouseMove);
    if (canvas.parentElement === container) {
      container.removeChild(canvas);
    }
    try {
      const loseContextExt = gl.getExtension('WEBGL_lose_context');
      if (loseContextExt) loseContextExt.loseContext();
    } catch (e) { }
    if (program && typeof program.remove === 'function') program.remove();
    if (geometry && typeof geometry.remove === 'function') geometry.remove();
    if (mesh && typeof mesh.remove === 'function') mesh.remove();
    if (renderer && typeof renderer.destroy === 'function') renderer.destroy();
  };
}

document.addEventListener('DOMContentLoaded', () => {
  // Initialize LightRays WebGL Background
  const bgContainer = document.getElementById('lightfall-bg');
  if (bgContainer) {
    initLightRays(bgContainer, {
      raysOrigin: 'top-center',
      raysColor: '#00ffff',      // Subtle cyan to match Shadow AI theme
      raysSpeed: 2.5,            // User requested speed
      lightSpread: 7.1,          // User requested spread above 2.5
      rayLength: 1.2,
      pulsating: true,           // User requested pulsating
      fadeDistance: 1.5,         // User requested fade distance
      followMouse: true,
      mouseInfluence: 0.2,
      noiseAmount: 0.5,          // User requested noise amount
      distortion: 0.05
    });
  }
  // 1. Interactive Variable Font Text Pressure Effect
  const pressureTitles = document.querySelectorAll(".text-pressure");
  if (pressureTitles.length > 0) {
    let mouse = {
      x: window.innerWidth / 2,
      y: window.innerHeight / 2
    };

    let cursor = {
      x: mouse.x,
      y: mouse.y
    };

    function distance(a, b) {
      const dx = b.x - a.x;
      const dy = b.y - a.y;
      return Math.sqrt(dx * dx + dy * dy);
    }

    function getAttr(distance, max, min, maxVal) {
      const value = maxVal - Math.abs((maxVal * distance) / max);
      return Math.max(min, value + min);
    }

    window.addEventListener("mousemove", (e) => {
      cursor.x = e.clientX;
      cursor.y = e.clientY;
    });

    window.addEventListener("touchmove", (e) => {
      if (e.touches && e.touches[0]) {
        cursor.x = e.touches[0].clientX;
        cursor.y = e.touches[0].clientY;
      }
    }, { passive: true });

    // Map each pressure title to its spans
    const titleData = Array.from(pressureTitles).map(title => {
      return {
        element: title,
        chars: Array.from(title.querySelectorAll("span"))
      };
    });

    function animatePressure() {
      mouse.x += (cursor.x - mouse.x) / 15;
      mouse.y += (cursor.y - mouse.y) / 15;

      titleData.forEach(({ element, chars }) => {
        const rect = element.getBoundingClientRect();
        const maxDistance = rect.width / 2;

        chars.forEach(char => {
          const r = char.getBoundingClientRect();
          const center = {
            x: r.left + r.width / 2,
            y: r.top + r.height / 2
          };

          const d = distance(mouse, center);

          const weight = Math.floor(
            getAttr(d, maxDistance, 100, 900)
          );

          const width = Math.floor(
            getAttr(d, maxDistance, 25, 151)
          );

          const italic = getAttr(
            d,
            maxDistance,
            0,
            1
          ).toFixed(2);

          char.style.fontVariationSettings = `"wght" ${weight}, "wdth" ${width}, "ital" ${italic}`;
        });
      });

      requestAnimationFrame(animatePressure);
    }

    animatePressure();
  }

  // 2. Scroll Reveal Animation with Staggered Grid Entrance
  const revealElements = document.querySelectorAll('.reveal');
  if (revealElements.length > 0) {
    const revealObserver = new IntersectionObserver((entries, observer) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          if (entry.target.classList.contains('feature-row-card') || entry.target.classList.contains('step-card')) {
            const grid = entry.target.parentElement;
            const cards = Array.from(grid.querySelectorAll('.reveal'));
            const idx = cards.indexOf(entry.target);
            setTimeout(() => {
              entry.target.classList.add('active');
            }, idx * 100);
          } else {
            entry.target.classList.add('active');
          }
          observer.unobserve(entry.target);
        }
      });
    }, { 
      threshold: 0.05,
      rootMargin: '0px 0px -50px 0px'
    });
    revealElements.forEach(el => revealObserver.observe(el));
  }

  // 3. Interactive Footer Wave Canvas Animation
  const footerCanvas = document.getElementById('footer-wave-canvas');
  if (footerCanvas) {
    const ctx = footerCanvas.getContext('2d');
    let animationFrameId;

    function resizeFooterCanvas() {
      footerCanvas.width = footerCanvas.parentElement.clientWidth || window.innerWidth;
      footerCanvas.height = footerCanvas.parentElement.clientHeight || 200;
    }
    
    window.addEventListener('resize', resizeFooterCanvas);
    resizeFooterCanvas();

    let phase = 0;
    function animateWaves() {
      ctx.clearRect(0, 0, footerCanvas.width, footerCanvas.height);

      // Gradient filled waves
      const waves = [
        { amplitude: 32, frequency: 0.003, speed: 0.015, color1: 'rgba(0, 210, 255, 0.25)', color2: 'rgba(0, 210, 255, 0.01)', stroke: '#00D2FF' },
        { amplitude: 22, frequency: 0.005, speed: -0.018, color1: 'rgba(0, 255, 204, 0.2)', color2: 'rgba(0, 255, 204, 0.01)', stroke: '#00FFCC' },
        { amplitude: 14, frequency: 0.002, speed: 0.008, color1: 'rgba(0, 230, 118, 0.15)', color2: 'rgba(0, 230, 118, 0.01)', stroke: '#00E676' }
      ];

      waves.forEach((w, idx) => {
        // Draw filled wave shape underneath
        ctx.beginPath();
        const grad = ctx.createLinearGradient(0, 0, 0, footerCanvas.height);
        grad.addColorStop(0, w.color1);
        grad.addColorStop(1, w.color2);
        ctx.fillStyle = grad;
        
        ctx.moveTo(0, footerCanvas.height);
        for (let x = 0; x <= footerCanvas.width; x += 6) {
          const y = footerCanvas.height / 2 + Math.sin(x * w.frequency + phase * w.speed) * w.amplitude;
          ctx.lineTo(x, y);
        }
        ctx.lineTo(footerCanvas.width, footerCanvas.height);
        ctx.closePath();
        ctx.fill();

        // Draw glowing neon stroke line on top
        ctx.beginPath();
        ctx.strokeStyle = w.stroke;
        ctx.lineWidth = idx === 0 ? 2 : 1.5;
        ctx.shadowColor = w.stroke;
        ctx.shadowBlur = 12;
        
        for (let x = 0; x <= footerCanvas.width; x += 6) {
          const y = footerCanvas.height / 2 + Math.sin(x * w.frequency + phase * w.speed) * w.amplitude;
          if (x === 0) {
            ctx.moveTo(x, y);
          } else {
            ctx.lineTo(x, y);
          }
        }
        ctx.stroke();
        ctx.shadowBlur = 0; // reset
      });

      phase += 1;
      animationFrameId = requestAnimationFrame(animateWaves);
    }
    animateWaves();
  }

  // 4. Ambient Background Audio Controller
  const audioBtn = document.getElementById('ambient-audio-toggle');
  const audio = document.getElementById('ambient-audio');

  if (audioBtn && audio) {
    // Attempt play on user interaction anywhere
    const startAudioOnInteraction = () => {
      audio.play().then(() => {
        audioBtn.classList.add('playing');
        document.removeEventListener('click', startAudioOnInteraction);
      }).catch(() => {
        // Autoplay blocked, wait for button click
      });
    };

    document.addEventListener('click', startAudioOnInteraction);

    audioBtn.addEventListener('click', (e) => {
      e.stopPropagation(); // prevent document click listener triggers
      if (audio.paused) {
        audio.play();
        audioBtn.classList.add('playing');
      } else {
        audio.pause();
        audioBtn.classList.remove('playing');
      }
    });
  }

  // 5. Interactive Comparison View Switching
  const compCards = document.querySelectorAll('.comp-card');
  const backNavBtn = document.getElementById('comparison-back-btn');
  const mainGrid = document.getElementById('comparison-main-grid');
  const tableWrappers = document.querySelectorAll('.comparison-table-wrapper');

  if (compCards.length > 0 && backNavBtn && mainGrid) {
    compCards.forEach(card => {
      card.addEventListener('click', () => {
        const targetId = card.getAttribute('data-target') + '-view';
        const targetView = document.getElementById(targetId);

        if (targetView) {
          // Hide main grid
          mainGrid.classList.remove('active-view');
          mainGrid.classList.add('hidden-view');

          // Show specific table
          targetView.classList.remove('hidden-view');
          targetView.classList.add('active-view');

          // Show back button
          backNavBtn.classList.remove('hidden-btn');

          // Scroll to the comparisons section header smoothly
          const compSection = document.getElementById('comparisons');
          if (compSection) {
            compSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
          }
        }
      });
    });

    backNavBtn.querySelector('.back-btn').addEventListener('click', () => {
      // Hide all tables
      tableWrappers.forEach(table => {
        table.classList.remove('active-view');
        table.classList.add('hidden-view');
      });

      // Show main grid
      mainGrid.classList.remove('hidden-view');
      mainGrid.classList.add('active-view');

      // Hide back button
      backNavBtn.classList.add('hidden-btn');

      // Scroll to the comparisons section header smoothly
      const compSection = document.getElementById('comparisons');
      if (compSection) {
        compSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    });
  }
});
