---
name: PineAI Management Panel
colors:
  sidebar-bg: "#12302B"
  sidebar-text: "#C5D8D3"
  sidebar-accent: "#41A870"
  sidebar-active-bg: "rgba(65, 168, 112, 0.12)"
  topbar-bg: "rgba(255, 255, 255, 0.96)"
  background: "#F6F9F7"
  surface: "#FFFFFF"
  border: "#D5E8DE"
  primary: "#1A5C3A"
  primary-dark: "#134428"
  text: "#1B2B27"
  text-muted: "#4D6A62"
  accent-gold: "#C9963B"
  success: "#41A870"
  warning: "#C9963B"
  danger: "#E53935"
typography:
  fontFamily: "Vazirmatn, system-ui, sans-serif"
  direction: "rtl"
  baseSize: "14px"
  headings:
    weight: 800
    color: "#1B2B27"
rounded:
  sm: "6px"
  md: "12px"
  lg: "16px"
shadows:
  card: "0 2px 8px rgba(26, 92, 58, 0.08)"
  card-hover: "0 6px 24px rgba(26, 92, 58, 0.12)"
---

# PineAI Management Panel — Design Specification

## Overview
The PineAI Management Panel is the internal administrative interface for managing Iranian e-commerce customers. It adapts the PineAI brand identity into a high-density, professional dashboard environment that balances data clarity with a modern, organic aesthetic.

## Visual Language

### Color Palette
The panel shifts from generic navy to the brand's signature **Deep Pine** and **Forest Green** palette.

- **Sidebar**: Uses `--pine-footer-bg` (#12302B) for a deep, focused navigation area. Active items use a subtle Forest Green tint.
- **Surface**: Pure white cards sit on the `--pine-bg` (#F6F9F7) canvas, providing high contrast for data readability.
- **Accents**: Gold is used sparingly for high-value alerts or "Coming Soon" indicators.

### Modernization Adjustments
- **Frosted Glass**: The top navigation bar uses a frosted-glass effect (`backdrop-filter: blur(12px)`) to feel lighter and more integrated.
- **Elevation**: Cards use soft, green-tinted shadows instead of neutral grays to maintain palette harmony.
- **Interactions**: Interactive elements use a subtle `2px` lift and shadow expansion on hover.

## Component Guidelines

### Sidebar (Navigation)
- **Background**: Deep Pine Green (#12302B).
- **Active State**: Forest Green (#41A870) left-side border (RTL) and a 12% opacity background wash.
- **Typography**: 0.88rem weight 500 for nav links.

### Data Tables & Cards
- **Cards**: Border radius of 12px with a 1px border (#D5E8DE). 
- **Tables**: Clean, borderless rows with a subtle background tint on hover. Headers are set in the brand's muted text color.

### Form Elements
- **Inputs**: 6px border radius, transitioning to a Forest Green border on focus.
- **Buttons**: Rounded-sm (6px). Primary actions use the solid brand green; secondary actions use the ghost variant with a thin border.

## Layout
- **Sidebar Width**: 260px (Desktop).
- **Content Padding**: 2rem (32px) standard padding for page containers.
- **Grid**: Uses an 8-point spacing system for all component margins and gaps.
