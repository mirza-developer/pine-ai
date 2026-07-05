# PineAI.Landing — Design Document

## Overview

PineAI.Landing is a **Blazor WebAssembly** landing page for the PineAI platform — a smart customer-management solution for Iranian online stores. The UI is built entirely with a custom, lightweight stylesheet (`wwwroot/css/app.css`) with **no third-party CSS frameworks**.

### Key Design Principles

- **RTL-first** — all layouts are designed for right-to-left Persian text (`direction: rtl`)
- **Zero dependency** — no Bootstrap, Tailwind, or any other CSS framework
- **Persian typography** — Vazirmatn variable-weight font served locally via WOFF2
- **Green brand identity** — primary palette is pine/forest green with gold accents

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | Blazor WebAssembly (.NET 10) |
| Styling | Custom CSS (no external framework) |
| Font | Vazirmatn (local WOFF2) |
| Bundler | BuildBundlerMinifier (app.css → app.min.css) |
| Deployment | IIS (publish profile) / Static WebAssembly |

---

## Design Tokens (CSS Custom Properties)

All tokens are defined in `:root` inside `wwwroot/css/app.css`.

### Color Palette

| Token | Value | Usage |
|---|---|---|
| `--pine-bg` | `#F6F9F7` | Page background |
| `--pine-surface` | `#FFFFFF` | Cards, modals, form areas |
| `--pine-border` | `#D5E8DE` | Borders, dividers |
| `--pine-text` | `#1B2B27` | Primary body text |
| `--pine-text-muted` | `#4D6A62` | Secondary / subdued text |
| `--pine-primary` | `#1A5C3A` | Brand primary (dark green) |
| `--pine-primary-dk` | `#134428` | Hover state for primary |
| `--pine-primary-lt` | `#EAF5EE` | Light tint backgrounds |
| `--pine-accent` | `#41A870` | Accent green (check marks, icons) |
| `--pine-accent-lt` | `#D8F3DC` | Light accent tint |
| `--pine-gold` | `#C9963B` | Highlight badge, CTA accents |
| `--pine-gold-lt` | `#FEF3E2` | Light gold tint |
| `--pine-footer-bg` | `#12302B` | Footer background |
| `--pine-footer-text` | `#C5D8D3` | Footer body text |
| `--pine-footer-muted` | `#8AAEA6` | Footer subdued text |

### Shadows

| Token | Value |
|---|---|
| `--pine-shadow-sm` | `0 2px 8px rgba(26,92,58,.08)` |
| `--pine-shadow-md` | `0 6px 24px rgba(26,92,58,.12)` |

### Border Radius

| Token | Value | Usage |
|---|---|---|
| `--radius-sm` | `6px` | Inputs, small buttons, tags |
| `--radius-md` | `12px` | Chat bubbles, feature icons |
| `--radius-lg` | `20px` | Cards, form container, hero card |

### Layout

| Token | Value | Usage |
|---|---|---|
| `--container` | `1140px` | Maximum page-width container |

---

## Typography

**Font family:** `Vazirmatn, 'Segoe UI', Tahoma, Arial, sans-serif`

Vazirmatn is a variable-weight Persian font (`font-weight: 100 900`) loaded locally from `wwwroot/fonts/Vazirmatn.woff2` with `font-display: swap` for performance.

| Element | Size | Weight |
|---|---|---|
| Base body | `1rem` (16px) | — |
| Line height | `1.7` | — |
| Hero title | `clamp(1.9rem, 4vw, 2.8rem)` | 900 |
| Section h2 | `clamp(1.6rem, 3vw, 2.2rem)` | 800 |
| Why section h2 | `clamp(1.4rem, 2.8vw, 2rem)` | 800 |
| Feature card h3 | `1.15rem` | 700 |
| Logo text | `1.35rem` | 800 |
| Section tag | `0.8rem` | 700 (uppercase, tracked) |
| Stat numbers | `1.8rem` | 900 |
| Footer logo | `1.4rem` | 900 |

---

## Page Layout

### Container

`.container` constrains content to `1140px` with `padding: 0 1.25rem` for edge bleed on small screens.

### Page Sections

| CSS Class | Background | Padding |
|---|---|---|
| `.hero` | Gradient (`--pine-primary-lt` → `--pine-bg`) | `5rem 0 4rem` |
| `.stats-bar` | `--pine-primary` (dark green) | `2rem 0` |
| `.features` | `--pine-surface` (white) | via `.section` |
| `.how-section` | `--pine-bg` | via `.section` |
| `.why-section` | `--pine-surface` | via `.section` |
| `.contact-section` | `--pine-bg` | via `.section` |
| `.site-footer` | `--pine-footer-bg` (deep green) | `3.5rem 0 2.5rem` |

Generic `.section` padding: `5rem 0`

---

## Components

