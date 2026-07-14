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

# PineAI — Design Specification

## Overview
PineAI is a smart customer-management platform for Iranian online stores. The visual identity projects trustworthiness and growth through a professional green-based palette, high-contrast Persian typography, and a clean, performance-oriented layout.

### Guiding Principles
- **Clarity First**: Generous whitespace and high-contrast text ensure instant readability in RTL Persian.
- **Warm Professionalism**: Earthy greens and gold accents avoid SaaS sterility, projecting premium value.
- **Performance Driven**: Minimal asset weight, native Persian fonts, and zero-dependency CSS.

---

## Visual Language

### Color Palette
Rooted in **Forest Green** and **Airy Off-White**, the palette maintains a calm, natural canvas with professional depth.

- **Primary (`#1A5C3A`)**: Used for brand anchors, primary CTAs, and section tags.
- **Background (`#F6F9F7`)**: Slight green tint for a cohesive, natural page canvas.
- **Accent Gold (`#C9963B`)**: Reserved for high-value signals and premium badges.
- **Dark Surface (`#12302B`)**: Deep green for footer and high-contrast sections.

### Typography
Set in **Vazirmatn**, a variable-weight Persian font optimized for legibility.
- **Direction**: Global RTL (`direction: rtl`).
- **Scale**: Fluid headings (`clamp`) ensure responsiveness across mobile and desktop.
- **Weight**: 900 weight is used for hero numbers and headlines to signal authority.

---

## Component Guidelines

### Navigation & Header
- **Style**: Sticky, frosted-glass effect (`backdrop-filter: blur(8px)`) with white-96% fill.
- **Layout**: Logo on the right (RTL), navigation and CTA on the left.

### Interactive Elements
- **Buttons**:
  - **Primary**: Solid Forest Green fill, white text.
  - **Ghost**: Forest Green border/text, Light Green hover fill.
- **Radii**: `6px` (`sm`) for interactive elements; `20px` (`lg`) for containers.

### Content Containers
- **Feature Cards**: Off-white fill with subtle border, featuring icon containers in Light Green.
- **Stats Bar**: Full-width Forest Green band with white numbers and semi-transparent dividers.

### Depth & Elevation
- **Shadow Sm**: `0 2px 8px rgba(26,92,58,.08)` for headers and rest-state cards.
- **Shadow Md**: `0 6px 24px rgba(26,92,58,.12)` for hero visuals and hover states.
