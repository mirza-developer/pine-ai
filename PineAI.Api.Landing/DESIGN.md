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

The landing page (`PineAI.Landing`) is a Blazor WebAssembly application served at **pineai.ir**. The `PineAI.Api.Landing` project is the companion ASP.NET Core 10 backend that processes the contact form and forwards submissions to a Bale Messenger chat via a secured API-key scheme.

---

## Colors

The palette is rooted in a **forest-green primary** paired with an off-white background for a natural, calm canvas. A single warm gold accent is reserved for high-value call-to-action signals.

### Primary Scale

- **Primary (`#1A5C3A`)** — The brand anchor color. Used for primary buttons, logo text, active navigation links, section tags, and feature icons. Evokes reliability and growth.
- **Primary Dark (`#134428`)** — Hover and pressed state for primary interactive elements. Ensures WCAG AA contrast on white surfaces.
- **Primary Light (`#EAF5EE`)** — Subtle tint for hover backgrounds, section tags, feature card backgrounds, and customer chat bubbles. Never used for text.

### Accent

- **Accent (`#41A870`)** — A brighter, livelier green for highlights, decorative dots, and secondary emphasis within dark backgrounds (e.g., the stats bar).
- **Accent Light (`#D8F3DC`)** — Background wash for accent-adjacent decorative elements.

### Neutrals

- **Background (`#F6F9F7`)** — Page canvas. The slight green tint keeps the palette cohesive.
- **Surface (`#FFFFFF`)** — Cards, modals, and elevated containers. Pure white provides maximum contrast with the background.
- **Border (`#D5E8DE`)** — Subtle dividers, card outlines, and separator lines. Avoids hard-grey borders that clash with the warm palette.
- **Text (`#1B2B27`)** — Near-black with a green undertone. Used for all body copy and headings.
- **Text Muted (`#4D6A62`)** — Secondary text, descriptions, captions, and icon labels.

### Gold Accent

- **Gold (`#C9963B`)** — Hero badge, premium badges, and "coming soon" tags. Signals special status without overuse.
- **Gold Light (`#FEF3E2`)** — Background fill for gold-tinted badges and callouts.

### Footer / Dark Surface

- **Footer BG (`#12302B`)** — Dark footer section; a near-black deep green.
- **Footer Text (`#C5D8D3`)** — Readable body text on the dark footer.
- **Footer Muted (`#8AAEA6`)** — Secondary links and copyright text within the footer.

---

## Typography

All text is set in **Vazirmatn**, a variable-weight Persian web font loaded via `@font-face` from a local `woff2` file. The font covers Arabic numerals and Latin characters, making it suitable for mixed Persian/English content common in the product.

The layout direction is **RTL** (`direction: rtl`) applied globally on `<body>`.

### Scale

| Token | Size | Weight | Line-height | Usage |
|---|---|---|---|---|
| `h1` | clamp(1.9rem, 4vw, 2.8rem) | 900 | 1.25 | Hero section title |
| `h2` | clamp(1.6rem, 3vw, 2.2rem) | 800 | 1.3 | Section headings |
| `h3` | 1.15rem | 700 | 1.4 | Feature card titles |
| `stat` | 1.8rem | 900 | 1 | Stats bar numbers |
| `body-lg` | 1.1rem | 400 | 1.8 | Hero description |
| `body-md` | 1rem | 400 | 1.7 | Default body text |
| `body-sm` | 0.92rem | 400 | 1.7 | Feature card body |
| `label` | 0.85rem | 700 | — | Hero badge, section tag |
| `caption` | 0.8rem | 500 | — | Stat labels, card sub-labels |

### Guidance

- **Never use fewer than 0.8rem** for any visible text to preserve legibility on mobile screens.
- Fluid type (`clamp()`) is preferred for headings to avoid abrupt layout shifts across breakpoints.
- **Font-weight 900** is reserved for hero and stats — it signals the most important numeric or headline message at a glance.
- Logo text uses `font-size: 1.35rem; font-weight: 800` and is colored `--pine-primary`.

---

## Spacing

A base-8 spacing scale is used throughout. Section-level vertical rhythm uses an 80 px (`5rem`) top and bottom padding as the standard unit.

| Token | Value | Usage |
|---|---|---|
| `xs` | 4 px | Inline gaps, badge padding |
| `sm` | 8 px | Button icon gaps, small padding |
| `md` | 16 px | Card inner rhythm, grid gaps |
| `lg` | 24 px | Stats bar gap, header nav gap |
| `xl` | 32 px | Hero actions gap, section margins |
| `xxl` | 48 px | Large decorative gaps |
| `section` | 80 px | Top/bottom padding for all page sections |

**Container max-width:** `1140 px` with `1.25 rem` horizontal padding on smaller viewports.

**Grid layouts:**
- Features: `repeat(3, 1fr)` with `1.5 rem` gap, collapses to single column on mobile.
- Hero: `1fr 1fr` two-column grid with `3 rem` gap, collapses to single column on mobile.

---

## Radii

Border radius tokens define the overall softness of the visual language. PineAI uses a **medium-soft** rounding — enough friendliness for consumer-facing UX, but not so round as to feel toy-like for a B2B product.

| Token | Value | Usage |
|---|---|---|
| `sm` | 6 px | Buttons, navigation links, code tags, small badges |
| `md` | 12 px | Chat bubbles, feature icons, input fields |
| `lg` | 20 px | Feature cards, hero visual card, modals |
| `pill` | 2 rem | Section tags, hero badge, stat badge, full-round labels |
| `circle` | 50 % | Avatar dots, status indicators, spinner |