### Buttons (`.btn`)

Three variants, all using `border-radius: var(--radius-sm)` and `padding: .75rem 1.75rem`:

| Class | Background | Text | Border |
|---|---|---|---|
| `.btn-primary` | `--pine-primary` | White | `--pine-primary` |
| `.btn-ghost` | Transparent | `--pine-primary` | `--pine-primary` |
| `.btn:disabled` | — | — | `opacity: 0.6` |

Hover: primary darkens to `--pine-primary-dk`; ghost fills `--pine-primary-lt`.

---

### Header (`.site-header`)

- Sticky, `z-index: 100`, height `64px`
- Frosted glass effect: `background: rgba(255,255,255,.96)` + `backdrop-filter: blur(8px)`
- Border bottom: `1px solid --pine-border`
- Contains: logo, desktop nav (`display: none` below 640px), hamburger toggle (visible below 640px)
- Mobile nav (`.mobile-nav`) slides in as block when `.is-open` is applied

---

### Feature Cards (`.feature-card`)

Three-column grid (`gap: 1.5rem`), collapses to single column below 900px.

- Default: `background: --pine-bg`, `border-radius: var(--radius-lg)`, `padding: 2rem`
- Accent (`.feature-card-accent`): `background: --pine-primary`, white text — used for the "popular" card
- Hover: lifts `2px` with `--pine-shadow-md`
- Feature icon: `52×52px` box, `border-radius: var(--radius-md)`, `--pine-primary-lt` background
- Gold badge (`.card-badge`): absolute, centered at top edge, `background: --pine-gold`

---

### Hero Card (`.hero-card`)

Decorative chat-preview card in the hero section:

- White surface, `border-radius: var(--radius-lg)`, `--pine-shadow-md`
- Max width `360px`
- Chat bubbles: `.customer` aligns right with `--pine-primary-lt` background; `.bot` aligns left with white surface + border

---

### Stats Bar (`.stats-bar`)

Full-width dark green band with white text. Responsive: `.stat-divider` hides below 640px.

---

### Contact Form (`.contact-form-wrap`)

- Max width `620px`, centered
- White surface card, `border-radius: var(--radius-lg)`, `padding: 2.5rem`
- Two-column grid for name/phone fields, collapses to single column below 640px
- Input focus: border changes to `--pine-primary`; error state: `#E53935`
- Inline validation with `.field-error` messages and `.input-error` class on inputs

---

### Footer (`.site-footer`)

Three-column grid (`1.5fr 1fr 1fr`) collapsing to `1fr 1fr` at 900px and `1fr` at 640px. Footer brand spans full width below 900px.

---

## Responsive Breakpoints

| Breakpoint | Changes |
|---|---|
| `≤ 900px` | Hero collapses to 1 col, hero visual hidden; features 1 col; why-section 1 col, visual hidden; footer 2-col |
| `≤ 640px` | Section padding reduces to `3.5rem 0`; hero padding `3rem 0`; stat dividers hidden; desktop nav hidden, hamburger shown; form columns collapse; footer 1-col; buttons full-width |

---

## Page Sections (Home.razor)

| # | Section ID | Heading |
|---|---|---|
| 1 | — | Hero — مدیریت هوشمند مشتریان |
| 2 | — | Stats bar — key metrics |
| 3 | `#features` | همه‌چیز برای رشد فروشگاه شما |
| 4 | `#how-it-works` | راه‌اندازی سریع، نتیجه فوری |
| 5 | — | Why PineAI |
| 6 | `#contact` | دمو رایگان درخواست کنید |

---

## Services & Configuration

### `SiteSettings`

Populated from `appsettings.json` under the `"Site"` key and injected as a singleton:

| Property | Default | Purpose |
|---|---|---|
| `Name` | `پاین‌ای` | Persian brand name |
| `NameEn` | `PineAI` | English brand name |
| `Tagline` | `پلتفرم هوشمند مدیریت مشتریان` | Site tagline |
| `Domain` | `https://pineai.ir` | Canonical URL |
| `ContactApiUrl` | — | Endpoint for contact form POST |
| `BaleUrl` | — | Bale messenger link |
| `InstagramUrl` | — | Instagram profile link |
| `Phone` | — | Contact phone |
| `Email` | — | Contact email |

### `ContactService`

Submits the contact form (`ContactFormDto`: Name, Phone, Website, Description) to `ContactApiUrl` via an `HttpClient` pre-configured with `X-Api-Key` authentication.

---

## Asset Pipeline

| Source | Output | Tool |
|---|---|---|
| `wwwroot/css/app.css` | `wwwroot/css/app.min.css` | BuildBundlerMinifier |
| `wwwroot/fonts/Vazirmatn.woff2` | Served as-is | — |

`bundleconfig.json` configures the minification. The minified file is referenced in production.
