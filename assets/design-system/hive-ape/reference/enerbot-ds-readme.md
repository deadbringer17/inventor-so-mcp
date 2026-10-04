# EnerBot Design System

**EnerBot** — _Adaptive Platform for Energy_. EnerBot industrializes ancillary services for solar PV (photovoltaic) plants: autonomous robotic dry-cleaning, continuous monitoring software, and repeatable field operations. The brand positioning is serious, engineering-grade, field-tested — **"anni di campo, non di slide"** ("years in the field, not on slides").

The product marketing is in **Italian**. Copy is precise, industrial, measurable. The primary product is **A.P.E.** (Adaptive Platform for Energy) — a gantry-style robot that cleans PV strings waterless. A second product, **Mu.Lo.** (Multi-purpose Loader), is in pilot.

---

## Sources

- **Codebase** (attached, read-only): `enerbot/project/` — single landing page `landing.html` built as a handoff bundle from Claude Design. That file is the source of truth for colors, type, spacing, motion, and UI patterns. A full copy is at `reference/landing.html` in this project.
- **Fonts**: `enerbot/project/font/Satoshi_Complete/` — Satoshi was bundled with the handoff but is **not** actually used in the landing. The landing uses **Playfair Display + Inter + JetBrains Mono** (Google Fonts). We follow the landing, not the bundled font. _If the brand should shift to Satoshi, the user should flag it._
- **Assets**: robot photography, logo, Twin Cell diagram. All copied to `assets/`.
- **No Figma** was attached.
- **No slide deck** was attached — slides folder is intentionally absent.

---

## Index / Manifest

| File | Purpose |
|---|---|
| `README.md` | This file — brand overview + content & visual rules |
| `colors_and_type.css` | CSS custom properties for color + type, plus semantic classes |
| `SKILL.md` | Agent Skill entry point (cross-compatible with Claude Code) |
| `assets/` | Logos, robot photography, Twin Cell protocol image |
| `reference/landing.html` | Pristine copy of the source landing page — the canonical visual reference |
| `preview/` | Small HTML cards populating the Design System tab |
| `components/` | Exported Design Components (`Navbar`, `Button`, `Eyebrow`, `MetricStat`) — `.jsx` + `.d.ts` + `@dsCard` preview each |
| `ui_kits/website/` | Re-usable React/JSX components for EnerBot marketing surfaces + a demo `index.html` |

---

## CONTENT FUNDAMENTALS

### Language
- **Primary language: Italian.** All marketing copy is Italian. Headlines, CTAs, form labels, legal — Italian throughout. English appears only in product names, technical tags, and mono-caps labels (e.g. `FIELD-READY`, `CYCLE 01`, `LIVE DEMO`).
- Common English loanwords kept in English: _pilot, payback, dashboard, roadmap, utility-scale, SLA, O&M, AI._

### Voice & tone
- **Industrial, engineering-first, measured.** Claims are always quantified: _`+17%` recupero, `0 L` water, `<17m` payback, `3.240 m²/h` throughput._
- **Third-person corporate + first-person plural ("noi / crediamo")** for vision statements. Never "io". Reader is addressed with **"tu" / "tuo"** in CTAs ("_Valuta EnerBot per il tuo impianto_", "_ti rispondiamo entro 24 ore_") — direct, professional, not stiff "Lei".
- **Anti-hype.** The tagline "_Anni di campo, non di slide_" literally refuses slide-deck marketing. Avoid superlatives ("best", "revolutionary"). Prefer demonstration: numbers, test logs, validation protocols.
- **Pair a bold claim with the mechanism.** "_Da costi opachi al processo industriale_" is followed by _how_ (robotics + software + protocols).

### Casing
- **Headlines (H1/H2): sentence case** in Italian — `Industrializziamo i servizi ausiliari per l'energia solare.` First letter capital, proper nouns capital, everything else lowercase. Always end with a **period**, even in large display type.
- **Eyebrows, kickers, labels, mono tags: ALL CAPS** with wide tracking (`.14em–.16em`). Numbered: `01 — VISIONE`, `02 — IL PROBLEMA`.
- **Product names preserve their dots:** `A.P.E.`, `Mu.Lo.` — always written with periods.
- **Section captions like `CYCLE 01 · 00:00`, `A.P.E. · LIVE DEMO`** use mono + caps + middle-dot separators.

