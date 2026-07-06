---
name: PineAI.UI
framework: Bootstrap 5 + Blazor Server
icons: Bootstrap Icons (bi-*)
colors:
  sidebar-from: "#05276b"
  sidebar-mid: "#0d1f4e"
  sidebar-to: "#2d0540"
  sidebar-accent: "#7fffb2"
  sidebar-text: "rgba(255,255,255,0.72)"
  sidebar-text-active: "#ffffff"
  sidebar-border: "rgba(255,255,255,0.08)"
  top-bar-bg: "#ffffff"
  top-bar-border: "#e8e8e8"
  page-bg: Bootstrap default (white / #f8f9fa for chat area)
  primary: "#1b6ec2" (Bootstrap override) / "#0d6efd" (Bootstrap default)
  danger: "#dc3545"
  success: "#28a745"
  warning: "#ffc107"
  info: "#17a2b8"
typography:
  base:
    fontFamily: "'Tahoma', 'Arial', sans-serif"
    fontSize: 14px (mobile), 15px (≥768px)
    direction: rtl
    textAlign: right
  label:
    fontWeight: 600 (fw-semibold)
  brand-name:
    fontSize: 1.05rem
    fontWeight: 700
  brand-tagline:
    fontSize: 0.68rem
  nav-link:
    fontSize: 0.88rem
  nav-section-label:
    fontSize: 0.68rem
    fontWeight: 700
    textTransform: uppercase
rounded:
  card: 0.75rem
  button: 0.5rem
  datepicker-popup: 0.5rem
  notification: 0.5rem
  toggler-btn: 0.4rem
  datepicker-cell: 0.3rem
  reconnect-modal: 0.5rem
spacing:
  sidebar-width-desktop: 240px
  sidebar-width-mobile: 270px
  top-bar-height: 3.5rem
  sidebar-brand-height: 4rem
  page-padding: "px-3 px-md-4"
shadows:
  card-hover: "0 .25rem 1rem rgba(0,0,0,.12)"
  top-bar: "0 1px 4px rgba(0,0,0,.05)"
  datepicker: "0 4px 16px rgba(0,0,0,.15)"
  notification: "0 4px 12px rgba(0,0,0,.15)"
  sidebar-mobile: "-6px 0 28px rgba(0,0,0,.4)"
---

# PineAI.UI — Design Specification

## Overview
PineAI.UI is the internal management panel for PineAI, built as a **Blazor Server** application.
The interface is fully RTL Persian, uses **Bootstrap 5** for the base component system, and **Bootstrap Icons** for iconography. It does not use any additional CSS frameworks beyond Bootstrap and a small `app.css` that adds custom layout, notification, and datepicker styles.

### Guiding Principles
- **Bootstrap-first**: Every component relies on Bootstrap utilities and classes. Custom CSS is minimal and additive.
- **RTL Native**: `direction: rtl` and `text-align: right` are declared globally; Bootstrap's RTL build is used throughout.
- **Admin Clarity**: Dense, data-oriented layouts (tables, cards, two-column panels) prioritize information density over marketing aesthetics.
- **Dark Sidebar / Light Content**: High contrast between the deep navy-purple sidebar and the white main area creates clear structural separation.

---

## Visual Language

### Color Palette

#### Sidebar
The sidebar uses a **diagonal gradient** from deep blue to dark purple, giving a rich, admin-panel feel.
- `#05276b` → `#0d1f4e` → `#2d0540` (gradient: 160deg)
- **Active accent**: `#7fffb2` (bright mint green) — used for the active nav item's right-side border and icon tint.
- Text: `rgba(255,255,255,0.72)` at rest; `#fff` on active/hover.

#### Main Content Area
- Background: Bootstrap default white / light gray (`#f8f9fa` for elevated containers).
- Top bar: white `#ffffff` with `#e8e8e8` bottom border.
- Cards: white `#ffffff` with Bootstrap's standard border.

#### Semantic Colors (Bootstrap defaults)
| Token | Value | Usage |
|---|---|---|
| `primary` | `#1b6ec2` / `#0d6efd` | Primary buttons, active states, links |
| `success` | `#28a745` | Create/add actions, success badges |
| `warning` | `#ffc107` | Edit actions, caution states |
| `danger` | `#dc3545` | Delete actions, error states |
| `info` | `#17a2b8` | Informational alerts |
| `secondary` | Bootstrap default | Secondary buttons, muted text |

#### Notification Colors
| Level | Background | Border (right) | Text |
|---|---|---|---|
| success | `#d4edda` | `#28a745` | `#155724` |
| error | `#f8d7da` | `#dc3545` | `#721c24` |
| warning | `#fff3cd` | `#ffc107` | `#856404` |
| information | `#d1ecf1` | `#17a2b8` | `#0c5460` |

---

## Layout

### Page Structure
```
┌─────────────────────────────────────────────┐
│  Sidebar (240px sticky)  │  Main Area        │
│  ┌──────────────────────┐│  ┌─────────────┐ │
│  │ Brand (4rem)         ││  │ Top bar     │ │
│  ├──────────────────────┤│  │ (3.5rem)    │ │
│  │ NavMenu (scrollable) ││  ├─────────────┤ │
│  │                      ││  │ article     │ │
│  │                      ││  │ .content    │ │
│  ├──────────────────────┤│  │             │ │
│  │ Sidebar Footer       ││  └─────────────┘ │
│  │ (user + logout)      ││                  │
│  └──────────────────────┘│                  │
└─────────────────────────────────────────────┘
```

### Responsive Behavior
- **Desktop (≥768px)**: `flex-direction: row`; sidebar is sticky, 240px wide, full viewport height.
- **Mobile (<768px)**: Full-screen layout; sidebar is a **fixed overlay drawer** (270px) that slides in from the right edge (RTL). A semi-transparent blurred backdrop (`rgba(0,0,0,0.5)`) covers the content when the drawer is open. The hamburger button in the top bar opens/closes the drawer.

---

## Component Guidelines

### Sidebar
- **Background**: `linear-gradient(160deg, #05276b 0%, #0d1f4e 50%, #2d0540 100%)`
- **Brand area**: 4rem height, bottom border `rgba(255,255,255,0.08)`. Shows app name, tagline, and close button (mobile only).
- **Brand icon**: `bi-tree-fill` in `#7fffb2`.
- **Toggler button** (hamburger/close): 2.2×2.2rem, semi-transparent white background (`rgba(255,255,255,0.08)`), border `rgba(255,255,255,0.15)`, radius `0.4rem`.
- **Footer**: User avatar (`bi-person-circle`) + Persian name + logout button. Logout button uses red tint: `rgba(220,53,69,0.1)` background, `rgba(255,120,130,0.9)` text, `0.5rem` radius.

### Navigation Menu
- **Section labels**: `0.68rem`, uppercase, `letter-spacing: .06em`, `rgba(255,255,255,0.35)`.
- **Nav links**: `0.88rem`, `rgba(255,255,255,0.72)`, rounded `0.5rem`, transition background/color.
- **Active state**: `rgba(255,255,255,0.18)` background, white text, bold weight, `3px solid #7fffb2` inline-end (right) border.
- **Icons**: Bootstrap Icons, `1rem`, `width: 1.1rem`, fixed width for alignment.

### Top Bar
- Height: `3.5rem`. White background. Bottom border `#e8e8e8`. Shadow `0 1px 4px rgba(0,0,0,.05)`.
- Contains: hamburger button (mobile), business name (mobile), logged-in user name (desktop), logout button (desktop).
- Sticky on desktop (`position: sticky; top: 0; z-index: 10`).

### Cards
- Bootstrap `.card` with `border-radius: 0.75rem`.
- `.card.shadow-sm:hover` transitions to `0 .25rem 1rem rgba(0,0,0,.12)` with `.2s ease`.
- Page content cards use `.card.shadow-sm` with `.card-body.p-4`.
- Table cards omit `p-4` and use `.table-responsive` directly inside.

### Buttons
- Global `border-radius: 0.5rem` overrides Bootstrap default.
- **Primary**: `#1b6ec2` background, white text (Bootstrap `.btn-primary` override).
- **Success (add/create)**: Bootstrap `.btn-success` — used consistently for add/create actions.
- **Warning (edit)**: Bootstrap `.btn-warning` — used for edit actions.
- **Danger (delete)**: Bootstrap `.btn-danger` / `.btn-outline-danger` — used for delete confirmations.
- **Secondary (cancel)**: Bootstrap `.btn-outline-secondary` — used for cancel/dismiss in modals.
- **Disabled state**: `opacity: 0.6`, `pointer-events: none` (Bootstrap default).
- Loading inline spinners use `.spinner-border.spinner-border-sm` inside buttons.

### Page Header Pattern
All content pages open with a `.page-header` div:
```html
<div class="page-header">  <!-- flex, align-items:center, gap:.5rem, mb:1.25rem -->
  <h4>Page Title</h4>
  <span class="badge bg-*">badge text</span>
  <!-- optional: right-aligned action button via d-flex justify-content-between -->
</div>
```

### Forms
- Use Bootstrap `.form-control`, `.form-select`, `.form-label`, `.form-text`.
- Labels: `fw-semibold` (`font-weight: 600`).
- Required markers: `<span class="text-danger">*</span>`.
- Floating-label placeholders align `text-align: end`; shift to `text-align: start` on focus.
- Validation: `.text-danger.small` for inline validation messages.
- Grid: Bootstrap `.row.g-3` with `.col-md-*` for multi-column form layouts.
- Form action buttons grouped in `.btn-group-rtl` (flex, wrap, gap `.5rem`).

### Tables
- Bootstrap `.table.table-hover.mb-0` inside `.table-responsive`.
- Header: `.thead.table-light`.
- Row actions (edit/delete): small icon-only buttons aligned `.text-end`.
- Pagination (bot conversations): Bootstrap outline buttons + active `.btn-primary`.

### Modals
Inline Bootstrap modals (not using JS) rendered conditionally:
- Backdrop: `rgba(0,0,0,.4)` via inline style.
- `.modal-dialog` standard sizing.
- Delete confirm modals use `.modal-title.text-danger` with `bi-exclamation-triangle` icon.
- Save/create buttons are `.btn-success`; cancel is `.btn-outline-secondary`.

---

## Custom Components

### PersianDatePicker
A custom RTL Persian (Jalali) calendar picker built in Blazor:
- **Trigger**: `.form-control`-styled input with a calendar icon (`bi-calendar3`) on the left side (absolute positioned at `left: 10px`).
- **Popup**: Absolute positioned below the input (`top: calc(100% + 4px)`), white background, `border-radius: 0.5rem`, `box-shadow: 0 4px 16px rgba(0,0,0,.15)`, `min-width: 300px`, `z-index: 1000`.
- **Grid**: 7-column grid for days, 4-column grid for months/years.
- **States**:
  - Today: `#e3f2fd` background, bold, `#1b6ec2` text.
  - Selected: `#1b6ec2` background, white text.
  - Other month: `#adb5bd` text.
  - Hover: `#e9ecef` background.
- **Overlay**: Full-screen transparent overlay (`z-index: 999`) closes the popup on outside click.

### NotificationContainer
Toast-style notification system fixed on-screen:
- Default position: **top-right** (`top: 1rem; right: 1rem`).
- Max width: `380px`. Each notification minimum `280px`.
- Slide-in animation: `translateX(20px)` → `0`, `opacity 0→1`, duration `0.25s`.
- Four severity levels: success / error / warning / information (see color table above).
- Each notification has a colored `4px` right-side border, icon, text, and close (✕) button.
- Auto-dismiss handled in the Blazor service layer.

### ReconnectModal
Blazor Server reconnect dialog using the native `<dialog>` element:
- Centered, `20rem` wide, appears at `20vh` from top.
- White background, `border-radius: 0.5rem`, shadow `0 3px 6px 2px rgba(0,0,0,.3)`.
- Animated: slides up + fades in on open (`1.5s`); fades out on close (`0.5s`).
- Retry/Resume button: `#6b9ed2` background, `border-radius: 4px`.
- Rejoining animation: two concentric ripple circles (`#0087ff`, `border-radius: 50%`).

---

## Pages

| Route | Page | Layout |
|---|---|---|
| `/login` | ورود | Centered card (no sidebar) |
| `/` | صفحه اصلی — آمار سفارشات | Chart.js bar/line chart inside a card |
| `/customer/add` | افزودن / ویرایش مشتری | Form card with multi-column row grid |
| `/customer/import` | وارد کردن مشتریان | File upload + validation alert card |
| `/bot/conversations` | مکالمات ربات بله | Table list → chat bubble view |
| `/order/statuses` | مدیریت وضعیت سفارش | Table + inline add/edit/delete modals |
| `/settings/users` | مدیریت کاربران | Two-column: user list card + permissions card |
| `/settings/menu-access` | دسترسی منو | Two-column: user list + menu checkbox panel |
| `/settings/apikeys` | مدیریت کلیدهای API | Table + create/delete modal |
| `/access-denied` | دسترسی ممنوع | Simple message |

### Login Page
- No sidebar. Full-viewport centered card (`min-height: 100vh`).
- Card: `col-11 col-sm-8 col-md-5 col-lg-4`, `shadow-sm`, `border-0`, `p-3 p-md-4`.
- Header: `bi-tree-fill text-success` (2rem), product name in `text-primary fw-bold`, subtitle in `text-muted small`.

### Chat Bubble View (Bot Conversations)
User and bot messages displayed as chat bubbles:
- Container: `max-height: 65vh`, `overflow-y: auto`, `background: #f8f9fa`, `border-radius: 0.5rem`, `border: 1px solid #dee2e6`.
- **User bubble**: `background: #0d6efd` (Bootstrap blue), white text, `border-bottom-right-radius: 0.25rem`.
- **Bot bubble**: white background, `border: 1px solid #dee2e6`, `border-bottom-left-radius: 0.25rem`.
- Timestamps shown below each bubble with Persian date + time. Labeled with `badge bg-secondary` (bot) or `badge bg-primary` (user).
