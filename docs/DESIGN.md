# DESIGN.md — visual and interaction specification

## 0. Brief

**Subject.** Norwegian household electricity: hydropower, winter darkness, a bill that arrives monthly and is mostly incomprehensible.

**Audience.** A Norwegian adult with a phone, standing in a kitchen at 22:30, deciding whether to start the dishwasher now or in the morning. Not an energy trader. Not a dashboard enthusiast. Someone who wants one sentence and will give the screen three seconds.

**Primary job.** Answer *"when, and does it even matter for me?"* — then get out of the way.

**The design consequence.** The interface is a **timetable**, not a dashboard. The day is a physical strip of time you read left to right. Cost is a property of a position on that strip. Everything else — bills, model accuracy, settings — is a secondary surface.

---

## 1. Design principles, specific to this product

1. **Anchor colour to the user's break-even, not to the day's min and max.** Every other price app normalises the colour scale to the visible range, which makes a completely flat cheap day look as dramatic as a spike day. Spotlys anchors the scale at the user's actual indifference price — the strømstøtte threshold, or the Norgespris rate, or their break-even after support. A boring day *looks* boring. This is the single most important design decision in the product and it comes straight from the domain.
2. **If it doesn't matter for this user, desaturate it.** A Norgespris household under the monthly cap sees the spot ribbon rendered in flat grey-blue with a one-line explanation, and the *grid tariff* band below it carries all the colour. The UI refuses to dramatise a number that cannot change this person's bill.
3. **Uncertainty is drawn, never hidden.** Forecast hours are a fan, not a line. The further out, the more the ribbon dissolves toward its edges. You can see the model's confidence without being told about it.
4. **One number, one range, one counterfactual.** Never a bare hero figure. "38–52 kr, versus 71–94 kr if you plug in at 17:00."
5. **Dark by default.** This is an app about night hours, used in the evening, in a country that is dark for five months. Light mode exists and is good; dark is the canonical rendering.

---

## 2. Colour

A single continuous ramp from **deep water** through **ice light** on the cheap side, and from neutral into **ember** on the expensive side, diverging at the anchor price. Cold = cheap is not arbitrary here: it is meltwater, night, and surplus hydro. Warm = expensive is load, heat, scarcity.

### Core palette

| Token | Hex | Role |
|---|---|---|
| `--fjord-900` | `#06202B` | Page ground |
| `--fjord-800` | `#0B2C3A` | Panel ground |
| `--fjord-700` | `#113E50` | Raised surface, ribbon base |
| `--fjord-600` | `#1B5A70` | Borders, inactive strokes |
| `--ice-300` | `#8FD6E8` | Cheapest end of the ramp, primary accent |
| `--ice-100` | `#D9F1F7` | Primary text on dark |
| `--ember-400` | `#E5A45F` | Expensive |
| `--ember-600` | `#C05635` | Extreme / spike |
| `--birch-200` | `#C9D4D8` | Secondary text |
| `--moss-400` | `#6F9E72` | Confirmed / success (used sparingly, never as "cheap") |

`--moss-400` is deliberately *not* on the price ramp. Green means "this is settled / confirmed" (the auction has cleared), never "this is cheap". Separating those two meanings is why the interface doesn't read as a traffic light.

### The price ramp

```
                  ANCHOR (user's break-even)
  cheap ◄──────────────────┼──────────────────► expensive
  #8FD6E8  #4E9DB5  #23687F │ #6B6F70  #E5A45F  #C05635
   ice      shallow   deep  │  slack     ember    spike
```

Implemented as an interpolation in **Oklch**, not sRGB, so the ramp is perceptually even and the midpoint doesn't turn muddy. `culori` or a hand-written `oklch()` interpolation; CSS `oklch()` has universal support now.

### Light mode

Not an inversion. Ground becomes `#EEF4F6` (glacier), the ramp keeps the same hues but shifts lightness down so the ice end stays legible on a pale ground. Two separate ramps, both authored, neither generated.

---

## 3. Typography

**Schibsted Grotesk** — interface, data, headlines. A Norwegian open-source grotesk with real character in its `a`, `g` and numerals; chosen because it is from this country and carries a Scandinavian editorial voice rather than the neutral Inter default. Variable weight 400–700. **`font-variant-numeric: tabular-nums` globally on anything numeric** — a price column that shifts width as digits change is the mark of an amateur data UI.

**Newsreader** — the explanatory voice. Used only for the "why" text: the sentence under the ribbon that explains what's driving the price, the regime advisor's reasoning, the model page's prose. A serif for explanation and a grotesk for data is a real distinction that maps onto a real difference in content type, not decoration.

### Scale

Modular, ratio 1.25, base 16 px.