### Emphasis pattern — "the italic gold"
Every hero / section headline has exactly **one** italic serif phrase, usually colored `--primary` (teal) on light surfaces or `--accent` (gold) on dark surfaces. Examples:
- "…_i **servizi ausiliari**_ per l'energia solare." (gold, on dark)
- "Dai costi opachi al _**processo industriale**_." (teal-italic, on light)
- "Anni di campo, non di _**slide**_." (gold-italic, on dark)

This is the single most recognizable typographic gesture in the brand. Use it once per heading, never twice.

### Numbers
- European formatting: **comma as decimal** (`1,7%`), dot as thousands (`3.240 m²`).
- Units are normal-weight inline: `+10–20%` (em-dash range), `0 L`, `<17m`, `+17%`.
- The big display metric uses **Playfair Display serif**, often gold, with the unit as smaller `<sub>` in sans-serif.

### Emoji
- **None.** Not used anywhere in the brand. Avoid entirely. Substitute mono-caps tags, middle-dot glyphs (`·`), or icon-free numbered prefixes.

### Vibe
Serious, confident, slightly austere. Think: engineering report that happens to be beautiful. Nothing bubbly, nothing playful. The only "warmth" in the system is the single gold accent — used like a highlighter pen, never like decoration.

---

## VISUAL FOUNDATIONS

### Color
- **Two palettes, one system.**
  - **Light surfaces:** cool industrial off-whites (`--bg #f4f8fb`, `--bg-2 #eaf2f7`, `--surface #ffffff`). Text is navy-ink `#102235`. Borders are translucent teal `rgba(50,105,117,0.10–0.16)` — never gray.
  - **Dark surfaces:** deep teal-navy (`--ink-1 #0d2030`, `--ink-2 #1a3a4a`, `--primary-dark #1a3a4a`). Used for hero, proof bar, traction, CTA, footer. Body text `rgba(255,255,255,.72)`, meta `rgba(255,255,255,.5)`.
- **Primary** is a muted industrial teal `#326975` — never bright. Hover goes _darker_, not lighter (`#285660`).
- **Accent** is saturated signal-yellow `#fdd11b`. Used ONLY for: the hot CTA button, the italic serif emphasis, headline numbers / metrics, and status dots. Never fills a whole background, never a card. On light surfaces, accent text uses `--accent-dark #c07a00` for readability.
- **No purple, no blue-purple gradients, no rainbow palettes.** Teal + navy + one signal yellow is the entire story.

### Typography
- **Playfair Display** (serif, 600/700 + italic) — all H1/H2/H3, display numbers, italic emphasis phrases.
- **Inter** (sans, 300–800) — body, H4, UI, form labels, nav.
- **JetBrains Mono** (mono, 400/500/700) — eyebrows, tags, meta captions, legal, technical labels, timestamps.
- **Ratio rule:** a section has ~one Playfair headline, ~one mono eyebrow, ~one mono caption, everything else Inter body. The three fonts are always visible together — that's the identity.

### Spacing & layout
- **Max container width: `1240px`**, side padding `32px`.
- **Section padding**: `clamp(70px, 8vw, 120px)` vertical — very generous vertical rhythm.
- **Section head**: `max-width: 760px`, 18px gap between eyebrow → h2 → lead p.
- **Fixed header** (72px tall, dark-translucent + 18px backdrop-blur). Content clears with `padding-top: calc(var(--header-h) + 80px)`.
- Grids are mostly **2 or 3 or 4 columns** with `20–24px` gaps on cards, `40–80px` gaps on major splits. Break to single column under `900–960px`.

### Backgrounds
- **Flat fills dominate.** No gradients on cards or normal surfaces.
- **Hero & CTA use radial-gradient "spotlight" backgrounds:** one warm pool of `rgba(253,209,27,.06–.12)` off-center, plus a darker teal pool, over a diagonal navy `linear-gradient(165deg, #1a3a4a → #0d2030)`. The overall impression is industrial dusk.
- **Grid mesh overlay** on dark hero sections: two orthogonal 1px white-alpha lines at 56px spacing, masked so it fades out toward the bottom (`mask: linear-gradient(to bottom, black 0%, black 60%, transparent 100%)`).
- **Imagery is full-bleed inside cards and the hero-visual**, never floated/clipped into weird shapes. Corner decorations (4 small L-shaped brackets in accent gold) frame the hero image.
- **No hand-drawn illustrations, no patterns, no textures** (except the simulated solar-panel SVG scene in the ape-band, which is product-specific).

