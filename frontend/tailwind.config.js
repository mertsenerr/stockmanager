/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        bg: 'var(--color-bg)',
        surface: 'var(--color-surface)',
        'surface-elevated': 'var(--color-surface-elevated)',
        ink: {
          DEFAULT: 'var(--color-ink)',
          secondary: 'var(--color-ink-secondary)',
          muted: 'var(--color-ink-muted)',
        },
        border: {
          DEFAULT: 'var(--color-border)',
          strong: 'var(--color-border-strong)',
        },
        text: {
          primary: 'var(--color-text-primary)',
          secondary: 'var(--color-text-secondary)',
          muted: 'var(--color-text-muted)',
        },
        coral: {
          DEFAULT: 'var(--color-coral)',
          hover: 'var(--color-coral-hover)',
        },
        lime: 'var(--color-lime)',
        butter: 'var(--color-butter)',
        cobalt: 'var(--color-cobalt)',
        mint: 'var(--color-mint)',
        violet: {
          DEFAULT: 'var(--color-violet, #8b5cf6)',
          strong: 'var(--color-violet-strong, #7c3aed)',
          soft: 'var(--color-violet-soft, rgba(139, 92, 246, 0.16))',
        },
        status: {
          blue:  'var(--color-status-blue, #3b82f6)',
          cyan:  'var(--color-status-cyan, #06b6d4)',
          green: 'var(--color-status-green, #22c55e)',
          pink:  'var(--color-status-pink, #ec4899)',
          amber: 'var(--color-status-amber, #f59e0b)',
        },
        accent: {
          success: 'var(--color-accent-success)',
          warning: 'var(--color-accent-warning)',
          danger: 'var(--color-accent-danger)',
          info: 'var(--color-accent-info)',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'Segoe UI', 'sans-serif'],
        mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'monospace'],
        // Theme refresh: serif/display utility'leri Inter'a düşürüldü — italic/serif kalkar.
        serif: ['Inter', 'system-ui', 'sans-serif'],
        display: ['Inter', 'system-ui', 'sans-serif'],
      },
      borderRadius: {
        sm: '6px',
        md: '10px',
        lg: '14px',
        xl: '18px',
      },
      boxShadow: {
        stamp: '4px 4px 0 var(--color-ink)',
        'stamp-sm': '2px 2px 0 var(--color-ink)',
        'stamp-lg': '6px 6px 0 var(--color-ink)',
        'stamp-coral': '4px 4px 0 var(--color-coral)',
      },
      transitionTimingFunction: {
        soft: 'cubic-bezier(0.16, 1, 0.3, 1)',
      },
      transitionDuration: {
        150: '150ms',
        200: '200ms',
      },
    },
  },
  plugins: [],
};