| Token | Size / line-height | Face | Use |
|---|---|---|---|
| `--t-hero` | 44/48, weight 600, tracking −0.02em | Grotesk | The headline number of the day |
| `--t-xl` | 28/34, 600 | Grotesk | Screen titles |
| `--t-lg` | 20/28, 500 | Grotesk | Section heads |
| `--t-body` | 16/26, 400 | Grotesk | UI body |
| `--t-prose` | 17/30, 400 | Newsreader | Explanation paragraphs |
| `--t-data` | 15/20, 500, tabular | Grotesk | Prices, kWh, kr |
| `--t-micro` | 13/18, 500 | Grotesk | Axis labels, timestamps |

No all-caps labels anywhere. No eyebrow text above headings. Line length capped at 68 characters for grotesk prose, 74 for Newsreader.

---

## 4. Layout

### Home — "the ribbon"

```
┌──────────────────────────────────────────────────────────┐
│  Spotlys                        NO2 · Kristiansand   ⚙   │
├──────────────────────────────────────────────────────────┤
│                                                          │
│   Billigst i natt, 02–06.                                │  ← t-hero, plain sentence
│   Oppvaskmaskinen koster 2,10 kr nå, 0,80 kr kl. 03.     │  ← t-prose (Newsreader)
│                                                          │
├──────────────────────────────────────────────────────────┤
│   I DAG                          ▼ NÅ                    │
│  ▓▓▓▓░░░░▒▒▒▒████▓▓▓▓░░░░░░░░▒▒▒▒▓▓▓▓████               │  ← ribbon, 24 cells
│  00      06      12      18      24                      │
│                                                          │
│   I MORGEN                    · bekreftet 13:02          │
│  ▓▓▓▓░░░░░░░░▒▒▒▒▓▓▓▓████▓▓▓▓░░░░░░░░▒▒▒▒               │
│                                                          │
│   ONSDAG–SØNDAG                  · varslet               │
│  ▓▓▒▒░░▒▒▓▓▒▒░░  ← fan widens, edges dissolve            │
├──────────────────────────────────────────────────────────┤
│   Nettleie i dag: natt fra 22:00      Din topp: 4,2 kW   │
│   ────────────────────────────────    trinn 2 av 3 brukt │
├──────────────────────────────────────────────────────────┤
│   [ Planlegg lading ]       [ Lønner Norgespris seg? ]   │
└──────────────────────────────────────────────────────────┘
```

Left-aligned throughout. No centred text except inside buttons. The ribbon spans full bleed on mobile — the day should touch both edges of the phone, because the day doesn't have margins.

**The ribbon itself.** 24 cells per day, each cell a vertical bar of *constant height* whose **fill colour** encodes price and whose **height of an inner cap** encodes uncertainty. Not a bar chart: bar height competes with colour and forces the eye to compare lengths across a non-zero-anchored axis, which is exactly the comparison that misleads. A constant-height band with an anchored colour ramp is read positionally — "this part of the night" — which is how people actually think about the question.

**Hour detail.** Tap or scrub a cell: a floating readout follows it showing hour, price after support, nettleie rate for that hour, and the cost of a named appliance. Keyboard: arrow keys move the cursor, Home/End jump to day bounds.

### Plan view

Seven-day ribbon stacked as seven rows (a calendar heat-strip), with the optimizer's chosen window drawn as a bracket that **snaps** across cells when inputs change. Below it: the two-number comparison and the constraint that bound the answer ("begrenset av at bilen skal være klar 07:00", or "begrenset av effekttrinnet ditt").

### Bill / regime advisor

A single horizontal comparison: two stacked bars, Norgespris vs spot + strømstøtte, decomposed into energy / nettleie / avgifter, with the uncertainty of the forecast-driven part drawn as a hatched extension rather than a solid segment. Under it, in Newsreader, three sentences of reasoning and a link to Elhub. This screen is allowed to be text-heavy; it's the screen people arrive at with a real decision.

### Model page

Public, no auth. Skill score by zone and lead time, calibration curves, forecast-vs-realised for the last 30 days, and the decision-regret comparison. Styled as an editorial page, not a dashboard — Newsreader prose between the charts explaining what each one means and where the model is weak.

---

## 5. Motion

One orchestrated moment, then silence. Everything else responds to input.

### Tokens

```css
--dur-instant: 90ms;    /* state flips: toggle, checkbox */
--dur-quick:  160ms;    /* hover, focus ring, tooltip */
--dur-base:   240ms;    /* panel, readout, bracket snap */
--dur-slow:   420ms;    /* view transition */
--dur-tide:   760ms;    /* the one orchestrated entrance */

--ease-out:   cubic-bezier(0.16, 1, 0.3, 1);      /* arrivals */
--ease-in-out:cubic-bezier(0.65, 0, 0.35, 1);     /* moves */
--ease-snap:  cubic-bezier(0.34, 1.4, 0.64, 1);   /* the optimizer bracket only */
```

### The entrance — "tide"

On first paint of the home screen, and only there:

