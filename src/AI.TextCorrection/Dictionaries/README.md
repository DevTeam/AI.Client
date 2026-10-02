# Embedded dictionaries

Source: https://github.com/wooorm/dictionaries

Pinned upstream commit: `8cfea406b505e4d7df52d5a19bce525df98c54ab`.
Downloaded on 2026-10-02. The `.dic`, `.aff`, license, and upstream readme files
are stored without modifications. All dictionaries use UTF-8.

- `en`: English (US), SCOWL-derived Hunspell dictionary.
- `ru`: Russian, Alexander I. Lebedev's dictionary with upstream modifications.
- `fr`: French (classic), Olivier R./Grammalecte, MPL-2.0.
- `es`: Spanish (Spain), Santiago Bosio and contributors. Upstream offers
  GPL-3.0-or-later, LGPL-3.0-or-later, or MPL-1.1-or-later; this distribution
  chooses MPL-2.0 under that MPL option.

The MPL-2.0 text is included in `MPL-2.0.txt`. The French and Spanish dictionary
files retain their own licenses and copyright notices; the application's license
does not replace them. Corresponding source files are included unmodified in this
repository and available at
https://github.com/wooorm/dictionaries/tree/8cfea406b505e4d7df52d5a19bce525df98c54ab/dictionaries/fr
and
https://github.com/wooorm/dictionaries/tree/8cfea406b505e4d7df52d5a19bce525df98c54ab/dictionaries/es.

Each directory contains its own copyright and redistribution conditions in
`license`. License notices are copied to build and publish output.

The reader is WeCantSpell.Hunspell 7.0.1:
https://github.com/aarondandy/WeCantSpell.Hunspell
Its license notice is included as `WeCantSpell.Hunspell.license.txt`.

These dictionaries belong to the application's layout analyzer. They do not
configure the native browser/WebView spellchecker.

Run the build tool's `prepare-text-correction` command manually after importing dictionaries.
It derives `index.trigrams` files from these unmodified word lists. Commit the indexes
with the dictionaries; ordinary builds embed them alongside `.dic`/`.aff` resources.
The indexes retain the corresponding
dictionary's license; generating them does not change the dictionary's copyright
or replace its license with the application's Apache-2.0 license. The generator
and versioned format are available in this repository under `build/Targets` and
`src/AI.TextCorrection/Resources`; the SHA-256 source fingerprint is stored in each index.
