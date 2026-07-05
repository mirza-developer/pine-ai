---
name: PineAI
colors:
  background: "#F6F9F7"
  surface: "#FFFFFF"
  border: "#D5E8DE"
  text: "#1B2B27"
  text-muted: "#4D6A62"
  primary: "#1A5C3A"
  primary-dark: "#134428"
  primary-light: "#EAF5EE"
  accent: "#41A870"
  accent-light: "#D8F3DC"
  gold: "#C9963B"
  gold-light: "#FEF3E2"
  footer-bg: "#12302B"
  footer-text: "#C5D8D3"
  footer-muted: "#8AAEA6"
  white: "#FFFFFF"
typography:
  h1:
    fontFamily: Vazirmatn
    fontSize: clamp(1.9rem, 4vw, 2.8rem)
    fontWeight: 900
    lineHeight: 1.25
  h2:
    fontFamily: Vazirmatn
    fontSize: clamp(1.6rem, 3vw, 2.2rem)
    fontWeight: 800
    lineHeight: 1.3
  h3:
    fontFamily: Vazirmatn
    fontSize: 1.15rem
    fontWeight: 700
    lineHeight: 1.4
  body-lg:
    fontFamily: Vazirmatn
    fontSize: 1.1rem
    lineHeight: 1.8
  body-md:
    fontFamily: Vazirmatn
    fontSize: 1rem
    lineHeight: 1.7
  body-sm:
    fontFamily: Vazirmatn
    fontSize: 0.92rem
    lineHeight: 1.7
  label:
    fontFamily: Vazirmatn
    fontSize: 0.85rem
    fontWeight: 700
  caption:
    fontFamily: Vazirmatn
    fontSize: 0.8rem
    fontWeight: 500
  stat:
    fontFamily: Vazirmatn
    fontSize: 1.8rem
    fontWeight: 900
    lineHeight: 1
rounded:
  sm: 6px
  md: 12px
  lg: 20px
  pill: 2rem
spacing:
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 32px
  xxl: 48px
  section: 80px
---

## Overview

PineAI is a smart customer-management platform for Iranian online stores (فروشگاه‌های آنلاین). The visual identity combines **deep pine green** with a clean, airy off-white background to project trustworthiness, professionalism, and technology-forward confidence — qualities that resonate with small-to-medium e-commerce merchants in Iran.

The design system is built with three guiding principles:

- **Clarity first** — generous whitespace, high contrast text, and a minimal component vocabulary ensure instant readability in Persian (RTL).
- **Warm professionalism** — the earthy green palette avoids the cold sterility typical of B2B SaaS; a warm gold accent communicates premium value without ostentation.
- **Performance over decoration** — no third-party CSS frameworks, a single variable-weight Persian web font, and inline SVG icons keep the landing page lightweight.

---

## Colors

The palette is rooted in a **forest-green primary** paired with an off-white background for a natural, calm canvas. A single warm gold accent is reserved for high-value call-to-action signals.

### Primary Scale

- **Primary (`#1A5C3A`)** — The brand anchor color. Used for primary buttons, logo text, active navigation links, section tags, and feature icons. Evokes reliability and growth.
- **Primary Dark (`#134428`)** — Hover and pressed state for primary interactive elements. Ensures WCAG AA contrast on white surfaces.
- **Primary Light (`#EAF5EE`)** — Subtle tint for hover backgrounds, section tags, feature card backgrounds, and customer chat bubbles.

### Accent

- **Accent (`#41A870`)** — A brighter, livelier green for highlights, decorative dots, and secondary emphasis.
- **Accent Light (`#D8F3DC`)** — Background wash for accent-adjacent decorative elements.

### Neutrals

- **Background (`#F6F9F7`)** — Page canvas. The slight green tint keeps the palette cohesive.
- **Surface (`#FFFFFF`)** — Cards, modals, and elevated containers.
- **Border (`#D5E8DE`)** — Subtle dividers and card outlines.
- **Text (`#1B2B27`)** — Near-black with a green undertone. Used for all body copy and headings.
- **Text Muted (`#4D6A62`)** — Secondary text, descriptions, and captions.

### Gold Accent

- **Gold (`#C9963B`)** — High-value callouts, premium badges, and "coming soon" tags.
- **Gold Light (`#FEF3E2`)** — Background fill for gold-tinted badges.

### Footer / Dark Surface

- **Footer BG (`#12302B`)** — Dark deep green for the footer.
- **Footer Text (`#C5D8D3`)** — Primary footer text.
- **Footer Muted (`#8AAEA6`)** — Secondary footer links and copyright info.

---

## Typography

All text is set in **Vazirmatn**, a variable-weight Persian web font. The layout direction is **RTL** (`direction: rtl`).

### Guidance

- **Fluid Type**: Headings use `clamp()` to ensure responsiveness across breakpoints.
- **Visual Weight**: Font-weight 900 is used for hero headlines and stats to maximize impact.
- **Readability**: Minimum font size is 0.8rem for mobile legibility.

---

## Spacing & Radii

### Spacing
A base-8 scale is used for consistency. Standard vertical section padding is 80px (`section`).

### Radii
- **Interactive Elements**: `sm` (6px) for buttons and inputs.
- **Content Containers**: `lg` (20px) for cards and hero visuals.
- **Badges/Labels**: `pill` (2rem) for a friendly, approachable feel.

---

## Depth / Elevation

- **shadow-sm**: `0 2px 8px rgba(26,92,58,.08)` — Sticky header and rest state cards.
- **shadow-md**: `0 6px 24px rgba(26,92,58,.12)` — Hero visual and hover states.

---

## Components

### Site Header
Sticky, frosted-glass (`backdrop-filter: blur(8px)`) with white-96% background. Logo on the right (RTL), navigation and CTA on the left.

### Buttons
- **Primary**: Solid `--primary`, white text.
- **Ghost**: `--primary` border and text, `--primary-light` hover background.

### Feature Card
`--background` fill with `--border` outline and `--lg` corners. Icons sit in a `--primary-light` container.

### Stats Bar
Full-width `--primary` band with white numbers and semi-transparent dividers.