1. `0 ms` — the ribbon exists as `--fjord-700` base, no colour.
2. `0 → 620 ms` — colour floods in **left to right**, one cell at a time, 18 ms stagger, each cell fading and rising 4 px, `--ease-out`. Reads as the tide coming in across the day.
3. `560 ms` — the *now* marker drops from above into its hour, `--dur-base`, `--ease-snap`, and the cell under it brightens once.
4. `700 ms` — the headline sentence fades up 8 px.

Total 760 ms, once per session (sessionStorage flag). Not on every navigation, not on every card, not on scroll. **No scroll-triggered reveals anywhere in the app** — they are the single clearest tell of a generated page and they make a utility app feel like a landing page.

### User-triggered motion (the real work)

| Interaction | Behaviour |
|---|---|
| Scrub the ribbon | Readout follows with `--dur-instant` position interpolation; no easing lag, it must feel attached to the finger |
| Optimizer inputs change | The window bracket **travels** to its new position with `--dur-base` / `--ease-snap`; cells it covers lift 2 px. You see the answer *move*, which teaches the relationship between constraint and result |
| Auction publishes (13:0x) while open | Tomorrow's ribbon cross-fades from fan to confirmed over `--dur-slow`, and the "varslet" label wipes to "bekreftet". A live data update is an event worth one animation |
| Regime toggle | The two bars re-decompose with a flip of the shared layout (`layoutId`), `--dur-base` |
| Loading | No spinner. The ribbon renders in base colour with a slow 1.8 s luminance breathe — the shape of the answer is there before the data is |

### Reduced motion

`prefers-reduced-motion: reduce` collapses all of the above to opacity-only transitions at `--dur-quick`, and the tide entrance to a single 120 ms fade. Handled once in a `MotionConfig`, not per component. The breathing loader becomes a static label. This is tested with a Playwright run under the emulated preference.

---

## 6. Accessibility

Non-negotiable, and each item is a test.

- **Never colour alone.** Every ribbon cell carries its price in the accessible name; the detail readout is always available; a `<table>` rendition of the day is present in the DOM (visually hidden, toggleable with a visible "Vis som tabell" control that isn't hidden).
- **Contrast.** All text ≥ 4.5:1 against its actual background, verified per ramp stop — the ice end of the ramp needs dark text on it, the deep end light. Automated check in CI over the generated ramp stops.
- **Keyboard.** The ribbon is a single composite widget: one tab stop, arrow keys to move, Enter to pin the readout. `role="group"` with `aria-describedby` giving a summary sentence ("24 timer, billigst 03:00 til 0,42 kroner, dyrest 08:00 til 2,11 kroner").
- **Focus.** A visible 2 px `--ice-300` ring with 2 px offset on every interactive element. Never `outline: none` without a replacement.
- **Colour vision.** The ice→ember ramp is distinguishable under deuteranopia and protanopia (blue–orange, not red–green). Verified with a simulation screenshot committed to the repo.
- **Motion.** As above.
- Axe-core assertions in the Playwright suite; a failure fails the build.

---

## 7. Copy

Norwegian bokmål is the product language; English is a full second locale (both because the portfolio audience may not read Norwegian, and because localisation done properly is a skill worth showing). Resource files, no string concatenation, ICU message format for plurals.

Voice rules:
- Sentence case everywhere. No shouting.
- Say the thing: "Billigst i natt, 02–06." Not "Optimal forbrukstid identifisert."
- Buttons name their outcome: "Planlegg lading" → the resulting screen says "Ladeplan".
- Errors explain and instruct, without apologising: "Morgendagens priser er ikke publisert ennå. De kommer vanligvis rundt kl. 13." Not "Beklager, noe gikk galt."
- Empty states invite: "Last opp forbruket ditt, så regner vi ut hva Norgespris faktisk ville kostet deg."
- Never imply certainty the model doesn't have. "Varslet" for forecasts, "bekreftet" for cleared prices, and those two words are used consistently everywhere including notifications.

---

## 8. Self-critique against the brief

Checked deliberately, because the default version of this app is very easy to produce by accident:

- **Rejected: green/red hour bars.** Traffic-light colouring on a min/max-normalised scale is what every competitor does and it lies about flat days. Replaced with a break-even-anchored ice/ember ramp.
- **Rejected: a hero KPI card with "Du sparer 432 kr!".** Invented precision on a forecast-derived number. Replaced with a range plus counterfactual.
- **Rejected: identical rounded cards in a grid.** The content isn't parallel — a ribbon, a tariff band and an advisor are three different kinds of thing and should not wear the same container.
- **Rejected: fade-and-slide-up on every section as you scroll.** Replaced with one orchestrated entrance and otherwise only response-to-input motion.
- **Rejected: Inter + a warm terracotta accent on cream.** Correct for many products; a generic default here, and wrong for a subject that is cold water and dark winter. Replaced with Schibsted Grotesk on fjord dark.
- **Removed one accessory:** an earlier draft had a live animated "current price" gauge in the header alongside the ribbon. It duplicated information the ribbon already carries at a glance. Cut. The ribbon is the one bold element; everything around it stays quiet.