**Rule:** Interactive elements (buttons, links) use `--radius-sm`. Content containers (cards, panels) use `--radius-lg`. Typographic labels and badges use `--radius-pill`.

---

## Depth / Elevation

Shadows use a green-tinted drop shadow to stay within the brand palette. Only two elevation levels are defined, keeping the visual hierarchy simple.

| Token | Value | Usage |
|---|---|---|
| `shadow-sm` | `0 2px 8px rgba(26,92,58,.08)` | Site header (sticky), default card rest state |
| `shadow-md` | `0 6px 24px rgba(26,92,58,.12)` | Hero visual card, feature card hover state |

**Elevation rules:**
- The sticky site header always carries `shadow-sm` to separate it from page content on scroll.
- Cards animate from no shadow (rest) to `shadow-md` on hover, accompanied by a subtle `translateY(-2px)` lift.
- Modals and overlays may use `shadow-md` plus a semi-transparent backdrop (`rgba(0,0,0,.4)`).
- Avoid mixing white box-shadows with the green-tinted ones — consistency is required.

---

## Components

### Site Header
A sticky, frosted-glass navigation bar (`height: 64px`) using `backdrop-filter: blur(8px)` with a white-96% background. Contains the logo (left in RTL flow = right side visually), navigation links, and a primary CTA button. Collapses into a hamburger toggle on mobile.

### Buttons
Two variants:
- **Primary** — Solid `--pine-primary` fill with white text. Hover darkens to `--pine-primary-dk`. Used for main CTAs ("درخواست دمو رایگان").
- **Ghost** — Transparent background with `--pine-primary` border and text. Hover fills with `--pine-primary-light`. Used for secondary actions ("بیشتر بدانید").

Both share `padding: .75rem 1.75rem`, `border-radius: --radius-sm`, `font-weight: 600`, `font-size: 1rem`.

### Section Tag
A pill-shaped label above section headings. Background: `--pine-primary-light`, color: `--pine-primary`, small caps, 0.8rem font, letter-spacing 0.04em.

### Feature Card
A `--pine-bg` filled card with `--pine-border` outline and `--radius-lg` corners. Icon container (`52×52px`) at top left uses `--pine-primary-light` background. An accent variant (`.feature-card-accent`) uses a full `--pine-primary` fill for visual variety.

### Stats Bar
A full-width `--pine-primary` band between the hero and features sections. Displays 2–4 key metrics side-by-side in white text with thin white semi-transparent vertical dividers.

### Hero Chat Card
A decorative mock-chat card (not interactive) showing an AI support conversation snippet. Uses `--pine-surface` background, `--radius-lg`, `shadow-md`. Customer messages align right with `--pine-primary-light` fill; bot messages align left with a border outline.

### Contact Form
Inputs use `--pine-border` outline, `--radius-sm`, and transition to `--pine-primary` on focus. The submit button is a full-width primary button. Validation errors display inline below the relevant field.

---

## Iconography

All icons are **inline SVG** — no icon font library is used. This keeps the HTTP footprint minimal and allows color to inherit from `currentColor`.

**Style rules:**
- Stroke-based line icons (`stroke-width: 1.8`, `stroke-linecap: round`, `stroke-linejoin: round`, `fill: none`).
- Rendered at `28×28 px` inside feature icon containers; `20×20 px` for inline/navigation usage.
- Color inherits from the parent element so icons automatically adapt to light and dark contexts (e.g., white on the accent feature card).

**Emoji / Unicode icons:**
- Decorative emojis are used in Bale bot message content (📋, 👤, 📞, 🌐, 💬) and are not part of the visual UI icon system.

**Favicon:** A `favicon.png` file; should remain consistent with the pine-green brand color.

---

## Examples

### Color Usage in Context

```
Hero section:  background gradient: --pine-primary-light → --pine-bg
               H1 color: --pine-text
               H1 accent span: --pine-primary
               Body copy: --pine-text-muted

Stats bar:     background: --pine-primary
               Numbers: #fff (weight 900)
               Labels: rgba(255,255,255,.75)

Feature cards: background: --pine-bg
               Border: --pine-border
               Icon box: --pine-primary-light / color: --pine-primary
               Accent card: background: --pine-primary / text: #fff

Footer:        background: --footer-bg (#12302B)
               Body text: --footer-text
               Secondary: --footer-muted
```

### Button States

```
Primary (rest):  bg #1A5C3A | text #fff | border #1A5C3A
Primary (hover): bg #134428 | text #fff | border #134428
Primary (disabled): opacity 0.6, pointer-events none

Ghost (rest):    bg transparent | text #1A5C3A | border #1A5C3A
Ghost (hover):   bg #EAF5EE    | text #1A5C3A | border #1A5C3A
```

### Responsive Breakpoints

```
≥ 1140px  — full container width, multi-column grids
≤  768px  — single-column layout, hamburger nav visible
           - hero: stacked (content above, visual below)
           - features: 1-column card list
```

### Contact Form → API Flow

```
PineAI.Landing (Blazor WASM)
  └─► POST /api/contact { name, phone, website?, description }
        X-Api-Key: <configured secret>
        ↓
      PineAI.Api.Landing (ASP.NET Core 10)
        └─► Validates API key (ApiKeyAuthenticationHandler)
        └─► Forwards formatted Persian-language message
              to Bale Messenger via tapi.bale.ai/bot{token}/sendMessage
```
