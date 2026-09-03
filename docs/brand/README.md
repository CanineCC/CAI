# The CAI mark

The band under the glass. The five-band scale is the mark's only colour and its dominant
element; the magnifying glass lifts off it as a callout, showing the reading — the pin at
the yellow/green boundary — magnified. An independent inspection (the reader's glass:
"verify it yourself") of the standard's own scale. Deliberately at arm's length from the
Canine Development identity: no shared glyphs, and the instrument itself carries no brand
colour at all.

- `cai-mark.svg` — the master. Ring, handle and pin outline are drawn in `currentColor`,
  so ONE file is black on a light ground and white on a dark one; the band is the only
  fixed colour. Inline it (imprint inlines vector assets) — referenced as an <img> it
  loses the ink inheritance.
- `cai-favicon-64.png` — the app-icon cut: the mark on a dark rounded tile, because a
  browser tab's ground is unknown and currentColor cannot reach a raster.
- `cai-og.png` / `og-card.html` — the 1200×630 share card and the HTML that renders it
  (headless Chrome at 1200×630).

Band colours: `#c94f43 #d97f3e #c9a13b #55a06c #2e8f75`. Pin fill white in every rendition.
