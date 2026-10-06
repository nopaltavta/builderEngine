HOW TO GIVE MATERIALS REAL TEXTURES
=====================================
Drop your own photos here and they replace the built-in
procedural patterns. No restart needed: click
Model > Textures (or restart the app).

FILE NAMES (exact, lowercase — one per material):
  grass.png    wood.png    marble.png    granite.png
  brick.png    sand.png    grid.png
(.jpg also works: grass.jpg, ...)

RULES THAT MATTER:
1. SEAMLESS / TILEABLE. Each texture repeats every stud,
   so edges must wrap invisibly. Search "seamless texture".
   A non-tiling photo shows visible grid seams.
2. SQUARE, 256–1024 px. 512x512 is the sweet spot.
   Bigger than 1024 just wastes VRAM.
3. MID-BRIGHT colors. The part color multiplies the texture,
   so keep textures light/neutral: white part = true colors,
   green part x grass photo = dark green mush. When in doubt,
   desaturate a little.
4. ALBEDO ONLY. No lighting baked in (no shadows/highlights
   painted into the image) — the engine lights it.

WHERE TO GET THEM (free, no account, CC0 — safe to publish):
  ambientCG.com  (>2000 seamless PBR sets, grab the Color map)
  polyhaven.com/textures  (same idea, "Diffuse" map)

PUBLISHING: this whole folder is copied next to your game
automatically, so players see your textures too.

DELETE a file to fall back to the built-in pattern for it.