### Animation
- **Easing:** one curve everywhere — `cubic-bezier(.16, 1, .3, 1)` aliased to `--ease`. Snappy on start, glides to stop.
- **Durations:** `.2s` micro (hover color), `.24s` buttons, `.3s` cards, `.6s` images on hover, `.7s` reveal-on-scroll. Nothing >1s except the 10s A.P.E. demo loop.
- **Reveal-on-scroll:** `opacity: 0 → 1` + `translateY(24px) → 0`, triggered via IntersectionObserver (`threshold: .12`). Applied to headlines, card grids, log tables.
- **Pulse:** live-status dots pulse `opacity 1 → .4` on a 1.8–2s loop.
- **No bounces, no springs, no parallax.** Motion is engineering-calm.

### Hover states
- **Buttons:** `translateY(-2px)` + stronger shadow. Primary gets darker (`--primary-hover`). Gold gets `filter: brightness(1.06)` + bigger gold glow.
- **Cards (`.prob-card`, `.layer`, `.competency`):** `translateY(-3 to -4px)` + `--shadow-md` + border tightens from `--border-soft` to `--primary` or `--border`.
- **Pillar rows:** `translateX(+4px)` (horizontal nudge, not vertical).
- **Product cards:** background image `scale(1.04)` + opacity `.55 → .65`.
- **Nav links:** color-only `.7 → 1.0` white, no background chip.

### Press states
- Inherits the hover transform but removes the shadow on `:active`. Not elaborately styled — keep minimal.

### Borders
- **Light surfaces:** 1px solid `--border-soft` (4% teal) on cards; on hover tightens to `--border` (10%) or `--primary`.
- **Dark surfaces:** 1px solid `rgba(255,255,255,.06–.14)`.
- **Dashed dividers** inside layer cards: `1px dashed var(--border)` between description and feature list.
- **Accent dividers**: sections topped with `2px solid rgba(253,209,27,.3)` on the proof bar — used once, not everywhere.

### Shadows
- Three-step elevation system only:
  - `--shadow-sm` → default card
  - `--shadow-md` → hovered card
  - `--shadow-lg` → hero media, modal-like surfaces
- All shadows are **cool navy** (`rgba(16,34,53,...)`) — never neutral gray, never warm.
- On dark backgrounds, shadows become `rgba(0,0,0,.4–.6)` for contrast.

### Capsules vs protection gradients
- **Dark photography → text legibility:** always a `linear-gradient(to top, rgba(13,32,48,.95) 0%, rgba(13,32,48,.55) 60%, transparent 100%)` bottom gradient on hero / product cards. Never flat tint.
- **Small labels over photography:** capsule / pill with `background: rgba(13,32,48,.55–.85)` + `backdrop-filter: blur(10–18px)` + 1px white-alpha border + mono-caps text. Called `.chip` / `.hv-caption .tag` / `.protocol-tag`.

### Transparency & blur
- `backdrop-filter: blur(10–20px)` is a core motif: sticky header, floating chips, form card (`.cta-form`), mini-stats, protocol caption. Always paired with a semi-opaque dark fill.
- **Pills on light:** `rgba(253,209,27,.15)` with `rgba(253,209,27,.35)` border — accent-tinted, not gray.

### Color vibe of imagery
- Warm-neutral with **slight saturation boost** (`filter: saturate(1.05) contrast(1.02)`). Landscapes of solar plants, industrial detail shots, robot-on-panels. Never black & white, never cool-blue, never heavily graded. Grain is not added.
- Images are **cropped tight on hardware and fields** — the robot, the panels, the dust.

### Corner radii
- `--r-sm 6px` — buttons, form inputs, small tags
- `--r-md 12px` — cards, step rows, floating tags
- `--r-lg 20px` — hero visuals, dark value columns, big CTAs, traction stats grid
- `100px` (pill) — badges, eyebrows with backgrounds, soiling bars
- **Never sharp 0-radius on content surfaces** except hairline dividers.

### Card anatomy
Standard card: `padding: 28–40px` · `background: var(--surface)` · `border: 1px solid var(--border-soft)` · `border-radius: var(--r-md)` · `box-shadow: none` at rest. On hover → `shadow-md` + translate + border tightens.

Dark hero card: `background: var(--primary-dark)` · `radius: var(--r-lg)` · `border: 1px solid rgba(255,255,255,.06–.1)` · inner gold radial in top-right corner via `::after`.

### Fixed / sticky
- Header is the only `position: fixed` element.
- Floating badges on hero image (`hero-tag-tl`) and status indicators on A.P.E. scene (`ape-indicator`) — absolutely positioned within their parent, not sticky.

### Layout rules
- Text columns: **max-width `520–680px`** on body copy. Never full-bleed paragraphs.
- **Grids break at 960px / 900px** to single column.
- **Asymmetric section heads** (h2 on left, pill or caption on right) are used for Problem and Platform sections.
- **Section numbering** (`01 — VISIONE`, `02 — IL PROBLEMA`) is a navigation aid — keep it on every major section.

### Iconography
See **ICONOGRAPHY** section below.

---

## ICONOGRAPHY

### Philosophy
**EnerBot uses almost no icons.** The landing page has zero decorative SVG icons — no chevrons, no check-marks, no card icons, no social icons, no product icons. Instead it uses:

1. **Mono numeric prefixes** — `01 — VISIONE`, `P · 01`, `LAYER 01`, `CYCLE 01`, `STEP · 01`. This is the primary navigation icon system.
2. **Serif-italic letter symbols** in the team competency grid — `R.` `E.` `I.` (for Robotica, Energia, Industrializzazione), oversized Playfair italic in primary teal.
3. **Middle-dot glyphs** `·` as separators in mono-caps meta lines: `A.P.E. · LIVE DEMO`, `CYCLE 01 · 00:00`, `Robotics · Software · Operations`.
4. **L-shaped corner brackets** — 4 small (`22×22px`) 1.5px-stroke borders in accent gold, placed at each corner of the hero image. Pure CSS, no SVG.
5. **Status dots** — 6–10px solid-color circles with a matching `box-shadow` glow. Green `#2ee08e` for active/live, gold `#fdd11b` for in-progress, with a 2s `pulse` keyframe.
6. **The arrow `→`** — a literal Unicode right-arrow character inside button labels. On hover it translates 3px to the right (`.btn:hover .arrow { transform: translateX(3px) }`). This is the only "icon" that animates.

### No icon font, no icon library
There is no Lucide, no Heroicons, no Font Awesome, no custom icon sprite. The codebase imports zero icon dependencies.

### Bespoke SVG is used — but only for illustration
The A.P.E. scene banner (`.ape-band`) contains a large hand-authored SVG depicting the robot cleaning a solar string with animated dust accumulation. This is a **scene illustration**, not reusable iconography. Treat it as a one-off asset.

### If you need to add an icon
Avoid if at all possible — see if a mono label, number, or middle-dot separator works first. If you truly need one:
- Match stroke-weight **1.5px**, rounded caps, outline-only (not filled).
- Use `currentColor` so it inherits text color.
- Keep it 14–18px — tiny.
- Do **not** put it in a colored circle or square background. Icons are inline text glyphs in this system.
- CDN fallback: **Lucide** (`https://unpkg.com/lucide@latest`) has the closest stroke-weight match. This is a _substitution_, not a match — flag it when you use it.

### Logo
- `assets/Logo_mark.svg` — **crisp vector icon mark** (hexagonal symbol, original colors: dark facets `#394149/#1d252c`, gold `#fdcf1d`, teal `#3f8997`). This is the primary logo asset — use it on **light surfaces**, paired with the "EnerBot" wordmark set in Satoshi 700. The dark facets disappear on dark backgrounds, so on dark surfaces use a light/contained container or the wordmark-on-dark treatment.
- `assets/Logo_name.svg` / `assets/logo.png` — older full wordmark + icon lockup (raster + vector). Still used in the website UI kit header (works on dark via `filter: brightness(1.2)`).
- **Never recolor the logo** — its colors are fixed brand equity.

### Photography as iconography
Several full-bleed photos serve as visual anchors instead of icons:
- `assets/technology-bridge-portrait.jpg` — hero (portrait aspect)
- `assets/capability_robot.jpg`, `operations_aerial.jpg`, `modular_robot.jpg`, `performance_robot.jpg` — product cards
- `assets/TwinCells.png` — Dual-Cell protocol diagram
- `assets/protocol-brush-detail.jpg`, `traction-plant-overview.jpg`, `hero-ape-wide.jpg`, `contact-robot-square.jpg` — contextual full-bleed shots

---

## Font note & substitution flag

> **⚠ Font substitution:** the handoff bundled the **Satoshi** OTF/TTF/WOFF family, but the landing page itself pulls **Playfair Display + Inter + JetBrains Mono from Google Fonts**. This design system follows the landing. The Satoshi files are not referenced. If the intended brand system actually uses Satoshi, please confirm — we'll swap the body/sans face.

All three font families load reliably from Google Fonts, so no local font file hosting is needed.
